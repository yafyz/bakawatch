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
        : LivePeriodTimetableSync<TeacherPeriod>
    {
        public Teacher Teacher { get; set; }

        protected override BakaContext bakaContext => _bakaContext;
        protected override ILogger logger => _logger;

        protected override IQueryable<TeacherPeriod> LivePeriods => bakaContext.TeacherPeriodsLive;
        protected override IQueryable<TeacherPeriod> PeriodHistory => bakaContext.TeacherPeriodHistory;

        protected override Task FirePeriodChanged(TeacherPeriod newPeriod, TeacherPeriod oldPeriod) {
            logger.Log(LogLevel.Information, $"Teacher Update {newPeriod.Day.Date}:{newPeriod.PeriodIndex} {newPeriod.Teacher.FullName} - {oldPeriod.Subject?.Name} ({oldPeriod.Type}) => {newPeriod.Subject?.Name} ({newPeriod.Type})");
            timetableNotificationService.FireTeacherPeriodChanged(newPeriod, oldPeriod);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodDropped(TeacherPeriod period) {
            logger.Log(LogLevel.Information, $"Teacher Dropping {period.Day.Date}:{period.PeriodIndex} {period.Teacher.FullName} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireTeacherPeriodDropped(period);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodNew(TeacherPeriod period) {
            logger.LogInformation($"Teacher New {period.Day.Date}:{period.PeriodIndex} {period.Teacher.FullName} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireTeacherPeriodAdded(period);
            return Task.CompletedTask;
        }

        protected override Task InsertPeriod(TeacherPeriod newEntity) {
            bakaContext.TeacherPeriods.Add(newEntity);
            return Task.CompletedTask;
        }

        protected override Task<Teacher?> GetTeacher(string? teacherName) {
            return Task.FromResult<Teacher?>(Teacher);
        }

        protected override Task<TeacherPeriod> MakeLivePeriod(LivePeriodBase genericPeriodInfo)
            => Task.FromResult(new TeacherPeriod() {
                Type = genericPeriodInfo.Type,
                Groups = genericPeriodInfo.Groups,
                Room = genericPeriodInfo.Room,
                Subject = genericPeriodInfo.Subject,
                Teacher = genericPeriodInfo.Teacher!,
                ChangeInfo = genericPeriodInfo.ChangeInfo,
                RemovedInfo = genericPeriodInfo.RemovedInfo,
                AbsenceInfoShort = genericPeriodInfo.AbsenceInfoShort,
                AbsenceInfoReason = genericPeriodInfo.AbsenceInfoReason,
                Day = genericPeriodInfo.Day,
                PeriodIndex = genericPeriodInfo.PeriodIndex,
                Timestamp = genericPeriodInfo.Timestamp,
                IsHistory = genericPeriodInfo.IsHistory,
            });
    }
}
