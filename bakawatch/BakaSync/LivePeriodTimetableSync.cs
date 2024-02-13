using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bakawatch.BakaSync {
    internal abstract class LivePeriodTimetableSync<T> : GenericTimetableSync<T, T>
        where T : LivePeriodBase
    {
        protected abstract IQueryable<T> PeriodHistory { get; }
        protected abstract IQueryable<T> LivePeriods { get; }

        protected abstract Task<T> MakeLivePeriod(LivePeriodBase genericPeriodInfo);

        protected override Task<bool> ComparePeriods(T p1, T p2) {
            if (p1.Day.ID != p2.Day.ID)
                throw new InvalidDataException("period not same day");

            if (p1.PeriodIndex != p2.PeriodIndex)
                throw new InvalidDataException("period not same index");

            bool areEqual = p1.Type == p2.Type
                         && p1.Groups.SequenceEqual(p2.Groups)
                         && p1.Subject?.ID == p2.Subject?.ID
                         && p1.Room?.ID == p2.Room?.ID
                         && p1.Teacher?.ID == p2.Teacher?.ID
                         && p1.ChangeInfo == p2.ChangeInfo
                         && p1.RemovedInfo == p2.RemovedInfo
                         && p1.AbsenceInfoShort == p2.AbsenceInfoShort
                         && p1.AbsenceInfoReason == p2.AbsenceInfoReason;

            return Task.FromResult(areEqual);
        }

        protected override async Task<T?> GetHistoryByPeriod(T period)
            => await PeriodHistory
                // id should be the same
                .Where(x => x.ID == period.ID)
                .FirstOrDefaultAsync();

        protected override async Task<T?> GetPeriodByPeriod(T period)
            => await LivePeriods
                .Where(x => x == period)
                .FirstOrDefaultAsync();

        protected override Task<bool> IsPeriodDropped(T period)
            => Task.FromResult(period.Type == PeriodType.Dropped);

        protected override Task MakePeriodDropped(T period) {
            period.Type = PeriodType.Dropped;
            return Task.CompletedTask;
        }

        protected override async Task<T> ParseIntoPeriod(BakaTimetableParser.PeriodInfo periodInfo) {
            var day = await bakaContext.TimetableDays.FirstAsync(x => x.Date == periodInfo.Date);

            var basePeriod = await ParseIntoBasePeriod(periodInfo);

            var period = new LivePeriodBase() {
                Type = periodInfo.JsonData.type switch {
                    "atom" => PeriodType.Normal,
                    "removed" => PeriodType.Removed,
                    "absent" => PeriodType.Absent,
                    _ => throw new InvalidDataException($"\"{periodInfo.JsonData.type}\" is not a valid period type")
                },

                Groups = basePeriod.Groups,

                Subject = basePeriod.Subject,
                Room = basePeriod.Room,
                Teacher = basePeriod.Teacher,

                ChangeInfo = periodInfo.JsonData.changeinfo,
                RemovedInfo = periodInfo.JsonData.removedinfo,
                AbsenceInfoShort = periodInfo.JsonData.absentinfo,
                AbsenceInfoReason = periodInfo.JsonData.InfoAbsentName,

                Day = day,
                PeriodIndex = periodInfo.PeriodIndex
            };

            return await MakeLivePeriod(period);
        }

        protected override Task<T> MakeIntoHistory(T period) {
            period.IsHistory = true;
            return Task.FromResult(period);
        }
    }
}
