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

        public async Task<Timetable> GetTimetable(TimetableWeek week, ClassBakaId id) {
            using var scope = serviceScopeFactory.CreateAsyncScope();
            using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();

            var periods = await week.Days
                .ToAsyncEnumerable()
                .Where(x => x.Week.ID == week.ID)
                .SelectAwait(async x => await GetTimetable(db, x, id))
                .SelectMany(x => x.ToAsyncEnumerable())
                .ToListAsync();

            return new Timetable(periods, id);
        }

        private async Task<List<Period>> GetTimetable(BakaContext db, TimetableDay day, ClassBakaId classBakaId) {
            return await db.Periods
                .Where(x => x.Day.ID == day.ID
                         && x.Class.BakaId == classBakaId)
                .Include(x => x.Class)
                .Include(x => x.Subject)
                .Include(x => x.Room)
                .Include(x => x.Teacher)
                .Include(x => x.Group)
                .Include(x => x.Day)
                .ToListAsync();
        }

        public async Task<Period> GetPeriod(BakaContext db, int ID)
            => await db.Periods
                .Include(x => x.Class)
                .Include(x => x.Subject)
                .Include(x => x.Room)
                .Include(x => x.Teacher)
                .Include(x => x.Group)
                .Include(x => x.Day)
                .FirstAsync(x => x.ID == ID);

        public IQueryable<Period> GetPeriods(BakaContext db, ClassBakaId classId, string? group) {
            IQueryable<Period> query;
            if (group == null) {
                query = db.Periods.Where(x => x.Class.BakaId == classId
                                           && x.Group == null);
            } else {
                query = db.Periods.Where(x => x.Class.BakaId == classId
                                           && x.Group != null
                                           && x.Group.Name == group);
            }
            return query
                .Include(x => x.Class)
                .Include(x => x.Subject)
                .Include(x => x.Room)
                .Include(x => x.Teacher)
                .Include(x => x.Group)
                .Include(x => x.Day);
        }

    }

    public class Timetable : ITimetable<Period> {
        public List<Period> Periods { get; }

        public Timetable(List<Period> periods, ClassBakaId classId) {
            Periods = periods;
        }

        public Period? GetPeriod(DateOnly date, int periodIndex, ClassGroup? group = null) {
            var e = GetPeriods(date, periodIndex)
                .Where(x => x.Group?.ID == group?.ID);
            
            // todo: logging

            if (e.Count() > 1) {
                throw new InvalidDataException($"timetable collision, classId={"ClassId"} group={group?.Name} periodIndex={periodIndex}");
            }

            return e.FirstOrDefault();
        }

        public IEnumerable<Period> GetPeriods(DateOnly date, int periodIndex) {
            return Periods.Where(x => x.Day.Date == date
                                    && x.PeriodIndex == periodIndex);
        }
    }

    public interface ITimetable<T> {
        public List<T> Periods { get; }

        public T? GetPeriod(DateOnly date, int periodIndex, ClassGroup? group = null);
        public IEnumerable<T> GetPeriods(DateOnly date, int periodIndex);
    }
}
