using bakawatch.BakaSync.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    public class PeriodQuery<T> where T : PeriodBase
    {
        public readonly static Expression<Func<T, bool>> IsClassPeriod = p => p.Who == BakaTimetableParser.Who.Class;
        public readonly static Expression<Func<T, bool>> IsTeacherPeriod = p => p.Who == BakaTimetableParser.Who.Teacher;

        public readonly static Expression<Func<T, bool>> IsCurrent = p => !p.IsHistory;
        public readonly static Expression<Func<T, bool>> IsHistorical = p => p.IsHistory;

        public static Expression<Func<T, bool>> ByClass(Class @class) => p => p.Groups.Any(x => x.Class == @class);
        public static Expression<Func<T, bool>> ByClassBakaId(ClassBakaId id) => p => p.Groups.Any(x => x.Class.BakaId.Value == id.Value);
        public static Expression<Func<T, bool>> ByGroup(ClassGroup group) => p => p.Groups.Contains(group);
        public static Expression<Func<T, bool>> ByGroupName(string groupName) => p => p.Groups.Any(g => g.Name == groupName);
        public readonly static Expression<Func<T, bool>> ByDefaultGroup = p => p.Groups.Single().IsDefaultGroup;

        public static Expression<Func<T, bool>> ByTeacher(Teacher teacher) => p => p.Teacher == teacher;
        public static Expression<Func<T, bool>> ByTeacherBakaId(TeacherBakaId id) => p => p.Teacher != null && p.Teacher.BakaId.Value == id.Value;
    }

    public class LivePeriodQuery : PeriodQuery<LivePeriod>;
    public class PermanentPeriodQuery : PeriodQuery<PermanentPeriod>;
}
