using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    public class TimetableDay
    {
        public int ID { get; set; }

        public required DateOnly Date { get; set; }

        public TimetableWeek Week { get; set; }
    }
}
