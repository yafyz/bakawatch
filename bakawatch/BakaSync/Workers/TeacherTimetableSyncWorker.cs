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
    internal class TeacherTimetableSyncWorker(
        IdSyncService idSyncService,
        TimetableService timetableService,
        TimetableNotificationService timetableNotificationService,
        BakaTimetableParser bakaTimetableParser,
        ILogger<TeacherTimetableSyncWorker> logger,
        IServiceScopeFactory serviceScopeFactory
    )
        : SyncWorkerBase
    {
        private readonly IdSyncService idSyncService = idSyncService;
        private readonly TimetableService timetableService = timetableService;
        private readonly TimetableNotificationService timetableNotificationService = timetableNotificationService;
        private readonly BakaTimetableParser bakaTimetableParser = bakaTimetableParser;
        private readonly ILogger logger = logger;
        private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

        private Dictionary<TeacherBakaId, List<CollisionLog>> collisionMap = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await idSyncService.IsInitialized;

            logger.Log(LogLevel.Information, $"Starting {nameof(TeacherTimetableSyncWorker)}");

            await Worker(stoppingToken);
        }

        private async Task DoParse(BakaContext db, TeacherTimetableSync sync, TimetableWeek week, BakaTimetableParser.When when, CancellationToken ct) {
            var ptm = await bakaTimetableParser.Get(sync.Teacher.BakaId.Value, BakaTimetableParser.Who.Teacher, when);
            var tm = await timetableService.GetTeacherTimetable(db, week, sync.Teacher.BakaId);

            await sync.ParseAndUpdateTimetable(ptm, tm, collisionMap[sync.Teacher.BakaId], ct);
        }

        private async Task Worker(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

                Teacher[] teachers = await db.Teachers.ToArrayAsync();

                TimetableWeek week = await timetableService.GetOrCreateWeek(DateOnly.FromDateTime(DateTime.Now));
                TimetableWeek nextWeek = await timetableService.GetOrCreateWeek(DateOnly.FromDateTime(DateTime.Now).AddDays(7));

                try {
                    foreach (var teacher in teachers) {
                        await WeekEdgeWait(ct);
                        if (ct.IsCancellationRequested) break;

                        if (!collisionMap.ContainsKey(teacher.BakaId)) {
                            collisionMap.Add(teacher.BakaId, []);
                        }

                        var sync = scope.ServiceProvider.GetRequiredService<TeacherTimetableSync>();
                        sync.Teacher = teacher;

                        await DoParse(db, sync, week, BakaTimetableParser.When.Actual, ct);
                        await DoParse(db, sync, nextWeek, BakaTimetableParser.When.Next, ct);

                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                } catch (BakaHttpError ex) {
                    logger.LogError(ex.InnerException, "Bakalari are down");
                } catch (BakaTimetableParser.BakaParseErrorNoTimetable ex) {
                    logger.LogError(ex, "bad response from baka");
                }

                await TakeBreak(ct);
            }
        }
    }
}
