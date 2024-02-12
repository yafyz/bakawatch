using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities {

    // will get changed in the future probably
    // to accommodate for permanent timetables
    // and timetables of teachers

    public abstract class PeriodBase {
        // it is not wise to directly reference
        // periods by id as they may get removed
        // from db at any point instead of
        // getting overwritten
        public int ID { get; set; }

        public Class? Class { get; set; }
        public Subject? Subject { get; set; }
        public Room? Room { get; set; }
        public Teacher? Teacher { get; set; }
        public ClassGroup? Group { get; set; }
    }

    public abstract class LivePeriodBase : PeriodBase {
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
