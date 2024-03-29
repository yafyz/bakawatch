﻿using bakawatch.BakaSync.Entities;
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
    internal class PermanentClassTimetableSyncWorker(
        IdSyncService idSyncService,
        TimetableService timetableService,
        TimetableNotificationService timetableNotificationService,
        BakaTimetableParser bakaTimetableParser,
        ILogger<PermanentClassTimetableSyncWorker> logger,
        SyncOptimizationService syncOptimizationService,
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

        private Dictionary<ClassBakaId, List<CollisionLog>> collisionMap = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await idSyncService.IsInitialized;

            logger.Log(LogLevel.Information, $"Starting {nameof(PermanentClassTimetableSyncWorker)}");

            await Worker(stoppingToken);
        }

        private async Task DoParse(BakaContext db, ClassPermanentTimetableSync sync, BakaTimetableParser.When when, CancellationToken ct) {
            var ptm = await bakaTimetableParser.Get(sync.Class.BakaId.Value, BakaTimetableParser.Who.Class, when);
            var tm = await timetableService.GetPermanentClassTimetable(db, sync.Class);

            await sync.ParseAndUpdateTimetable(ptm, tm, collisionMap[sync.Class.BakaId], ct);
        }

        private async Task Worker(CancellationToken ct) {
        outer:
            while (!ct.IsCancellationRequested) {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

                Class[] classes = await db.Classes.ToArrayAsync();

                try {
                    foreach (var @class in classes) {
                        if (ct.IsCancellationRequested)
                            break;

                        if (await WeekEdgeWait(ct))
                            // we have waited over a week edge and
                            // now week and nextWeek have shifted
                            goto outer;
                            
                        if (!await syncOptimizationService.ShouldCheck(@class.BakaId))
                            continue;

                        if (!collisionMap.ContainsKey(@class.BakaId)) {
                            collisionMap.Add(@class.BakaId, []);
                        }

                        var sync = scope.ServiceProvider.GetRequiredService<ClassPermanentTimetableSync>();
                        sync.Class = @class;

                        await DoParse(db, sync, BakaTimetableParser.When.Permanent, ct);
                        
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
