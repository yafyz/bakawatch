using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    public class PermanentPeriod : PeriodBase
    {
        public required int PeriodIndex { get; set; }
        public required DayOfWeek DayOfWeek { get; set; }
        public required OddEven OddOrEvenWeek { get; set; }

        public bool CompareWithLive(LivePeriod livePeriod) {
            if (livePeriod.Day.Week.OddEven != OddOrEvenWeek)
                throw new InvalidDataException("period not same oddness");

            if (livePeriod.Day.Date.DayOfWeek != DayOfWeek)
                throw new InvalidDataException("period not same week day");

            if (livePeriod.PeriodIndex != PeriodIndex)
                throw new InvalidDataException("period not same index");

            return livePeriod.Who == Who
                && livePeriod.Subject == Subject
                && livePeriod.Room == Room
                && livePeriod.Groups.SetEquals(livePeriod.Groups);
        }
    }
}
