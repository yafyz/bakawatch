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

        public DbSet<ClassPeriod> ClassPeriods { get; set; }
        private IQueryable<ClassPeriod> _ClassPeriods { get =>
                ClassPeriods
                    .Include(x => x.Subject)
                    .Include(x => x.Room)
                    .Include(x => x.Teacher)
                    .Include(x => x.Day)
                    .Include(x => x.Groups)
                    .ThenInclude(x => x.Class);
        }

        public IQueryable<ClassPeriod> ClassPeriodsLive { get => _ClassPeriods.Where(x => !x.IsHistory); }
        public IQueryable<ClassPeriod> ClassPeriodHistory { get => _ClassPeriods.Where(x => x.IsHistory); }

        public DbSet<TeacherPeriod> TeacherPeriods { get; set; }
        public IQueryable<TeacherPeriod> TeacherPeriodsLive { get => TeacherPeriods.Where(x => !x.IsHistory); }
        public IQueryable<TeacherPeriod> TeacherPeriodHistory { get => TeacherPeriods.Where(x => x.IsHistory); }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data source=baka.db")
                .EnableDetailedErrors(true);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {

        }
    }
}
