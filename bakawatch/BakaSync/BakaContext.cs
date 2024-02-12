using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    public class BakaContext : DbContext
    {
        public DbSet<Class> Classes { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Room> Rooms { get; set; }
        public DbSet<TimetableWeek> TimetableWeeks { get; set; }
        public DbSet<TimetableDay> TimetableDays { get; set; }
        public DbSet<Period> Periods { get; set; }
        public DbSet<ClassGroup> Groups { get; set; }
        public DbSet<Subject> Subjects { get; set; }

        public IQueryable<Period> LivePeriods { get => Periods.Where(x => !x.IsHistory); }
        public IQueryable<Period> PeriodHistory { get => Periods.Where(x => x.IsHistory); }
        
        [Obsolete("bruh", true)]
        public DbSet<PeriodHistory> PeriodHistoriesObsolete { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {

            optionsBuilder.UseSqlite($"Data source=baka.db");
        }
    }
}
