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

        public async Task<Timetable<ClassPeriod>> GetClassTimetable(BakaContext db, TimetableWeek week, ClassBakaId id) {
            //using var scope = serviceScopeFactory.CreateAsyncScope();
            //using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

            var periods = await week.Days
                .Where(x => x.Week.ID == week.ID)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await GetClassTimetable(db, x, id))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new Timetable<ClassPeriod>(periods);
        }

        public async Task<Timetable<TeacherPeriod>> GetTeacherTimetable(BakaContext db, TimetableWeek week, TeacherBakaId id) {
            //using var scope = serviceScopeFactory.CreateAsyncScope();
            //using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

            var periods = await week.Days
                .Where(x => x.Week.ID == week.ID)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await GetTeacherTimetable(db, x, id))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new Timetable<TeacherPeriod>(periods);
        }

        private async Task<List<TeacherPeriod>> GetTeacherTimetable(BakaContext db, TimetableDay day, TeacherBakaId teacherBakaId) {
            return await db.TeacherPeriodsLive
                .Where(x => x.Day.ID == day.ID
                         && x.Teacher.BakaId == teacherBakaId)
                .Include(x => x.Subject)
                .Include(x => x.Room)
                .Include(x => x.Teacher)
                .Include(x => x.Day)
                .Include(x => x.Groups)
                .ThenInclude(x => x.Class)
                .ToListAsync();
        }

        private async Task<List<ClassPeriod>> GetClassTimetable(BakaContext db, TimetableDay day, ClassBakaId classBakaId) {
            return await db.ClassPeriodsLive
                .Where(x => x.Day.ID == day.ID
                         && x.Groups.Single().Class.BakaId.Value == classBakaId.Value)
                .ToListAsync();
        }

        public async Task<ClassPeriod> GetPeriod(BakaContext db, int ID)
            => await db.ClassPeriodsLive
                .FirstAsync(x => x.ID == ID);

        public IQueryable<ClassPeriod> GetPeriods(BakaContext db, ClassBakaId classId, string? group) {
            IQueryable<ClassPeriod> query;
            if (group == null) {
                query = db.ClassPeriodsLive.Where(x => x.Class.BakaId == classId
                                           && x.Group == null);
            } else {
                query = db.ClassPeriodsLive.Where(x => x.Class.BakaId == classId
                                           && x.Group != null
                                           && x.Group.Name == group);
            }
            return query;
        }

    }

    public class Timetable<T> : ITimetable<T>
        where T : LivePeriodBase
    {
        public List<T> Periods { get; }

        public Timetable(List<T> periods) {
            Periods = periods;
        }

        public T? GetPeriod(DateOnly date, int periodIndex, List<ClassGroup> groups) {
            var e = GetPeriods(date, periodIndex)
                .Where(x => x.Groups.SequenceEqual(groups));
            
            // todo: logging

            if (e.Count() > 1) {
                var classes = groups.Select(x => $"{x.Class.Name}:{x.Name}").Aggregate((t,c) => t+", "+c);
                throw new InvalidDataException($"timetable collision, classes='{classes}' periodIndex={periodIndex} date={date}");
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

        public T? GetPeriod(DateOnly date, int periodIndex, List<ClassGroup> group);
        public IEnumerable<T> GetPeriods(DateOnly date, int periodIndex);
    }
}
