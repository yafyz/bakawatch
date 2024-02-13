using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities {
    public class PeriodBase {
        public int ID { get; set; }

        public Subject? Subject { get; set; }
        public Room? Room { get; set; }
        public Teacher? Teacher { get; set; }

        public required HashSet<ClassGroup> Groups { get; set; }
    }

    public class LivePeriodBase : PeriodBase {
        public required PeriodType Type { get; set; }

        // may be set if PeriodType=Normal
        public string? ChangeInfo { get; set; }

        // set if PeriodType=Removed
        public string? RemovedInfo { get; set; }

        // set if PeriodType=Absent
        public string? AbsenceInfoShort { get; set; }
        public string? AbsenceInfoReason { get; set; }

        public required TimetableDay Day { get; set; }
        public int PeriodIndex { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public bool IsHistory { get; set; }
    }

    public enum PeriodType {
        Normal,
        Removed,
        Absent,
        // not actually a real type from baka,
        // all nullable properties will be null
        Dropped
    }
}
