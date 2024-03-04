using bakawatch.BakaSync.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Entities
{
    public class SubjectReminder
    {
        public int ID { get; set; }
        public required Guid Guid { get; set; }

        public required ClassBakaId ClassBakaId { get; set; }
        public required string? GroupName { get; set; }
        
        public required string SubjectShortName { get; set; }
        
        public required string Description { get; set; }

        public required DateOnly Date { get; set; }
        public required int ToSkipCount { get; set; }

        public int SkippedCount { get; set; } = 0;
        public int SkipsRemaining { get => ToSkipCount - SkippedCount; }

        public bool MessageUpdatePending { get; set; }
        public required LocalDMessage Message { get; set; }

        public bool Finished { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        public DateOnly LatestDate { get; set; }
        public int LatestPeriodIndex { get; set; }
    }
}
