using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Services
{
    internal class TeacherTimetableSync(
        BakaContext _bakaContext,
        TimetableNotificationService timetableNotificationService,
        ILogger<TeacherTimetableSync> _logger
    )
        : LivePeriodTimetableSync
    {
        public Teacher Teacher { get; set; }
        public override string? Tag => $"teacher={Teacher.FullName}";

        protected override BakaContext bakaContext => _bakaContext;
        protected override ILogger logger => _logger;

        protected override IQueryable<LivePeriod> LivePeriods
            => bakaContext.LivePeriodsWithIncludes
                .Where(LivePeriodQuery.IsTeacherPeriod)
                .Where(LivePeriodQuery.IsCurrent);
        protected override IQueryable<LivePeriod> PeriodHistory
            => bakaContext.LivePeriodsWithIncludes
                .Where(LivePeriodQuery.IsTeacherPeriod)
                .Where(LivePeriodQuery.IsHistorical);
        protected override BakaTimetableParser.Who Who => BakaTimetableParser.Who.Teacher;

        protected override Task FirePeriodChanged(LivePeriod _newPeriod, LivePeriod _oldPeriod) {
            TeacherPeriod newPeriod = new(_newPeriod);
            TeacherPeriod oldPeriod = new(_oldPeriod);
            logger.Log(LogLevel.Information, $"Teacher Update {newPeriod.Day.Date}:{newPeriod.PeriodIndex} {newPeriod.Teacher.FullName} - {oldPeriod.Subject?.Name} ({oldPeriod.Type}) => {newPeriod.Subject?.Name} ({newPeriod.Type})");
            timetableNotificationService.FireTeacherPeriodChanged(newPeriod, oldPeriod);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodDropped(LivePeriod _period) {
            TeacherPeriod period = new(_period);
            logger.Log(LogLevel.Information, $"Teacher Dropping {period.Day.Date}:{period.PeriodIndex} {period.Teacher.FullName} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireTeacherPeriodDropped(period);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodNew(LivePeriod _period) {
            TeacherPeriod period = new(_period);
            logger.LogInformation($"Teacher New {period.Day.Date}:{period.PeriodIndex} {period.Teacher.FullName} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireTeacherPeriodAdded(period);
            return Task.CompletedTask;
        }

        protected override Task<Teacher?> GetTeacher(string? teacherName) {
            if (teacherName != null)
                throw new ArgumentException($"expected teacherName=null, got teacherName={teacherName}");
            return Task.FromResult<Teacher?>(Teacher);
        }
    }
}
