using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Entities {
    public class PeriodBase {
        public int ID { get; set; }
        public required BakaTimetableParser.Who Who { get; set; }

        public Subject? Subject { get; set; }
        public Room? Room { get; set; }
        public Teacher? Teacher { get; set; }
        public required HashSet<ClassGroup> Groups { get; set; }

        public bool IsHistory { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
