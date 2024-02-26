using bakawatch.BakaSync.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    public class TeacherPeriod {
        public LivePeriod Period { get; init; }

        public TeacherPeriod(LivePeriod period) {
            if (period.Who != BakaTimetableParser.Who.Teacher)
                throw new ArgumentException($"Expected Who=Teacher, got Who={period.Who}");
            Period = period;
        }

        public Subject? Subject => Period.Subject;
        public Room? Room => Period.Room;
        public Teacher Teacher => Period.Teacher!;
        public HashSet<ClassGroup> Groups => Period.Groups;

        public PeriodType Type => Period.Type;

        public BakaTimetableParser.Who Who => Period.Who;

        // may be set if PeriodType=Normal
        public string? ChangeInfo => Period.ChangeInfo;

        // set if PeriodType=Removed
        public string? RemovedInfo => Period.RemovedInfo;

        public bool HasAbsent => Period.HasAbsent;

        // set if PeriodType=Absent or HasAbsent=true
        public string? AbsenceInfoShort => Period.AbsenceInfoShort;
        public string? AbsenceInfoReason => Period.AbsenceInfoReason;

        public TimetableDay Day => Period.Day;
        public int PeriodIndex => Period.PeriodIndex;

        public DateTime Timestamp => Period.Timestamp;

        public bool IsHistory => Period.IsHistory;
    }
}
