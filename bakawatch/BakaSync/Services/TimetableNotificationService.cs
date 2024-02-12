using bakawatch.BakaSync.Entities;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Services {
    public class TimetableNotificationService {
        public delegate void DelClassPeriodChanged(Period currentPeriod, Period oldPeriod);
        public event DelClassPeriodChanged? OnClassPeriodChanged;

        internal void FireClassPeriodChanged(Period currentPeriod, Period oldPeriod)
            => OnClassPeriodChanged?.Invoke(currentPeriod, oldPeriod);

        public delegate void DelClassPeriodAdded(Period period);
        public event DelClassPeriodAdded? OnClassPeriodAdded;

        internal void FireClassPeriodAdded(Period period)
            => OnClassPeriodAdded?.Invoke(period);

        public delegate void DelClassPeriodDropped(Period period);
        public event DelClassPeriodDropped? OnClassPeriodDropped;

        internal void FireClassPeriodDropped(Period period)
            => OnClassPeriodDropped?.Invoke(period);
    }
}
