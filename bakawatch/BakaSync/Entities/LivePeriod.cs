using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities {
    public class LivePeriod : PeriodBase {
        public required PeriodType Type { get; set; }

        public required BakaTimetableParser.Who Who { get; set; }

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

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public bool IsHistory { get; set; }

        public readonly static Expression<Func<LivePeriod, bool>> IsClassPeriod = p => p.Who == BakaTimetableParser.Who.Class;
        public readonly static Expression<Func<LivePeriod, bool>> IsTeacherPeriod = p => p.Who == BakaTimetableParser.Who.Teacher;

        public readonly static Expression<Func<LivePeriod, bool>> IsCurrent = p => !p.IsHistory;
        public readonly static Expression<Func<LivePeriod, bool>> IsHistorical = p => p.IsHistory;

        public static Expression<Func<LivePeriod, bool>> ByClass(Class @class) => p => p.Groups.Any(x => x.Class == @class);
        public static Expression<Func<LivePeriod, bool>> ByClassBakaId(ClassBakaId id) => p => p.Groups.Any(x => x.Class.BakaId.Value == id.Value);
        public static Expression<Func<LivePeriod, bool>> ByGroup(ClassGroup group) => p => p.Groups.Contains(group);
        public static Expression<Func<LivePeriod, bool>> ByGroupName(string groupName) => p => p.Groups.Any(g => g.Name == groupName);
        public readonly static Expression<Func<LivePeriod, bool>> ByDefaultGroup = p => p.Groups.Single().IsDefaultGroup;

        public static Expression<Func<LivePeriod, bool>> ByTeacher(Teacher teacher) => p => p.Teacher == teacher;
        public static Expression<Func<LivePeriod, bool>> ByTeacherBakaId(TeacherBakaId id) => p => p.Teacher != null && p.Teacher.BakaId.Value == id.Value;
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
