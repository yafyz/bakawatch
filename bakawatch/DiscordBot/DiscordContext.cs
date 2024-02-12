using bakawatch.DiscordBot.Entities;
using bakawatch.DiscordBot.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot {
    public class DiscordContext : DbContext {
        public DbSet<PeriodChangeNotification> PeriodChangeNotifications { get; set; }
        public DbSet<LocalDChannel> DiscordChannels { get; set; }
        public DbSet<LocalDMessage> DiscordMessages { get; set; }
        public DbSet<SubjectReminder> SubjectReminders { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            optionsBuilder.UseSqlite($"Data source=dbot.db");
        }
    }
}
