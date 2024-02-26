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
    internal class ClassLiveTimetableSync(
        BakaContext _bakaContext,
        TimetableNotificationService timetableNotificationService,
        ILogger<ClassLiveTimetableSync> _logger
    )
        : LivePeriodTimetableSync
    {
        public Class Class { get; set; }
        public override string? Tag => $"class={Class.Name}";

        protected override BakaContext bakaContext => _bakaContext;
        protected override ILogger logger => _logger;

        protected override IQueryable<LivePeriod> PeriodHistory
            => bakaContext.LivePeriodsWithIncludes
                .Where(LivePeriodQuery.IsClassPeriod)
                .Where(LivePeriodQuery.IsHistorical);
        protected override IQueryable<LivePeriod> LivePeriods
            => bakaContext.LivePeriodsWithIncludes
                .Where(LivePeriodQuery.IsClassPeriod)
                .Where(LivePeriodQuery.IsCurrent);
        protected override BakaTimetableParser.Who Who => BakaTimetableParser.Who.Class;

        protected override Task FirePeriodChanged(LivePeriod _newPeriod, LivePeriod _oldPeriod) {
            ClassPeriod newPeriod = new(_newPeriod);
            ClassPeriod oldPeriod = new(_oldPeriod);
            logger.Log(LogLevel.Information, $"Class Update {newPeriod.Day.Date}:{newPeriod.PeriodIndex} {newPeriod.Class!.Name}:{newPeriod.Group?.Name} - {oldPeriod.Subject?.Name} ({oldPeriod.Type}) => {newPeriod.Subject?.Name} ({newPeriod.Type})");
            timetableNotificationService.FireClassPeriodChanged(newPeriod, oldPeriod);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodDropped(LivePeriod _period) {
            ClassPeriod period = new(_period);
            logger.Log(LogLevel.Information, $"Class Dropping {period.Day.Date}:{period.PeriodIndex} {period.Class!.Name} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireClassPeriodDropped(period);
            return Task.CompletedTask;
        }

        protected override Task FirePeriodNew(LivePeriod _period) {
            ClassPeriod period = new(_period);
            logger.LogInformation($"Class New {period.Day.Date}:{period.PeriodIndex} {period.Class!.Name}:{period.Group?.Name} - {period.Subject?.Name} ({period.Type})");
            timetableNotificationService.FireClassPeriodAdded(period);
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
