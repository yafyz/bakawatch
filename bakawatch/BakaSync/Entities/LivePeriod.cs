using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities {
    public class LivePeriod : PeriodBase {
        public required PeriodType Type { get; set; }

        // may be set if PeriodType=Normal
        public string? ChangeInfo { get; set; }

        // set if PeriodType=Removed
        public string? RemovedInfo { get; set; }

        public bool HasAbsent { get; set; }

        // set if PeriodType=Absent or HasAbsent=true
        public string? AbsenceInfoShort { get; set; }
        public string? AbsenceInfoReason { get; set; }
        
        public required TimetableDay Day { get; set; }
        public int PeriodIndex { get; set; }
    }

    public enum PeriodType {
        Normal,
        Removed,
        Absent,

        /* -- not actually a real types from baka -- */

        // all nullable properties will be null
        Dropped,

        // RemovedInfo will maybe contain reason,
        // everything else nullable will be null
        Holiday
    }
}
