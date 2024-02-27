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
    }
}
