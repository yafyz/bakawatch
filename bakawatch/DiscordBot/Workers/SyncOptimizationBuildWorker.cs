using bakawatch.BakaSync;
using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Workers
{
    internal class SyncOptimizationBuildWorker(SyncOptimizationService syncOptimizationService, IServiceScopeFactory serviceScopeFactory) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            syncOptimizationService.OnOptimizationBuilding += GoOverReminders;
            syncOptimizationService.OnOptimizationBuilding += GoOverPeriodNotifications;
            return Task.CompletedTask;
        }

        private async Task GoOverReminders(IServiceProvider services) {
            var discordContext = services.GetRequiredService<DiscordContext>();

            var list = await discordContext.SubjectReminders
                .GroupBy(x => x.ClassBakaId.Value)
                .Select(x => x.Key)
                .ToListAsync();

            await syncOptimizationService.AddRange(list.Select(x => new ClassBakaId(x)));
        }

        private async Task GoOverPeriodNotifications(IServiceProvider services) {
            var discordContext = services.GetRequiredService<DiscordContext>();
            var bakaContext = services.GetRequiredService<BakaContext>();

            var classesList = await discordContext.PeriodChangeNotifications
                .GroupBy(x => x.ClassBakaId.Value)
                .Select(x => x.Key)
                .ToListAsync();

            await syncOptimizationService.AddRange(classesList.Select(x => new ClassBakaId(x)));
        }
    }
}
