#if false
using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using bakawatch.DiscordBot.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static bakawatch.BakaSync.BakaAPI;

namespace bakawatch.BakaSync.Workers {
    [Obsolete("get out", true)]
    internal class TimetableSyncWorker(
        IdSyncService idSyncService,
        TimetableService timetableService,
        TimetableNotificationService timetableNotificationService,
        BakaTimetableParser bakaTimetableParser,
        ILogger<TimetableSyncWorker> logger,
        IServiceScopeFactory serviceScopeFactory
    )
        : BackgroundService
    {
        private readonly IdSyncService idSyncService = idSyncService;
        private readonly TimetableService timetableService = timetableService;
        private readonly TimetableNotificationService timetableNotificationService = timetableNotificationService;
        private readonly BakaTimetableParser bakaTimetableParser = bakaTimetableParser;
        private readonly ILogger logger = logger;
        private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

        class CollisionLog(DateOnly Date, int PeriodIndex, string Group) {
            public DateOnly Date = Date;
            public int PeriodIndex = PeriodIndex;
            public string Group = Group;
        };
        private Dictionary<ClassBakaId, List<CollisionLog>> collisionMap = new();

        // range from before and after midnight,
        // so its actually double the ammount
        private double MidnightPauseRange = 10 * 60 * 1000; 

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await idSyncService.IsInitialized;

            logger.Log(LogLevel.Information, $"Starting {nameof(TimetableSyncWorker)}");

            await Worker(stoppingToken);
        }

