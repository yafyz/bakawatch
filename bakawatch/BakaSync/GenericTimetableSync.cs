using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection.Metadata.Ecma335;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;


namespace bakawatch.BakaSync
{
    internal abstract class GenericTimetableSync<PERIOD, PERIODHISTORY>
    {
        public abstract string? Tag { get; }
        protected abstract BakaTimetableParser.Who Who { get; }

        protected abstract BakaContext bakaContext { get; }
        protected abstract ILogger logger { get; }

        protected abstract Task<PERIOD> ParseIntoPeriod(BakaTimetableParser.PeriodInfo periodInfo);
        protected abstract Task<PERIOD?> GetPeriodByPeriod(PERIOD period);

        protected abstract Task<bool> IsPeriodDropped(PERIOD period);
        protected abstract Task MakePeriodDropped(PERIOD period);

        protected abstract Task<PERIODHISTORY?> GetHistoryByPeriod(PERIOD period);
        protected abstract Task<PERIODHISTORY> MakeIntoHistory(PERIOD period);

        protected abstract Task<bool> ComparePeriods(PERIOD p1, PERIOD p2);

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
                
                var groups = await GetGroups(parsedPeriod);
                var areDefaultGroups = groups.All(x => x.IsDefaultGroup);

                if (!areDefaultGroups && groups.Any(x => x.IsDefaultGroup))
                    throw new NotImplementedException("this may not be good");

                var period = currentTimetable.GetPeriod(parsedPeriod, groups);
                var newPeriod = await ParseIntoPeriod(parsedPeriod);

                if (period == null) {
                    // new period never seen before

                    List<PERIODHISTORY>? histories = null;

                    if (areDefaultGroups) {
                        // split => merge

                        // i lied, we may have actually seen em,
                        // introducing to you, split subjects

                        var maybe_periods = currentTimetable.GetPeriods(parsedPeriod);

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
                        PERIOD? maybe_period = currentTimetable
                            .GetPeriods(parsedPeriod, true)
                            .SingleOrDefault();

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
                    await InsertPeriod(newPeriod);

                    await FirePeriodChanged(newPeriod, history);
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
                          && x.OddOrEvenWeek == pper.OddOrEvenWeek
                          && x.DayOfWeek == pper.DayOfWeek
                          && x.PeriodIndex == pper.PeriodIndex
                          && x.JsonData.group == pper.JsonData.group);

            var collisionLog = collisionLogs
                .FirstOrDefault(x => x.Date == pper.Date
                                  && x.PeriodIndex == pper.PeriodIndex
                                  && x.OddOrEvenWeek == pper.OddOrEvenWeek
                                  && x.DayOfWeek == pper.DayOfWeek
                                  && x.Group == pper.JsonData.group);

            if (collisions.Any()) {
                if (collisionLog == null) {
                    logger.LogWarning($"timetable collision, tag='{Tag}', group={pper.JsonData.group} date={pper.Date} periodIndex={pper.PeriodIndex}");
                    collisionLogs.Add(new(pper.Date, pper.PeriodIndex, pper.JsonData.group, pper.OddOrEvenWeek, pper.DayOfWeek));
                }

                return true;
            } else if (collisionLog != null) {
                logger.LogInformation($"timetable collision resolved, tag='{Tag}', group={collisionLog.Group} date={collisionLog.Date} periodIndex={collisionLog.PeriodIndex}");
                collisionLogs.Remove(collisionLog);
            }

            return false;
        }

        protected virtual async Task<PeriodBase> ParseIntoBasePeriod(BakaTimetableParser.PeriodInfo periodInfo) {
            var groups = await GetGroups(periodInfo);

            Subject? subject = null;
            // periodInfo.SubjectShortName may be set even if its not actually a subject but an absence
            if (periodInfo.JsonData.type == "atom")
                subject = await GetSubject(periodInfo.SubjectShortName, periodInfo.SubjectFullName);

            Teacher? teacher = await GetTeacher(periodInfo.TeacherFullNameNoDegree);
            Room? room = await GetRoom(periodInfo.JsonData.room);

            var period = new PeriodBase() {
                Who = Who,
                Groups = groups.ToHashSet(),
                Subject = subject,
                Room = room,
                Teacher = teacher
            };

            return period;
        }

        protected virtual async Task<Teacher?> GetTeacher(string? teacherName) {
            Teacher? teacher = null;
            if (teacherName != null)
                teacher = await bakaContext.Teachers.FirstAsync(x => x.FullName == teacherName);
            return teacher;
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

        protected async Task<ClassGroup> GetGroup(Class @class, string? groupName) {
            var group = await bakaContext.Groups
                .FirstOrDefaultAsync(x => (groupName != null
                                            ? x.Name == groupName
                                            : x.IsDefaultGroup)
                                       && x.Class.ID == @class.ID);

            if (group == null) {
                group = new ClassGroup() {
                    Class = @class,
                    Name = groupName ?? ClassGroup.DefaultGroupName,
                    IsDefaultGroup = groupName == null
                };
                bakaContext.Groups.Add(group);
                logger.Log(LogLevel.Information, $"Creating group {group.Name ?? ClassGroup.DefaultGroupName} for class {@class.Name}");
                await bakaContext.SaveChangesAsync();
            }

            return group;
        }

        protected virtual async Task<Class> GetClassByName(string? className) {
            if (className == null) {
                throw new ArgumentException("className cannot be null");
            }
            return await bakaContext.Classes.FirstAsync(x => x.Name == className);
        }

        protected virtual IEnumerable<(string?, string?)> GetClassNameAndGroupName(BakaTimetableParser.PeriodInfo pper) {
            if (pper.JsonData.group == null) {
                return [];
            }
            
            return pper.JsonData.group
                .Split(',')
                .Select(x => x.Trim())
                .Select(x => x.Split(' '))
                .Select<string[], (string?, string?)>(x => x switch {
                    [var className, "celá"] => (className, null),
                    [var className, var groupName] => (className, groupName),
                    [var groupName] => (null, groupName),
                    _ => throw new Exception($"invalid group data: {pper.JsonData.group}")
                });
        }

        protected async Task<HashSet<ClassGroup>> GetGroups(BakaTimetableParser.PeriodInfo pper) {
            return await GetClassNameAndGroupName(pper)
                .ToAsyncEnumerable()
                .SelectAwait(async x => {
                    (var className, var groupName) = x;
                    var @class = await GetClassByName(className);
                    ClassGroup group = await GetGroup(@class, groupName);
                    return group;
                })
                .Where(x => x != null)
                .ToHashSetAsync();
        }
    }

    public class CollisionLog(DateOnly Date, int PeriodIndex, string Group, OddEven oddEven, DayOfWeek dayOfWeek) {
        public DateOnly Date = Date;
        public int PeriodIndex = PeriodIndex;
        public string Group = Group;
        public OddEven OddOrEvenWeek = oddEven;
        public DayOfWeek DayOfWeek = dayOfWeek;
    };
}
