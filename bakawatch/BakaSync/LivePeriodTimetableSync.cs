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


    internal abstract class LivePeriodTimetableSync : GenericTimetableSync<LivePeriod, LivePeriod>
    {
        protected abstract IQueryable<LivePeriod> PeriodHistory { get; }
        protected abstract IQueryable<LivePeriod> LivePeriods { get; }

        protected override Task<bool> ComparePeriods(LivePeriod p1, LivePeriod p2) {
            if (p1.Day.ID != p2.Day.ID)
                throw new InvalidDataException("period not same day");

            if (p1.PeriodIndex != p2.PeriodIndex)
                throw new InvalidDataException("period not same index");

            bool areEqual = p1.Type == p2.Type
                         && p1.Groups.SetEquals(p2.Groups)
                         && p1.Subject?.ID == p2.Subject?.ID
                         && p1.Room?.ID == p2.Room?.ID
                         && p1.Teacher?.ID == p2.Teacher?.ID
                         && p1.ChangeInfo == p2.ChangeInfo
                         && p1.RemovedInfo == p2.RemovedInfo
                         && p1.AbsenceInfoShort == p2.AbsenceInfoShort
                         && p1.AbsenceInfoReason == p2.AbsenceInfoReason;

            return Task.FromResult(areEqual);
        }

        protected override async Task<LivePeriod?> GetHistoryByPeriod(LivePeriod period)
            => await PeriodHistory
                // id should be the same
                .Where(x => x.ID == period.ID)
                .FirstOrDefaultAsync();

        protected override async Task<LivePeriod?> GetPeriodByPeriod(LivePeriod period)
            => await LivePeriods
                .Where(x => x == period)
                .FirstOrDefaultAsync();

        protected override Task<bool> IsPeriodDropped(LivePeriod period)
            => Task.FromResult(period.Type == PeriodType.Dropped);

        protected override Task MakePeriodDropped(LivePeriod period) {
            period.Type = PeriodType.Dropped;
            return Task.CompletedTask;
        }

        protected override async Task<LivePeriod> ParseIntoPeriod(BakaTimetableParser.PeriodInfo periodInfo) {
            var day = await bakaContext.TimetableDays.FirstAsync(x => x.Date == periodInfo.Date);

            var basePeriod = await ParseIntoBasePeriod(periodInfo);

            var period = new LivePeriod() {
                Type = periodInfo.JsonData.type switch {
                    "atom" => PeriodType.Normal,
                    "removed" => PeriodType.Removed,
                    "absent" => PeriodType.Absent,
                    _ => throw new InvalidDataException($"\"{periodInfo.JsonData.type}\" is not a valid period type")
                },
                Who = Who,

                Groups = basePeriod.Groups,

                Subject = basePeriod.Subject,
                Room = basePeriod.Room,
                Teacher = basePeriod.Teacher,

                ChangeInfo = periodInfo.JsonData.changeinfo,
                RemovedInfo = periodInfo.JsonData.removedinfo,
                AbsenceInfoShort = periodInfo.JsonData.absentinfo,
                AbsenceInfoReason = periodInfo.JsonData.InfoAbsentName,
                HasAbsent = periodInfo.JsonData.hasAbsent,

                Day = day,
                PeriodIndex = periodInfo.PeriodIndex
            };

            return period;
        }

        protected override Task<LivePeriod> MakeIntoHistory(LivePeriod period) {
            period.IsHistory = true;
            return Task.FromResult(period);
        }

        protected override Task InsertPeriod(LivePeriod newEntity) {
            bakaContext.LivePeriods.Add(newEntity);
            return Task.CompletedTask;
        }
    }
}
