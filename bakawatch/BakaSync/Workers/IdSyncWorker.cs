using bakawatch.BakaSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Workers
{
    internal class IdSyncWorker : BackgroundService
    {
        private readonly BakaTimetableParser timetable;
        private readonly IServiceScopeFactory serviceScopeFactory;
        private readonly ILogger logger;
        private readonly IdSyncService idSyncService;

        public IdSyncWorker(BakaTimetableParser timetable, IServiceScopeFactory serviceScopeFactory, ILogger<IdSyncWorker> logger, IdSyncService idSyncService)
        {
            this.timetable = timetable;
            this.serviceScopeFactory = serviceScopeFactory;
            this.logger = logger;
            this.idSyncService = idSyncService;
        }

        async Task<string?> FetchTeacherShortName(string id)
        {
            var v = await timetable.Get(id, BakaTimetableParser.Who.Teacher, BakaTimetableParser.When.Permanent);
            if (v.Count < 1)
                v = await timetable.Get(id, BakaTimetableParser.Who.Teacher, BakaTimetableParser.When.Actual);
            if (v.Count < 1)
                v = await timetable.Get(id, BakaTimetableParser.Who.Teacher, BakaTimetableParser.When.Next);

            if (v.Count < 1)
                return null;

            return v.First().TeacherShortName;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            logger.Log(LogLevel.Information, $"Starting {nameof(IdSyncWorker)}");

            await Worker(ct);
        }

        private async Task Worker(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await WorkerInner(ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "");
                    if (!idSyncService.IsInitialized.IsCompleted)
                    {
                        logger.Log(LogLevel.Warning, "Exception occured on initial sync, retrying in 5 seconds");
                        await Task.Delay(5_000, ct);
                        continue;
                    }
                }

                await Task.Delay(60_000, ct);
            }

            logger.Log(LogLevel.Information, "no weay");
        }

        private async Task WorkerInner(CancellationToken ct)
        {
            int classes_added = 0, classes_modified = 0;
            int rooms_added = 0, rooms_modified = 0;
            int teachers_added = 0, teachers_modified = 0;

            if (!idSyncService._IsInitializedTCS.Task.IsCompleted)
                logger.Log(LogLevel.Information, "Initial sync begin");

            var list = await timetable.GetList();
            using (var scope = serviceScopeFactory.CreateAsyncScope())
            using (var db = scope.ServiceProvider.GetRequiredService<BakaContext>())
            {

                foreach ((var key, var value) in list.Classes)
                {
                    var o = await db.Classes.Where(x => x.BakaId.Value == key)
                                            .FirstOrDefaultAsync(ct);
                    if (o != null)
                    {
                        if (o.Name != value)
                        {
                            o.Name = value;
                            db.Classes.Update(o);
                            classes_modified++;
                        }
                    }
                    else
                    {
                        db.Classes.Add(new Entities.Class
                        {
                            BakaId = new(key),
                            Name = value,
                            Active = true
                        });
                        classes_added++;
                    }
                }

                if (classes_added + classes_modified > 0)
                    logger.Log(LogLevel.Information, $"Classes added {classes_added}, modified {classes_modified}");

                foreach ((var key, var value) in list.Rooms)
                {
                    var o = await db.Rooms.Where(x => x.BakaId.Value == key)
                                          .FirstOrDefaultAsync(ct);
                    if (o != null)
                    {
                        if (o.Name != value)
                        {
                            o.Name = value;
                            db.Rooms.Update(o);
                            rooms_modified++;
                        }
                    }
                    else
                    {
                        db.Rooms.Add(new Entities.Room
                        {
                            BakaId = new(key),
                            Name = value,
                            Active = true
                        });
                        rooms_added++;
                    }
                }

                if (rooms_added + rooms_modified > 0)
                    logger.Log(LogLevel.Information, $"Classes added {rooms_added}, modified {rooms_modified}");

                foreach ((var key, var _value) in list.Teachers)
                {
                    var o = await db.Teachers.Where(x => x.BakaId.Value == key)
                                             .FirstOrDefaultAsync(ct);
                    
                    // baka teacher dropdown list is in last first name format
                    // convert to first last name format
                    var value = string.Join(" ", _value.Split(" ").Reverse());

                    if (o != null)
                    {
                        if (o.FullName != value)
                        {
                            o.FullName = value;
                            db.Teachers.Update(o);
                            teachers_modified++;
                        }
                    }
                    else
                    {
                        db.Teachers.Add(new Entities.Teacher
                        {
                            BakaId = new(key),
                            FullName = value,
                            Active = true
                        });
                        teachers_added++;
                    }
                }

                if (teachers_added + teachers_modified > 0)
                    logger.Log(LogLevel.Information, $"Classes added {teachers_added}, modified {teachers_modified}");

                await db.SaveChangesAsync(CancellationToken.None);
            }

            if (!idSyncService._IsInitializedTCS.Task.IsCompleted)
            {
                logger.Log(LogLevel.Information, "Initial sync done");
                idSyncService._IsInitializedTCS.SetResult();
            }
        }
    }
}
