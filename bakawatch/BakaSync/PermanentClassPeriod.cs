using bakawatch.BakaSync.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    public class PermanentClassPeriod
    {
        public PermanentPeriod Period { get; init; }

        public PermanentClassPeriod(PermanentPeriod period) {
            if (period.Who != BakaTimetableParser.Who.Class)
                throw new ArgumentException($"Expected Who=Class, got Who={period.Who}");
            Period = period;
        }

        public Class Class => Period.Groups.Single().Class;
        public ClassGroup Group => Period.Groups.Single();

        public Subject? Subject => Period.Subject;
        public Room? Room => Period.Room;
        public Teacher? Teacher => Period.Teacher;
        public HashSet<ClassGroup> Groups => Period.Groups;

        public BakaTimetableParser.Who Who => Period.Who;

        public int PeriodIndex => Period.PeriodIndex;
        public DayOfWeek DayOfWeek => Period.DayOfWeek;
        public OddEven OddOrEvenWeek => Period.OddOrEvenWeek;
    }
}
