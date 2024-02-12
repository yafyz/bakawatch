using bakawatch.BakaSync.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Entities {
    public class PeriodChangeNotification {
        public int ID { get; set; }

        public required LocalDChannel Channel { get; set; }
        public required ClassBakaId ClassBakaId { get; set; }
        public string? GroupName { get; set; }
    }
}
