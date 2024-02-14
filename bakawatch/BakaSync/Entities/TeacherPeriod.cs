using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities {
    public class TeacherPeriod : LivePeriodBase {
        public new Teacher Teacher { get => base.Teacher!; set => base.Teacher = value; }
        public bool HasAbsent { get; set; }
    }
}
