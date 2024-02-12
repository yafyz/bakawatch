using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using bakawatch.DiscordBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Workers
{
    internal class SubjectReminderWorker(
        DiscordService discordService,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<SubjectReminderWorker> logger
    )
        : BackgroundService
    {

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await discordService.Started;

            logger.Log(LogLevel.Information, $"{nameof(SubjectReminderWorker)} started");
            
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await UpdateReminders();
                } catch (Exception ex) {
                    logger.LogError(ex, $"Exception in {nameof(SubjectReminderWorker)}");
                }
                await Task.Delay(60_000, stoppingToken);
            }

            logger.Log(LogLevel.Information, $"{nameof(SubjectReminderWorker)} stopped");
        }

        private async Task UpdateReminders() {
            using var scope = serviceScopeFactory.CreateAsyncScope();
            using var discordContext = scope.ServiceProvider.GetRequiredService<DiscordContext>();
            var subjectReminderService = scope.ServiceProvider.GetRequiredService<SubjectReminderService>();

            var query = discordContext.SubjectReminders
                .Where(x => !x.Finished)
                .Include(x => x.Message)
                .Include(x => x.Message.Channel)
                .AsAsyncEnumerable();
            
            await foreach (var reminder in query) {
                await subjectReminderService.UpdateReminder(reminder);
            }
        }
    }
}
