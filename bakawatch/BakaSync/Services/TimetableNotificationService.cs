using bakawatch.BakaSync.Entities;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Services {
    public class TimetableNotificationService {
        public delegate void DelClassPeriodChanged(ClassPeriod currentPeriod, ClassPeriod oldPeriod);
        public event DelClassPeriodChanged? OnClassPeriodChanged;

        internal void FireClassPeriodChanged(ClassPeriod currentPeriod, ClassPeriod oldPeriod)
            => OnClassPeriodChanged?.Invoke(currentPeriod, oldPeriod);

        public delegate void DelClassPeriodAdded(ClassPeriod period);
        public event DelClassPeriodAdded? OnClassPeriodAdded;

        internal void FireClassPeriodAdded(ClassPeriod period)
            => OnClassPeriodAdded?.Invoke(period);

        public delegate void DelClassPeriodDropped(ClassPeriod period);
        public event DelClassPeriodDropped? OnClassPeriodDropped;

        internal void FireClassPeriodDropped(ClassPeriod period)
            => OnClassPeriodDropped?.Invoke(period);


        public delegate void DelTeacherPeriodChanged(TeacherPeriod currentPeriod, TeacherPeriod oldPeriod);
        public event DelTeacherPeriodChanged? OnTeacherPeriodChanged;

        internal void FireTeacherPeriodChanged(TeacherPeriod currentPeriod, TeacherPeriod oldPeriod)
            => OnTeacherPeriodChanged?.Invoke(currentPeriod, oldPeriod);

        public delegate void DelTeacherPeriodAdded(TeacherPeriod period);
        public event DelTeacherPeriodAdded? OnTeacherPeriodAdded;

        internal void FireTeacherPeriodAdded(TeacherPeriod period)
            => OnTeacherPeriodAdded?.Invoke(period);

        public delegate void DelTeacherPeriodDropped(TeacherPeriod period);
        public event DelTeacherPeriodDropped? OnTeacherPeriodDropped;

        internal void FireTeacherPeriodDropped(TeacherPeriod period)
            => OnTeacherPeriodDropped?.Invoke(period);
    }
}
