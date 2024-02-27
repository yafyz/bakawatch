using bakawatch.BakaSync.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Services
{
    internal class ClassPermanentTimetableSync(
        BakaContext _bakaContext,
        TimetableNotificationService timetableNotificationService,
        ILogger<ClassLiveTimetableSync> _logger
    )
        : PermanentPeriodTimetableSync
    {
        public Class Class { get; set; }
        protected override BakaTimetableParser.Who Who => BakaTimetableParser.Who.Class;
        
        public override string? Tag => $"class={Class.Name}";
        protected override BakaContext bakaContext => _bakaContext;
        protected override ILogger logger => _logger;

        protected override IQueryable<PermanentPeriod> PeriodHistory
            => bakaContext.PermanentPeriodsWithIncludes
                .Where(PermanentPeriodQuery.IsHistorical)
                .Where(PermanentPeriodQuery.IsClassPeriod)
                .Where(PermanentPeriodQuery.ByClass(Class));
        protected override IQueryable<PermanentPeriod> PermanentPeriods
            => bakaContext.PermanentPeriodsWithIncludes
                .Where(PermanentPeriodQuery.IsCurrent)
                .Where(PermanentPeriodQuery.IsClassPeriod)
                .Where(PermanentPeriodQuery.ByClass(Class));

        protected override Task FirePeriodChanged(PermanentPeriod _newPeriod, PermanentPeriod _periodHistory) {
            PermanentClassPeriod newPeriod = new(_newPeriod);
            PermanentClassPeriod oldPeriod = new(_periodHistory);
            logger.Log(LogLevel.Information, $"Class Update {newPeriod.OddOrEvenWeek}:{newPeriod.DayOfWeek}:{newPeriod.PeriodIndex} {newPeriod.Class!.Name}:{newPeriod.Group?.Name} - {oldPeriod.Subject?.Name} ({oldPeriod.Room?.Name}) => {newPeriod.Subject?.Name} ({newPeriod.Room?.Name})");
            timetableNotificationService.FirePermanentClassPeriodChanged(newPeriod, oldPeriod);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodDropped(PermanentPeriod _period) {
            PermanentClassPeriod period = new(_period);
            logger.Log(LogLevel.Information, $"Class Dropping {period.OddOrEvenWeek}:{period.DayOfWeek}:{period.PeriodIndex} {period.Class!.Name} - {period.Subject?.Name} ({period.Room?.Name})");
            timetableNotificationService.FirePermanentClassPeriodDropped(period);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodNew(PermanentPeriod _period) {
            PermanentClassPeriod period = new(_period);
            logger.LogInformation($"Class New {period.OddOrEvenWeek}:{period.DayOfWeek}:{period.PeriodIndex} {period.Class!.Name}:{period.Group?.Name} - {period.Subject?.Name} ({period.Room?.Name})");
            timetableNotificationService.FirePermanentClassPeriodAdded(period);
            return Task.CompletedTask;
        }

        protected override Task<Class> GetClassByName(string? className) {
            if (className != null)
                throw new ArgumentException($"expected className=null, got className={className}");
            return Task.FromResult(Class);
        }

        protected override IEnumerable<(string?, string?)> GetClassNameAndGroupName(BakaTimetableParser.PeriodInfo pper) {
            var ret = base.GetClassNameAndGroupName(pper);
            if (!ret.Any())
                ret = ret.Append((null, null));
            return ret;
        }
    }
}
