using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private async Task<TimetableWeek> CreateWeek(BakaContext db, DateOnly date) {
            var weekStart = date.AddDays(
                    // week starts with monday
                    -(date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek)
                    + 1
                );

            var week = new TimetableWeek() {
                StartDate = weekStart,
                EndDate = weekStart.AddDays(6),
                OddEven = calendar.GetWeekOfYear(
                                    weekStart.ToDateTime(TimeOnly.MinValue),
                                    CalendarWeekRule.FirstFullWeek,
                                    DayOfWeek.Monday)
                                  % 2 == 0
                                  ? OddEven.Even
                                  : OddEven.Odd,
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

        public async Task<Timetable<LivePeriod>> GetClassTimetable(BakaContext db, TimetableWeek week, Class @class) {
            var periods = await week.Days
                .Where(x => x.Week.ID == week.ID)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await GetClassTimetable(db, x, @class.BakaId))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new Timetable<LivePeriod>(periods, $"{BakaTimetableParser.Who.Class}={@class.Name}");
        }

        public async Task<Timetable<LivePeriod>> GetTeacherTimetable(BakaContext db, TimetableWeek week, Teacher teacher) {
            var periods = await week.Days
                .Where(x => x.Week.ID == week.ID)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await GetTeacherTimetable(db, x, teacher.BakaId))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new Timetable<LivePeriod>(periods, $"{BakaTimetableParser.Who.Teacher}={teacher.FullName}");
        }

        private async Task<List<LivePeriod>> GetTeacherTimetable(BakaContext db, TimetableDay day, TeacherBakaId teacherBakaId) {
            return await db.LivePeriodsWithIncludes
                .Where(LivePeriod.IsCurrent)
                .Where(LivePeriod.IsTeacherPeriod)
                .Where(LivePeriod.ByTeacherBakaId(teacherBakaId))
                .Where(x => x.Day.ID == day.ID)
                .ToListAsync();
        }

        private async Task<List<LivePeriod>> GetClassTimetable(BakaContext db, TimetableDay day, ClassBakaId classBakaId) {
            return await db.LivePeriodsWithIncludes
                .Where(LivePeriod.IsCurrent)
                .Where(LivePeriod.IsClassPeriod)
                .Where(LivePeriod.ByClassBakaId(classBakaId))
                .Where(x => x.Day.ID == day.ID)
                .ToListAsync();
        }

        public IQueryable<LivePeriod> GetClassPeriods(BakaContext db, ClassBakaId classId, string? group) {
            IQueryable<LivePeriod> query = db.LivePeriodsWithIncludes
                        .Where(LivePeriod.IsCurrent)
                        .Where(LivePeriod.IsClassPeriod)
                        .Where(LivePeriod.ByClassBakaId(classId));
            if (group == null) {
                query = query.Where(LivePeriod.ByDefaultGroup);
            } else {
                query = query.Where(LivePeriod.ByGroupName(group));
            }
            return query;
        }

    }

    public class Timetable<T> : ITimetable<T>
        where T : LivePeriod {
        public string? Tag { get; }
        public List<T> Periods { get; }

        public Timetable(List<T> periods, string? tag = null) {
            Periods = periods;
            Tag = tag;
        }

        public T? GetPeriod(DateOnly date, int periodIndex, HashSet<ClassGroup> groups) {
            var e = GetPeriods(date, periodIndex)
                .Where(x => x.Groups.SetEquals(groups));
            
            if (e.Count() > 1) {
                var classes = groups.Select(x => $"{x.Class.Name}:{x.Name}").Aggregate((t,c) => t+", "+c);
                throw new InvalidDataException($"timetable collision, tag='{Tag}', classes='{classes}' periodIndex={periodIndex} date={date}");
            }

            return e.FirstOrDefault();
        }

        public IEnumerable<T> GetPeriods(DateOnly date, int periodIndex) {
            return Periods.Where(x => x.Day.Date == date
                                   && x.PeriodIndex == periodIndex);
        }
    }

    public interface ITimetable<T> {
        public List<T> Periods { get; }

        public T? GetPeriod(DateOnly date, int periodIndex, HashSet<ClassGroup> group);
        public IEnumerable<T> GetPeriods(DateOnly date, int periodIndex);
    }
}
