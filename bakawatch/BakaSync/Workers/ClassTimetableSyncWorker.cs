using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using static bakawatch.BakaSync.BakaAPI;

namespace bakawatch.BakaSync.Workers {
    internal class ClassTimetableSyncWorker(
        IdSyncService idSyncService,
        TimetableService timetableService,
        TimetableNotificationService timetableNotificationService,
        BakaTimetableParser bakaTimetableParser,
        ILogger<ClassTimetableSyncWorker> logger,
        IServiceScopeFactory serviceScopeFactory
    )
        : BackgroundService {
        private readonly IdSyncService idSyncService = idSyncService;
        private readonly TimetableService timetableService = timetableService;
        private readonly TimetableNotificationService timetableNotificationService = timetableNotificationService;
        private readonly BakaTimetableParser bakaTimetableParser = bakaTimetableParser;
        private readonly ILogger logger = logger;
        private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

        private Dictionary<ClassBakaId, List<CollisionLog>> collisionMap = new();

        // range from before and after midnight,
        // so its actually double the ammount
        private double MidnightPauseRange = 10 * 60 * 1000;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await idSyncService.IsInitialized;

            logger.Log(LogLevel.Information, $"Starting {nameof(ClassTimetableSyncWorker)}");

            await Worker(stoppingToken);
        }

        private async Task DoParse(ClassTimetableSync sync, TimetableWeek week, BakaTimetableParser.When when, CancellationToken ct) {
            var ptm = await bakaTimetableParser.Get(sync.Class.BakaId.Value, BakaTimetableParser.Who.Class, when);
            var tm = await timetableService.GetTimetable(week, sync.Class.BakaId);

            await sync.ParseAndUpdateTimetable(ptm, tm, collisionMap[sync.Class.BakaId], ct);
        }

        private async Task Worker(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

                Class[] classes = await db.Classes.ToArrayAsync();

                TimetableWeek week = await timetableService.GetOrCreateWeek(DateOnly.FromDateTime(DateTime.Now));
                TimetableWeek nextWeek = await timetableService.GetOrCreateWeek(DateOnly.FromDateTime(DateTime.Now).AddDays(7));

                try {
                    foreach (var @class in classes) {
                        if (!collisionMap.ContainsKey(@class.BakaId)) {
                            collisionMap.Add(@class.BakaId, []);
                        }

                        var sync = scope.ServiceProvider.GetRequiredService<ClassTimetableSync>();
                        sync.Class = @class;

                        await DoParse(sync, week, BakaTimetableParser.When.Actual, ct);
                        await DoParse(sync, nextWeek, BakaTimetableParser.When.Next, ct);

                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                } catch (BakaError ex) {
                    // todo: properly handle baka errors
                    logger.LogError(ex, "Bakalari are down");
                } catch (System.Net.Http.HttpRequestException ex) {
                    // timeout catch number 1
                    logger.LogError(ex, "Bakalari are down, probably");
                } catch (TaskCanceledException ex) {
                    // timeout catch number 2
                    logger.LogError(ex, "Bakalari are down, probably, probably");
                } catch (BakaTimetableParser.BakaParseErrorNoTimetable ex) {
                    logger.LogError(ex, "bad response from baka");
                }

                // prevent date desync while parsing timetables
                // when on the edge of a week by pausing for a while

                var now = DateTime.Now;

                if (now.DayOfWeek == DayOfWeek.Sunday) {
                    var midnight = DateOnly.FromDateTime(now.AddDays(1)).ToDateTime(TimeOnly.MinValue);
                    var diff = midnight - now;

                    if (Math.Abs(diff.TotalMilliseconds) < MidnightPauseRange) {
                        if (diff.TotalMilliseconds > 0) {
                            // before midnight,
                            // wait for after midnight + MidnightPauseRange
                            await Task.Delay((int)(diff.TotalMilliseconds + MidnightPauseRange), ct);
                        } else {
                            await Task.Delay((int)diff.TotalMilliseconds, ct);
                        }
                    }
                }

                await Task.Delay(10_000, ct);
            }
        }
    }
}
