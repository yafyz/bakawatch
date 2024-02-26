using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities
{
    public class TimetableWeek
    {
        public int ID { get; set; }

        public required OddEven OddEven { get; set; }
        public required DateOnly StartDate { get; set; }
        public required DateOnly EndDate { get; set; }

        public ICollection<TimetableDay> Days { get; set; }
    }

    public enum OddEven
    {
        None,
        Even,
        Odd
    }
}
