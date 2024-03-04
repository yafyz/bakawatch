using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    internal abstract class PermanentPeriodTimetableSync : GenericTimetableSync<PermanentPeriod, PermanentPeriod>
    {
        protected abstract IQueryable<PermanentPeriod> PeriodHistory { get; }
        protected abstract IQueryable<PermanentPeriod> PermanentPeriods { get; }

        protected override Task<bool> ComparePeriods(PermanentPeriod p1, PermanentPeriod p2) {
            if (p1.OddOrEvenWeek != p1.OddOrEvenWeek)
                throw new InvalidDataException("period not same oddness");

            if (p1.DayOfWeek != p1.DayOfWeek)
                throw new InvalidDataException("period not same week day");

            if (p1.PeriodIndex != p2.PeriodIndex)
                throw new InvalidDataException("period not same index");

            bool areEqual = p1.Groups.SetEquals(p2.Groups)
                         && p1.Subject?.ID == p2.Subject?.ID
                         && p1.Room?.ID == p2.Room?.ID
                         && p1.Teacher?.ID == p2.Teacher?.ID;

            return Task.FromResult(areEqual);
        }

        protected override async Task<PermanentPeriod?> GetHistoryByPeriod(PermanentPeriod period)
            => await PeriodHistory
                // id should be the same
                .Where(x => x.ID == period.ID)
                .FirstOrDefaultAsync();

        protected override async Task<PermanentPeriod?> GetPeriodByPeriod(PermanentPeriod period)
            => await PermanentPeriods
                .Where(x => x == period)
                .FirstOrDefaultAsync();

        protected override Task MakePeriodDropped(PermanentPeriod period) {
            period.IsHistory = true;
            return Task.CompletedTask;
        }

        protected override Task<bool> IsPeriodDropped(PermanentPeriod period) {
            throw new UnreachableException();
        }

        protected override Task<PermanentPeriod> MakeIntoHistory(PermanentPeriod period) {
            period.IsHistory = true;
            return Task.FromResult(period);
        }

        protected override Task InsertPeriod(PermanentPeriod newEntity) {
            bakaContext.PermanentPeriods.Add(newEntity);
            return Task.CompletedTask;
        }

        protected override async Task<PermanentPeriod> ParseIntoPeriod(BakaTimetableParser.PeriodInfo periodInfo) {
            var basePeriod = await ParseIntoBasePeriod(periodInfo);

            var period = new PermanentPeriod() {
                Who = basePeriod.Who,

                Groups = basePeriod.Groups,

                Subject = basePeriod.Subject,
                Room = basePeriod.Room,
                Teacher = basePeriod.Teacher,

                PeriodIndex = periodInfo.PeriodIndex,
                DayOfWeek = periodInfo.DayOfWeek,
                OddOrEvenWeek = periodInfo.OddOrEvenWeek
            };

            return period;
        }
    }
}
