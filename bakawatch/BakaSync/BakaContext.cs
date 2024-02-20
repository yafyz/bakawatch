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
        public DbSet<ClassGroup> Groups { get; set; }
        public DbSet<Subject> Subjects { get; set; }

        public DbSet<LivePeriod> LivePeriods { get; set; }
        public IQueryable<LivePeriod> LivePeriodsWithIncludes
            => LivePeriods
                .Include(x => x.Subject)
                .Include(x => x.Room)
                .Include(x => x.Teacher)
                .Include(x => x.Day)
                .Include(x => x.Groups)
                .ThenInclude(x => x.Class);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data source=baka.db")
                .EnableDetailedErrors(true);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {

        }
    }
}
