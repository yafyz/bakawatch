using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static bakawatch.BakaSync.BakaTimetableParser;

namespace bakawatch.BakaSync.Services
{
    public class TimetableService(IServiceScopeFactory serviceScopeFactory, ILogger<TimetableService> logger)
    {
        private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;
        private readonly ILogger logger = logger;

        private readonly Calendar calendar = new GregorianCalendar();

        public async Task<TimetableWeek> GetOrCreateWeek(DateOnly date) {
            using var scope = serviceScopeFactory.CreateAsyncScope();
            using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

            return await GetWeek(db, date) ?? await CreateWeek(db, date);
        }

        private async Task<TimetableWeek?> GetWeek(BakaContext db, DateOnly date)
            => await db.TimetableWeeks
                .Where(x => x.StartDate <= date
                         && x.EndDate >= date)
                .Include(x => x.Days)
                .FirstOrDefaultAsync();

        public OddEven GetWeekOddness(DateOnly date)
            => calendar.GetWeekOfYear(
                                    date.ToDateTime(TimeOnly.MinValue),
                                    CalendarWeekRule.FirstFullWeek,
                                    DayOfWeek.Monday)
                                  % 2 == 0
                                  ? OddEven.Even
                                  : OddEven.Odd;

        public DateOnly NextWeekStart(DateOnly date)
            => date.AddDays(
                    -(date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek)
                    + 1
                ).AddDays(7);

        private async Task<TimetableWeek> CreateWeek(BakaContext db, DateOnly date) {
            var weekStart = date.AddDays(
                    // week starts with monday
                    -(date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek)
                    + 1
                );

            var week = new TimetableWeek() {
                StartDate = weekStart,
                EndDate = weekStart.AddDays(6),
                OddEven = GetWeekOddness(weekStart),
                Days = new List<TimetableDay>(7)
            };

            db.TimetableWeeks.Add(week);
            
            for (var i = 0; i < 7; i++) {
                var day = new TimetableDay() {
                    Date = week.StartDate.AddDays(i),
                    Week = week,
                };
                week.Days.Add(day);
                db.TimetableDays.Add(day);
            }

            await db.SaveChangesAsync();

            return week;
        }

        public async Task<LiveTimetable<LivePeriod>> GetClassTimetable(BakaContext db, TimetableWeek week, Class @class) {
            var periods = await week.Days
                .Where(x => x.Week.ID == week.ID)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await GetClassTimetable(db, x, @class.BakaId))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new LiveTimetable<LivePeriod>(periods, $"{BakaTimetableParser.Who.Class}={@class.Name}");
        }

        public async Task<PermanentTimetable<PermanentPeriod>> GetPermanentClassTimetable(BakaContext db, Class @class) {
            var periods = await db.PermanentPeriodsWithIncludes
                .Where(PermanentPeriodQuery.IsClassPeriod)
                .Where(PermanentPeriodQuery.ByClass(@class))
                .ToListAsync();

            return new PermanentTimetable<PermanentPeriod>(periods, $"{BakaTimetableParser.Who.Class}={@class.Name}");
        }

        public async Task<PermanentPeriod?> GetPermanentClassPeriod(BakaContext db, ClassGroup group, DayOfWeek dayOfWeek, int periodIndex, OddEven oddEven) {
            return await db.PermanentPeriodsWithIncludes
                .Where(PermanentPeriodQuery.IsCurrent)
                .Where(PermanentPeriodQuery.IsClassPeriod)
                .Where(PermanentPeriodQuery.ByGroup(group))
                .Where(x => x.DayOfWeek == dayOfWeek)
                .Where(x => x.PeriodIndex == periodIndex)
                .Where(x => x.OddOrEvenWeek == oddEven)
                .SingleOrDefaultAsync();
        }

