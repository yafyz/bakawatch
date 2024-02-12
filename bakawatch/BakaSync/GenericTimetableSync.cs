#if true
using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    internal abstract class GenericTimetableSync<PERIOD, PERIODHISTORY>(
        BakaContext bakaContext,
        ILogger logger
    ) {
        //private readonly BakaContext bakaContext = bakaContext;
        //private readonly ILogger logger = logger;

        protected abstract Task<PERIOD> ParseIntoPeriod(BakaTimetableParser.PeriodInfo periodInfo);
        protected abstract Task<PERIOD?> GetPeriodByPeriod(PERIOD period);

        protected abstract Task<bool> IsPeriodDropped(PERIOD period);
        protected abstract Task MakePeriodDropped(PERIOD period);

        protected abstract Task<PERIODHISTORY?> GetHistoryByPeriod(PERIOD period);
        protected abstract Task<PERIODHISTORY> MakeIntoHistory(PERIOD period);

        protected abstract Task<bool> ComparePeriods(PERIOD e1, PERIOD e2);

        protected abstract Task RemovePeriod(PERIOD entity);
        protected abstract Task InsertPeriod(PERIOD newEntity);

        protected abstract Task FirePeriodNew(PERIOD period);
        protected abstract Task FirePeriodChanged(PERIOD newPeriod, PERIODHISTORY periodHistory);
        protected abstract Task FirePeriodDropped(PERIOD period);

        public async Task ParseAndUpdateTimetable(
            List<BakaTimetableParser.PeriodInfo> parsedTimetable,
            ITimetable<PERIOD> currentTimetable,
            List<CollisionLog> collisionLogs,
            CancellationToken ct
        ) {
            var checkedPeriods = new List<PERIOD>(currentTimetable.Periods.Count);

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

            foreach (var parsedPeriod in parsedTimetable) {
                // dont break, return, or else you will
                // drop the whole local timetable
                if (ct.IsCancellationRequested) return;

                if (CheckForCollision(parsedTimetable, parsedPeriod, collisionLogs))
                    // this period is currently colliding with another one
                    // skip it until the collision is fixed
                    continue;

                (var @class, var group) = await GetClassAndGroup(parsedPeriod);

                var period = currentTimetable.GetPeriod(parsedPeriod.Date, parsedPeriod.PeriodIndex, group);
                var newPeriod = await ParseIntoPeriod(parsedPeriod);
                
                if (period == null) {
                    // new period never seen before

                    List<PERIODHISTORY>? histories = null;

                    if (group == null) {
                        // split => merge

                        // i lied, we may have actually seen em,
                        // introducing to you, split subjects

                        var maybe_periods = currentTimetable.GetPeriods(parsedPeriod.Date, parsedPeriod.PeriodIndex);

                        if (maybe_periods.Any()) {
                            histories = new(2);

                            var periods = maybe_periods.ToList();
                            checkedPeriods.AddRange(periods);

                            foreach (var toDrop in periods) {
                                var history = await MakeIntoHistory(toDrop);
                                histories.Add(history);
                            }
                        }
                    } else {
                        // merge => split
                        PERIOD? maybe_period = currentTimetable.GetPeriod(parsedPeriod.Date, parsedPeriod.PeriodIndex);

                        if (maybe_period != null) {
                            checkedPeriods.Add(maybe_period);
                            histories = new(1);

                            maybe_period = await GetPeriodByPeriod(maybe_period);

                            if (maybe_period != null) {
                                var history = await MakeIntoHistory(maybe_period);

                                histories.Add(history);
                            } else {
                                // this period was already dropped because of a previous group
                                var history = await GetHistoryByPeriod(newPeriod)
                                    ?? throw new Exception("what the fuck");
                                histories.Add(history);
                            }
                        }
                    }

                    await InsertPeriod(newPeriod);

                    if (histories != null) {
                        foreach (var history in histories) {
                            await FirePeriodChanged(newPeriod, history);
                        }    
                    } else {
                        await FirePeriodNew(newPeriod);
                    }
                } else {
                    checkedPeriods.Add(period);

                    // period already in local db, sniff it n' diff it
                    if (await ComparePeriods(period, newPeriod))
                        continue;

                    var history = await MakeIntoHistory(period);
                    await InsertPeriod(period);

                    await FirePeriodChanged(period, history);
                }
            }

            // idk where to put this but
            // dropped periods should get removed
            // from db if they are replaced with
            // an actual period in timetable that
            // doesnt match the group by code above
            //                  (new period branch)

            var droppedPeriods = currentTimetable.Periods
                .Where(x => !checkedPeriods.Contains(x))
                .ToAsyncEnumerable()
                .WhereAwait(async x => !await IsPeriodDropped(x));

            await foreach (var droppedPeriod in droppedPeriods) {
                await MakePeriodDropped(droppedPeriod);
                await FirePeriodDropped(droppedPeriod);
            }
        }

        private PeriodType ParsePeriodType(BakaTimetableParser.PeriodInfo periodInfo)
            => periodInfo.JsonData.type switch {
                "atom" => PeriodType.Normal,
                "removed" => PeriodType.Removed,
                "absent" => PeriodType.Absent,
                _ => throw new InvalidDataException($"\"{periodInfo.JsonData.type}\" is not a valid period type")
            };

        private bool CheckForCollision(List<BakaTimetableParser.PeriodInfo> ptm, BakaTimetableParser.PeriodInfo pper, List<CollisionLog> collisionLogs) {
            var collisions = ptm.Where(x => x != pper
                          && x.Date == pper.Date
                          && x.PeriodIndex == pper.PeriodIndex
                          && x.JsonData.group == pper.JsonData.group);

            var collisionLog = collisionLogs
                .FirstOrDefault(x => x.Date == pper.Date
                                  && x.PeriodIndex == pper.PeriodIndex
                                  && x.Group == pper.JsonData.group);


            var isColliding = collisions.Any();

            // bias for absent periods as they often have collisions with other
            // periods in teacher timetables before the other periods get
            // a substitution
            if (isColliding && ParsePeriodType(pper) == PeriodType.Absent) {
                var count = collisions
                    .Where(x => ParsePeriodType(x) == PeriodType.Absent)
                    .Count();

                // look anything is possible on this website,
                // double absent periods can go blow themselves up
                if (count == 1) {
                    isColliding = false;
                }
            }

            // todo: logging

            if (isColliding) {
                if (collisionLog == null) {
                    logger.LogWarning($"timetable collision, className={"@class.Name"} group={pper.JsonData.group} date={pper.Date} periodIndex={pper.PeriodIndex}");
                    collisionLogs.Add(new(pper.Date, pper.PeriodIndex, pper.JsonData.group));
                }

                return true;
            } else if (collisionLog != null) {
                logger.LogInformation($"timetable collision resolved, className={"@class.Name"} group={collisionLog.Group} date={collisionLog.Date} periodIndex={collisionLog.PeriodIndex}");
                collisionLogs.Remove(collisionLog);
            }

            return false;
        }


        protected async Task<Subject?> GetSubject(string? subjectShortName, string? subjectName) {
            if (string.IsNullOrEmpty(subjectShortName) && string.IsNullOrEmpty(subjectName))
                return null;

            if (string.IsNullOrEmpty(subjectShortName))
                throw new InvalidDataException($"subjectShortName empty with non empty subjectName={subjectName}");

            if (string.IsNullOrEmpty(subjectName))
                throw new InvalidDataException($"subjectName empty with non empty subjectShortName={subjectShortName}");

            var subject = await bakaContext.Subjects.FirstOrDefaultAsync(x => x.ShortName == subjectShortName);
            if (subject == null) {
                subject = new Subject() {
                    Name = subjectName,
                    ShortName = subjectShortName,
                };
                bakaContext.Subjects.Add(subject);
                logger.Log(LogLevel.Information, $"Creating new subject '{subject.Name}' ({subject.ShortName})");
                await bakaContext.SaveChangesAsync();
            }

            return subject;
        }


        protected async Task<Room?> GetRoom(string? roomName) {
            if (string.IsNullOrEmpty(roomName))
                return null;

            var room = await bakaContext.Rooms.FirstOrDefaultAsync(x => x.Name == roomName);
            if (room == null) {
                room = new Room() {
                    IsFake = true,
                    // null complex types not yet supported
                    BakaId = new(roomName),
                    Name = roomName,
                    Active = true,
                };
                bakaContext.Rooms.Add(room);
                logger.Log(LogLevel.Information, $"Creating fake room {room.Name}");
                await bakaContext.SaveChangesAsync();
            }

            return room;
        }

        protected async Task<ClassGroup?> GetGroup(Class @class, string? groupName) {
            if (string.IsNullOrEmpty(groupName))
                return null;

            var group = await bakaContext.Groups.FirstOrDefaultAsync(x => x.Name == groupName
                                                                  && x.Class.ID == @class.ID);

            if (group == null) {
                group = new ClassGroup() {
                    Class = @class,
                    Name = groupName,
                };
                bakaContext.Groups.Add(group);
                logger.Log(LogLevel.Information, $"Creating group {group.Name} for class {@class.Name}");
                await bakaContext.SaveChangesAsync();
            }

            return group;
        }

        protected async Task<Class> GetClassByName(string? className) {
            if (className == null) {
                throw new ArgumentException("className cannot be null");
            }
            return await bakaContext.Classes.FirstAsync(x => x.Name == className);
        }

        protected (string?, string?) GetClassNameAndGroupName(BakaTimetableParser.PeriodInfo pper) {
            return pper.JsonData.group.Split(" ") switch {
                [var className, "celá"] => (className, null),
                [var className, var groupName] => (className, groupName),
                [var groupName] => (null, groupName),
                _ => throw new Exception($"invalid group data: {pper.JsonData.group}")
            };
        }

        protected virtual async Task<(Class, ClassGroup?)> GetClassAndGroup(BakaTimetableParser.PeriodInfo pper) {
            (var className, var groupName) = GetClassNameAndGroupName(pper);
            var @class = await GetClassByName(className);
            var group = await GetGroup(@class, groupName);
            return (@class, group);
        }
    }

    public class CollisionLog(DateOnly Date, int PeriodIndex, string Group) {
        public DateOnly Date = Date;
        public int PeriodIndex = PeriodIndex;
        public string Group = Group;
    };
}
#endif