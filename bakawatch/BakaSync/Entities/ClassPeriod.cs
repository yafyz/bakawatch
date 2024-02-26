using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    public class ClassPeriod : LivePeriodBase {
        public Class Class {
            get => base.Groups.Single().Class;
        }

        public ClassGroup Group {
            get => base.Groups.Single();
        }
    }
}