        public async Task<LiveTimetable<LivePeriod>> GetTeacherTimetable(BakaContext db, TimetableWeek week, Teacher teacher) {
            var periods = await week.Days
                .Where(x => x.Week.ID == week.ID)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await GetTeacherTimetable(db, x, teacher.BakaId))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new LiveTimetable<LivePeriod>(periods, $"{BakaTimetableParser.Who.Teacher}={teacher.FullName}");
        }

        private async Task<List<LivePeriod>> GetTeacherTimetable(BakaContext db, TimetableDay day, TeacherBakaId teacherBakaId) {
            return await db.LivePeriodsWithIncludes
                .Where(LivePeriodQuery.IsCurrent)
                .Where(LivePeriodQuery.IsTeacherPeriod)
                .Where(LivePeriodQuery.ByTeacherBakaId(teacherBakaId))
                .Where(x => x.Day.ID == day.ID)
                .ToListAsync();
        }

        private async Task<List<LivePeriod>> GetClassTimetable(BakaContext db, TimetableDay day, ClassBakaId classBakaId) {
            return await db.LivePeriodsWithIncludes
                .Where(LivePeriodQuery.IsCurrent)
                .Where(LivePeriodQuery.IsClassPeriod)
                .Where(LivePeriodQuery.ByClassBakaId(classBakaId))
                .Where(x => x.Day.ID == day.ID)
                .ToListAsync();
        }

        public IQueryable<LivePeriod> GetClassPeriods(BakaContext db, ClassBakaId classId, string? group) {
            IQueryable<LivePeriod> query = db.LivePeriodsWithIncludes
                        .Where(LivePeriodQuery.IsCurrent)
                        .Where(LivePeriodQuery.IsClassPeriod)
                        .Where(LivePeriodQuery.ByClassBakaId(classId));
            if (group == null) {
                query = query.Where(LivePeriodQuery.ByDefaultGroup);
            } else {
                query = query.Where(LivePeriodQuery.ByGroupName(group));
            }
            return query;
        }

        public async Task<LivePeriod?> GetClassPeriod(BakaContext db, ClassBakaId classId, DateOnly date, int periodIndex, string? group) {
            IQueryable<LivePeriod> query = db.LivePeriodsWithIncludes
                        .Where(LivePeriodQuery.IsCurrent)
                        .Where(LivePeriodQuery.IsClassPeriod)
                        .Where(LivePeriodQuery.ByClassBakaId(classId))
                        .Where(x => x.Day.Date == date)
                        .Where(x => x.PeriodIndex == periodIndex);
            if (group == null) {
                query = query.Where(LivePeriodQuery.ByDefaultGroup);
            } else {
                query = query.Where(LivePeriodQuery.ByGroupName(group));
            }
            return await query.SingleOrDefaultAsync();
        }

    }

    public class LiveTimetable<T> : ITimetable<T>
        where T : LivePeriod {
        public string? Tag { get; }
        public List<T> Periods { get; }

        public LiveTimetable(List<T> periods, string? tag = null) {
            Periods = periods;
            Tag = tag;
        }

        public T? GetPeriod(PeriodInfo periodInfo, HashSet<ClassGroup> groups)
            => GetPeriod(periodInfo.Date, periodInfo.PeriodIndex, groups);
        public IEnumerable<T> GetPeriods(PeriodInfo periodInfo, bool defaultOnly = false)
            => GetPeriods(periodInfo.Date, periodInfo.PeriodIndex, defaultOnly);

        public T? GetPeriod(DateOnly date, int periodIndex, HashSet<ClassGroup> groups) {
            var e = GetPeriods(date, periodIndex)
                .Where(x => x.Groups.SetEquals(groups));
            
            if (e.Count() > 1) {
                var classes = groups.Select(x => $"{x.Class.Name}:{x.Name}").Aggregate((t,c) => t+", "+c);
                throw new InvalidDataException($"timetable collision, tag='{Tag}', classes='{classes}' periodIndex={periodIndex} date={date}");
            }

            return e.FirstOrDefault();
        }

        public IEnumerable<T> GetPeriods(DateOnly date, int periodIndex, bool defaultOnly = false) {
            var query = Periods.Where(x => x.Day.Date == date
                                        && x.PeriodIndex == periodIndex);

            if (defaultOnly) {
                query = query.Where(x => x.Groups.All(x => x.IsDefaultGroup));
            }

            return query;
        }
    }

    public class PermanentTimetable<T> : ITimetable<T> where T : PermanentPeriod
    {
        public string? Tag { get; }
        public List<T> Periods { get; }

        public PermanentTimetable(List<T> periods, string? tag = null) {
            Periods = periods;
            Tag = tag;
        }

        public T? GetPeriod(PeriodInfo periodInfo, HashSet<ClassGroup> groups)
            => GetPeriod(periodInfo.DayOfWeek, periodInfo.OddOrEvenWeek, periodInfo.PeriodIndex, groups);

        public IEnumerable<T> GetPeriods(PeriodInfo periodInfo, bool defaultOnly = false)
            => GetPeriods(periodInfo.DayOfWeek, periodInfo.OddOrEvenWeek, periodInfo.PeriodIndex, defaultOnly);

        public T? GetPeriod(DayOfWeek dayOfWeek, OddEven oddEven, int periodIndex, HashSet<ClassGroup> groups) {
            var e = GetPeriods(dayOfWeek, oddEven, periodIndex)
                .Where(x => x.Groups.SetEquals(groups));

            if (e.Count() > 1) {
                var classes = groups.Select(x => $"{x.Class.Name}:{x.Name}").Aggregate((t, c) => t + ", " + c);
                throw new InvalidDataException($"timetable collision, tag='{Tag}', classes='{classes}' periodIndex={periodIndex} date={oddEven}");
            }

            return e.FirstOrDefault();
        }

        public IEnumerable<T> GetPeriods(DayOfWeek dayOfWeek, OddEven oddEven, int periodIndex, bool defaultOnly = false) {
            var query = Periods.Where(x => x.DayOfWeek == dayOfWeek
                                        && x.OddOrEvenWeek == oddEven
                                        && x.PeriodIndex == periodIndex);

            if (defaultOnly) {
                query = query.Where(x => x.Groups.All(x => x.IsDefaultGroup));
            }

            return query;
        }
    }

    public interface ITimetable<T> {
        public List<T> Periods { get; }

        public T? GetPeriod(PeriodInfo periodInfo, HashSet<ClassGroup> group);
        public IEnumerable<T> GetPeriods(PeriodInfo periodInfo, bool defaultOnly = false);
    }
}
