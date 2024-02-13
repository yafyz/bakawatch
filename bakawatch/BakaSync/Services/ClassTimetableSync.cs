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
    internal class ClassTimetableSync(
        BakaContext _bakaContext,
        TimetableNotificationService timetableNotificationService,
        ILogger<ClassTimetableSync> _logger
    )
        : LivePeriodTimetableSync<ClassPeriod>
    {
        public Class Class { get; set; }

        protected override BakaContext bakaContext => _bakaContext;
        protected override ILogger logger => _logger;

        protected override IQueryable<ClassPeriod> PeriodHistory => bakaContext.ClassPeriodHistory;
        protected override IQueryable<ClassPeriod> LivePeriods => bakaContext.ClassPeriodsLive;

        protected override Task FirePeriodChanged(ClassPeriod newPeriod, ClassPeriod oldPeriod) {
            logger.Log(LogLevel.Information, $"Class Update {newPeriod.Day.Date}:{newPeriod.PeriodIndex} {newPeriod.Class!.Name}:{newPeriod.Group?.Name} - {oldPeriod.Subject?.Name} ({oldPeriod.Type}) => {newPeriod.Subject?.Name} ({newPeriod.Type})");
            timetableNotificationService.FireClassPeriodChanged(newPeriod, oldPeriod);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodDropped(ClassPeriod period) {
            timetableNotificationService.FireClassPeriodDropped(period);
            logger.Log(LogLevel.Information, $"Class Dropping {period.Day.Date}:{period.PeriodIndex} {period.Class!.Name} - {period.Subject?.Name} ({period.Type})");
            return Task.CompletedTask;
        }

        protected override Task FirePeriodNew(ClassPeriod period) {
            logger.LogInformation($"Class New {period.Day.Date}:{period.PeriodIndex} {period.Class!.Name}:{period.Group?.Name} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireClassPeriodAdded(period);
            return Task.CompletedTask;
        }

        protected override Task InsertPeriod(ClassPeriod newEntity) {
            bakaContext.ClassPeriods.Add(newEntity);
            return Task.CompletedTask;
        }

        protected override Task<Class> GetClassByName(string? className) {
            return Task.FromResult(Class);
        }

        protected override Task<ClassPeriod> MakeLivePeriod(LivePeriodBase genericPeriodInfo)
            => Task.FromResult(new ClassPeriod() {
                Type = genericPeriodInfo.Type,
                Groups = genericPeriodInfo.Groups,
                Room = genericPeriodInfo.Room,
                Subject = genericPeriodInfo.Subject,
                Teacher = genericPeriodInfo.Teacher,
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