        private async Task Worker(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested) {
                Class[] classes;
                using (var scope = serviceScopeFactory.CreateAsyncScope())
                using (var db = scope.ServiceProvider.GetRequiredService<BakaContext>()) {
                    classes = await db.Classes.ToArrayAsync();
                }

                TimetableWeek week = await timetableService.GetOrCreateWeek(DateOnly.FromDateTime(DateTime.Now));
                TimetableWeek nextWeek = await timetableService.GetOrCreateWeek(DateOnly.FromDateTime(DateTime.Now).AddDays(7));

                try {
                    foreach (var @class in classes) {
                        if (!collisionMap.ContainsKey(@class.BakaId)) {
                            collisionMap.Add(@class.BakaId, new());
                        }

                        await ParseAndUpdateTimetable(@class, week, BakaTimetableParser.When.Actual, ct);
                        await ParseAndUpdateTimetable(@class, nextWeek, BakaTimetableParser.When.Next, ct);
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

        private async Task ParseAndUpdateTimetable(Class @class, TimetableWeek week, BakaTimetableParser.When when, CancellationToken ct)
        {
            var ptm = await bakaTimetableParser.Get(@class.BakaId.Value, BakaTimetableParser.Who.Class, when);
            var tm = await timetableService.GetTimetable(week, @class.BakaId);
            var checkedPeriods = new List<Period>(tm.Periods.Count);

            using var scope = serviceScopeFactory.CreateAsyncScope();
            using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

            // cases handled
            //      1. simple changes
            //          eg. p1 => p2
            //           or p1 => removed
            //           or p1:g1 => p2:g1
            //      2. merged/split subjects (groups)
            //          eg. (p1:g1 & p2:g2) => removed
            //           or removed => (p1:g1 & p2:g2)
            //           or (p1:g1 & p2:g2) => p3
            //      3. drops, a period getting completely removed (disapearing)

            foreach (var pper in ptm) {
                // dont break, return, or else you will
                // drop the whole local timetable
                if (ct.IsCancellationRequested) return;

                if (CheckForCollision(@class, ptm, pper, collisionMap[@class.BakaId]))
                    // this period is currently colliding with another one
                    // skip it until the collision is fixed
                    continue;

                ClassGroup? group = null;
                if (!string.IsNullOrEmpty(pper.JsonData.group))
                    group = await GetGroup(db, await db.Classes.FirstAsync(x => x.BakaId == @class.BakaId), pper.JsonData.group);
                
                var period = tm.GetPeriod(pper.Date, pper.PeriodIndex, group);
                var np = await ParsedPeriodToPeriod(db, @class.BakaId, pper);

                if (period == null) {
                    // new period never seen before

                    List<PeriodHistory>? histories = null;

                    if (group == null) {
                        // split => merge
                        
                        // i lied, we may have actually seen em,
                        // introducing to you, split subjects
                        
                        var _maybe_periods = tm.GetPeriods(pper.Date, pper.PeriodIndex);
                        if (_maybe_periods.Any()) {
                            histories = new();

                            var maybe_periods = _maybe_periods.ToList();
                            checkedPeriods.AddRange(maybe_periods);

                            var toDropEnum = maybe_periods
                                .ToAsyncEnumerable()
                                .Where(x => x.Group != null)
                                .SelectAwait(async x => await timetableService.GetPeriod(db, x.ID));

                            await foreach (var toDrop in toDropEnum) {
                                PeriodHistory ph = PeriodToPeriodHistory(toDrop);
                                db.PeriodHistoriesObsolete.Add(ph);
                                db.Periods.Remove(toDrop);
                                logger.Log(LogLevel.Information, $"Dropping1 {ph.Day.Date}:{ph.PeriodIndex} {ph.Class.Name}:{ph.Group!.Name} - {ph.Subject?.Name} ({ph.Type})");
                                histories.Add(ph);
                            }
                        }
                    } else {
                        // merge => split
                        Period? maybe_period = tm.GetPeriod(pper.Date, pper.PeriodIndex);

                        if (maybe_period != null) {
                            checkedPeriods.Add(maybe_period);
                            histories = new();
                            maybe_period = await db.Periods.FirstOrDefaultAsync(x => x.ID == maybe_period.ID);

                            if (maybe_period != null) {
                                PeriodHistory ph = PeriodToPeriodHistory(maybe_period);
                                db.PeriodHistoriesObsolete.Add(ph);
                                db.Periods.Remove(maybe_period);
                                logger.Log(LogLevel.Information, $"Dropping2 {ph.Day.Date}:{ph.PeriodIndex} {ph.Class.Name} - {ph.Subject?.Name} ({ph.Type})");
                                histories.Add(ph);
                            } else {
                                // this period was already dropped because of a previous group
                                var ph = await db.PeriodHistoriesObsolete
                                    .Where(x => x.Day.Date == pper.Date
                                             && x.PeriodIndex == pper.PeriodIndex
                                             && x.Group == null)
                                    .OrderByDescending(x => x.Timestamp)
                                    .Include(x => x.Class)
                                    .Include(x => x.Subject)
                                    .Include(x => x.Room)
                                    .Include(x => x.Teacher)
                                    .Include(x => x.Group)
                                    .Include(x => x.Day)
                                    .FirstAsync();

                                histories.Add(ph);
                            }
                        }
                    }
                    
                    db.Periods.Add(np);

                    await db.SaveChangesAsync();

                    if (histories != null) {
                        foreach (var ph in histories) {
                            timetableNotificationService.FireClassPeriodChanged(np, ph);
                            logger.Log(LogLevel.Information, $"Update2 {np.Day.Date}:{np.PeriodIndex} {np.Class.Name}:{np.Group?.Name} - {ph.Subject?.Name} ({ph.Type}) => {np.Subject?.Name} ({np.Type})");
                        }
                    } else {
                        logger.Log(LogLevel.Information, $"New1 {np.Day.Date}:{np.PeriodIndex} {np.Class.Name}:{np.Group?.Name} - {np.Subject?.Name} ({np.Type})");
                        timetableNotificationService.FireClassPeriodAdded(np);
                    }
                } else {
                    checkedPeriods.Add(period);

                    // period already in local db, sniff it n' diff it
                    if (ArePeriodsSame(period, np))
                        continue;

                    period = await timetableService.GetPeriod(db, period.ID);

                    PeriodHistory ph = PeriodToPeriodHistory(period);
                    db.PeriodHistoriesObsolete.Add(ph);

                    MergePeriod(period, np);
                    db.Periods.Update(period);

                    logger.Log(LogLevel.Information, $"Update1 {period.Day.Date}:{period.PeriodIndex} {period.Class.Name} - {ph.Subject?.Name} ({ph.Type}) => {period.Subject?.Name} ({period.Type})");

                    await db.SaveChangesAsync();

                    timetableNotificationService.FireClassPeriodChanged(period, ph);
                }
            }

            // idk where to put this but
            // dropped periods should get removed
            // from db if they are replaced with
            // an actual period in timetable that
            // doesnt match the group by code above
            //                  (new period branch)

            var droppedPeriods = await tm.Periods
                .Where(x => !checkedPeriods.Contains(x))
                .Where(x => x.Type != PeriodType.Dropped)
                .ToAsyncEnumerable()
                .SelectAwait(async x => {
                    x = await timetableService.GetPeriod(db, x.ID);
                    var ph = PeriodToPeriodHistory(x);
                    db.PeriodHistoriesObsolete.Add(ph);

                    x.Type = PeriodType.Dropped;

                    x.Subject = null;
                    x.Room = null;
                    x.Teacher = null;
                    // keep the group // x.Group = null; 
                    x.ChangeInfo = null; 
                    x.RemovedInfo = null;
                    x.AbsenceInfoShort = null;
                    x.AbsenceInfoReason = null;
                    x.Timestamp = DateTime.Now;

                    db.Periods.Update(x);

                    return ph;
                })
                .ToListAsync();

            await db.SaveChangesAsync();

            foreach (var ph in droppedPeriods) {
                logger.Log(LogLevel.Information, $"Dropping3 {ph.Day.Date}:{ph.PeriodIndex} {ph.Class.Name}:{ph.Group?.Name} - {ph.Subject?.Name} ({ph.Type})");
                timetableNotificationService.FireClassPeriodDropped(ph);
            }
        }

        private bool CheckForCollision(Class @class, List<BakaTimetableParser.PeriodInfo> ptm, BakaTimetableParser.PeriodInfo pper, List<CollisionLog> collisionLogs) {
            var isColliding = ptm.Any(x => x != pper
                          && x.Date == pper.Date
                          && x.PeriodIndex == pper.PeriodIndex
                          && x.JsonData.group == pper.JsonData.group);

            var collisionLog = collisionLogs
                .FirstOrDefault(x => x.Date == pper.Date
                                  && x.PeriodIndex == pper.PeriodIndex
                                  && x.Group == pper.JsonData.group);

            if (isColliding) {
                if (collisionLog == null) {
                    logger.LogWarning($"timetable collision, className={@class.Name} group={pper.JsonData.group} date={pper.Date} periodIndex={pper.PeriodIndex}");
                    collisionLogs.Add(new(pper.Date, pper.PeriodIndex, pper.JsonData.group));
                }

                return true;
            } else if (collisionLog != null) {
                logger.LogInformation($"timetable collision resolved, className={@class.Name} group={collisionLog.Group} date={collisionLog.Date} periodIndex={collisionLog.PeriodIndex}");
                collisionLogs.Remove(collisionLog);
            }

            return false;
        }

        private void MergePeriod(Period p, Period u) {
            p.Type = u.Type;
            p.Class = u.Class;
            p.Subject = u.Subject;
            p.Room = u.Room;
            p.Teacher = u.Teacher;
            p.Group = u.Group;
           
            p.ChangeInfo = u.ChangeInfo;
            p.RemovedInfo = u.RemovedInfo;
            p.AbsenceInfoShort = u.AbsenceInfoShort;
            p.AbsenceInfoReason = u.AbsenceInfoReason;
           
            p.Day = u.Day;
            p.PeriodIndex = u.PeriodIndex;
            p.Timestamp = u.Timestamp;
        }

        private PeriodHistory PeriodToPeriodHistory(Period p) {
            var ph = new PeriodHistory() {
                Type = p.Type,
                Class = p.Class,

                Subject = p.Subject,
                Room = p.Room,
                Teacher = p.Teacher,
                Group = p.Group,

                ChangeInfo = p.ChangeInfo,
                RemovedInfo = p.RemovedInfo,
                AbsenceInfoShort = p.AbsenceInfoShort,
                AbsenceInfoReason = p.AbsenceInfoReason,

                Day = p.Day,

                PeriodIndex = p.PeriodIndex,
                Timestamp = p.Timestamp
            };

            return ph;
        }

        private bool ArePeriodsSame(Period p1, Period p2) {
            if (p1.Day.ID != p2.Day.ID)
                throw new InvalidDataException("period not same day");

            if (p1.PeriodIndex != p2.PeriodIndex)
                throw new InvalidDataException("period not same index");

            return p1.Type == p2.Type
                && p1.Class?.ID == p2.Class?.ID
                && p1.Subject?.ID == p2.Subject?.ID
                && p1.Room?.ID == p2.Room?.ID
                && p1.Teacher?.ID == p2.Teacher?.ID
                && p1.Group?.ID == p2.Group?.ID
                && p1.ChangeInfo == p2.ChangeInfo
                && p1.RemovedInfo == p2.RemovedInfo
                && p1.AbsenceInfoShort == p2.AbsenceInfoShort
                && p1.AbsenceInfoReason == p2.AbsenceInfoReason;
        }

        private async Task<Period> ParsedPeriodToPeriod(BakaContext db, ClassBakaId classId, BakaTimetableParser.PeriodInfo periodInfo) {
            var @class = await db.Classes.FirstAsync(x => x.BakaId == classId);
            var day = await db.TimetableDays.FirstAsync(x => x.Date == periodInfo.Date);

            Subject? subject = null;
            // periodInfo.SubjectShortName may be set even if its not actually a subject but an absence
            if (periodInfo.JsonData.type == "atom")
                subject = await GetSubject(db, periodInfo.SubjectShortName, periodInfo.SubjectFullName);

            Room? room = await GetRoom(db, periodInfo.JsonData.room);

            Teacher? teacher = null;
            if (periodInfo.TeacherFullNameNoDegree != null)
                teacher = await db.Teachers.FirstAsync(x => x.FullName == periodInfo.TeacherFullNameNoDegree);

            ClassGroup? group = await GetGroup(db, @class, periodInfo.JsonData.group);

            var period = new Period() {
                Type = periodInfo.JsonData.type switch {
                    "atom" => PeriodType.Normal,
                    "removed" => PeriodType.Removed,
                    "absent" => PeriodType.Absent,
                    _ => throw new InvalidDataException($"\"{periodInfo.JsonData.type}\" is not a valid period type")
                },

                Class = @class,
                
                Subject = subject,
                Room = room,
                Teacher = teacher,
                Group = group,

                ChangeInfo = periodInfo.JsonData.changeinfo,
                RemovedInfo = periodInfo.JsonData.removedinfo,
                AbsenceInfoShort = periodInfo.JsonData.absentinfo,
                AbsenceInfoReason = periodInfo.JsonData.InfoAbsentName,

                Day = day,
                PeriodIndex = periodInfo.PeriodIndex
            };

            return period;
        }

        private async Task<Subject?> GetSubject(BakaContext db, string? subjectShortName, string? subjectName) {
            if (string.IsNullOrEmpty(subjectShortName) && string.IsNullOrEmpty(subjectName))
                return null;

            if (string.IsNullOrEmpty(subjectShortName))
                throw new InvalidDataException($"subjectShortName empty with non empty subjectName={subjectName}");

            if (string.IsNullOrEmpty(subjectName))
                throw new InvalidDataException($"subjectName empty with non empty subjectShortName={subjectShortName}");

            var subject = await db.Subjects.FirstOrDefaultAsync(x => x.ShortName == subjectShortName);
            if (subject == null) {
                subject = new Subject() {
                    Name = subjectName,
                    ShortName = subjectShortName,
                };
                db.Subjects.Add(subject);
                logger.Log(LogLevel.Information, $"Creating new subject '{subject.Name}' ({subject.ShortName})");
                await db.SaveChangesAsync();
            }

            return subject;
        }


        private async Task<Room?> GetRoom(BakaContext db, string? roomName) {
            if (string.IsNullOrEmpty(roomName))
                return null;
                
            var room = await db.Rooms.FirstOrDefaultAsync(x => x.Name == roomName);
            if (room == null) {
                room = new Room() {
                    IsFake = true,
                    // null complex types not yet supported
                    BakaId = new(roomName),
                    Name = roomName,
                    Active = true,
                };
                db.Rooms.Add(room);
                logger.Log(LogLevel.Information, $"Creating fake room {room.Name}");
                await db.SaveChangesAsync();
            }

            return room;
        }

        private async Task<ClassGroup?> GetGroup(BakaContext db, Class @class, string? groupName) {
            if (string.IsNullOrEmpty(groupName))
                return null;

            var group = await db.Groups.FirstOrDefaultAsync(x => x.Name == groupName
                                                              && x.Class.ID == @class.ID);

            if (group == null) {
                group = new ClassGroup() {
                    Class = @class,
                    Name = groupName,
                };
                db.Groups.Add(group);
                logger.Log(LogLevel.Information, $"Creating group {group.Name} for class {@class.Name}");
                await db.SaveChangesAsync();
            }

            return group;
        }
    }
}
#endif