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
        BakaContext bakaContext,
        TimetableNotificationService timetableNotificationService,
        ILogger<ClassTimetableSync> logger
    )
        : GenericTimetableSync<Period, Period>(bakaContext, logger)
    {
        public Class Class { get; set; }

        protected override Task<bool> ComparePeriods(Period p1, Period p2) {
            if (p1.Day.ID != p2.Day.ID)
                throw new InvalidDataException("period not same day");

            if (p1.PeriodIndex != p2.PeriodIndex)
                throw new InvalidDataException("period not same index");

            bool areEqual = p1.Type == p2.Type
                         && p1.Class?.ID == p2.Class?.ID
                         && p1.Subject?.ID == p2.Subject?.ID
                         && p1.Room?.ID == p2.Room?.ID
                         && p1.Teacher?.ID == p2.Teacher?.ID
                         && p1.Group?.ID == p2.Group?.ID
                         && p1.ChangeInfo == p2.ChangeInfo
                         && p1.RemovedInfo == p2.RemovedInfo
                         && p1.AbsenceInfoShort == p2.AbsenceInfoShort
                         && p1.AbsenceInfoReason == p2.AbsenceInfoReason;

            return Task.FromResult(areEqual);
        }

        protected override Task FirePeriodChanged(Period newPeriod, Period oldPeriod) {
            logger.Log(LogLevel.Information, $"Update {newPeriod.Day.Date}:{newPeriod.PeriodIndex} {newPeriod.Class!.Name}:{newPeriod.Group?.Name} - {oldPeriod.Subject?.Name} ({oldPeriod.Type}) => {oldPeriod.Subject?.Name} ({oldPeriod.Type})");
            timetableNotificationService.FireClassPeriodChanged(newPeriod, oldPeriod);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodDropped(Period period) {
            timetableNotificationService.FireClassPeriodDropped(period);
            logger.Log(LogLevel.Information, $"Dropping {period.Day.Date}:{period.PeriodIndex} {period.Class!.Name} - {period.Subject?.Name} ({period.Type})");
            return Task.CompletedTask;
        }

        protected override Task FirePeriodNew(Period period) {
            logger.LogInformation($"New {period.Day.Date}:{period.PeriodIndex} {period.Class!.Name}:{period.Group?.Name} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireClassPeriodAdded(period);
            return Task.CompletedTask;
        }

        protected override async Task<Period?> GetHistoryByPeriod(Period period)
            => await bakaContext.PeriodHistory
                .Where(x => x.Class == period.Class
                         && x.Day == period.Day
                         && x.PeriodIndex == period.PeriodIndex
                         && x.Group == period.Group)
                .FirstOrDefaultAsync();

        protected override async Task<Period?> GetPeriodByPeriod(Period period)
            => await bakaContext.LivePeriods
                .Where(x => x == period)
                .FirstOrDefaultAsync();

        protected override Task InsertPeriod(Period newEntity) {
            bakaContext.Periods.Add(newEntity);
            return Task.CompletedTask;
        }

        protected override Task<bool> IsPeriodDropped(Period period)
            => Task.FromResult(period.Type == PeriodType.Dropped);

        protected override Task MakePeriodDropped(Period period) {
            period.Type = PeriodType.Dropped;
            return Task.CompletedTask;
        }

        protected override async Task<Period> ParseIntoPeriod(BakaTimetableParser.PeriodInfo periodInfo) {
            (var @class, var group) = await GetClassAndGroup(periodInfo);
            var day = await bakaContext.TimetableDays.FirstAsync(x => x.Date == periodInfo.Date);

            Subject? subject = null;
            // periodInfo.SubjectShortName may be set even if its not actually a subject but an absence
            if (periodInfo.JsonData.type == "atom")
                subject = await GetSubject(periodInfo.SubjectShortName, periodInfo.SubjectFullName);

            Room? room = await GetRoom(periodInfo.JsonData.room);

            Teacher? teacher = null;
            if (periodInfo.TeacherFullNameNoDegree != null)
                teacher = await bakaContext.Teachers.FirstAsync(x => x.FullName == periodInfo.TeacherFullNameNoDegree);

            var period = new Period() {
                Type = periodInfo.JsonData.type switch {
                    "atom" => PeriodType.Normal,
                    "removed" => PeriodType.Removed,
                    "absent" => PeriodType.Absent,
                    _ => throw new InvalidDataException($"\"{periodInfo.JsonData.type}\" is not a valid period type")
                },

                Class = @class,

                Subject = subject,
                Room = room,
                Teacher = teacher,
                Group = group,

                ChangeInfo = periodInfo.JsonData.changeinfo,
                RemovedInfo = periodInfo.JsonData.removedinfo,
                AbsenceInfoShort = periodInfo.JsonData.absentinfo,
                AbsenceInfoReason = periodInfo.JsonData.InfoAbsentName,

                Day = day,
                PeriodIndex = periodInfo.PeriodIndex
            };

            return period;
        }

        protected override Task RemovePeriod(Period entity) {
            // well do nothing
            return Task.CompletedTask;
        }

        protected override async Task<(Class, ClassGroup?)> GetClassAndGroup(BakaTimetableParser.PeriodInfo pper) {
            return (Class, await GetGroup(Class, pper.JsonData.group));
        }

        protected override Task<Period> MakeIntoHistory(Period period) {
            period.IsHistory = true;
            return Task.FromResult(period);
        }
    }
}
