using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using bakawatch.DiscordBot.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace bakawatch.DiscordBot.Workers {
    internal class DiscordPeriodNotificationWorker(
            DiscordService discordService,
            IServiceScopeFactory serviceScopeFactory,
            TimetableNotificationService timetableNotificationService,
            DiscordSocketClient discordClient,
            ILogger<DiscordPeriodNotificationWorker> logger
        )
            : BackgroundService
        {

        List<(ITextChannel, string)> messageBuffer;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await discordService.Started;

            messageBuffer = new();
            timetableNotificationService.OnClassPeriodChanged += OnClassPeriodChanged;
            timetableNotificationService.OnClassPeriodDropped += OnClassPeriodDropped;
            
            logger.Log(LogLevel.Information, $"{nameof(DiscordPeriodNotificationWorker)} started");

            while (!stoppingToken.IsCancellationRequested) {
                await MessageWriter();
                await Task.Delay(2_000, stoppingToken);
            }

            logger.Log(LogLevel.Information, $"{nameof(DiscordPeriodNotificationWorker)} stopped");
        }

        private async Task MessageWriter() {
            if (messageBuffer.Count < 1)
                return;

            var buffer = messageBuffer;
            messageBuffer = new();
            var groups = buffer.GroupBy(x => x.Item1);

            try {
                foreach (var group in groups) {
                    List<string> messages = new(1);
                    string currMsg = "";
                    void flush() { messages.Add(currMsg); currMsg = ""; }

                    foreach ((_, var msg) in group) {
                        if (currMsg.Length + msg.Length > 2000) {
                            flush();
                        }
                        currMsg += msg + "\n";
                    }

                    flush();

                    foreach (var msg in messages) {
                        await group.Key.SendMessageAsync(msg);
                    }
                }
            } catch (Discord.Net.HttpException ex) {
                // discord api is down probably
                logger.LogError(ex, "Error in writing messages");
                // push messages back to retry
                messageBuffer.AddRange(buffer);
            }
        }

        private void OnClassPeriodChanged(ClassPeriod currentPeriod, ClassPeriod oldPeriod) {
            Task.Run(async () => {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                var periodNotifService = scope.ServiceProvider.GetRequiredService<DiscordPeriodNotificationService>();
                var channelService = scope.ServiceProvider.GetRequiredService<DiscordLocalService>();
                
                var group = currentPeriod.Group ?? oldPeriod.Group;

                await foreach (var channelNotif in periodNotifService.GetSubscriptionsFor(oldPeriod.Class.BakaId, group?.Name)) {
                    var channel = (ITextChannel)channelNotif.Channel.Resolve(discordClient);

                    string? grouptext = group != null ? $":{group.Name}" : null;

                    var msg = $"{currentPeriod.Day.Date} | {currentPeriod.PeriodIndex}. | {currentPeriod.Class.Name}{grouptext} | {FormatPeriod(oldPeriod)} => {FormatPeriod(currentPeriod)}";
                    messageBuffer.Add((channel, msg));
                }
            });
        }

        private void OnClassPeriodDropped(ClassPeriod period) {
            Task.Run(async () => {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                var periodNotifService = scope.ServiceProvider.GetRequiredService<DiscordPeriodNotificationService>();
                var channelService = scope.ServiceProvider.GetRequiredService<DiscordLocalService>();

                await foreach (var channelNotif in periodNotifService.GetSubscriptionsFor(period.Class.BakaId, period.Group?.Name)) {
                    var channel = (ITextChannel)channelNotif.Channel.Resolve(discordClient);

                    string? grouptext = period.Group != null ? $":{period.Group.Name}" : null;

                    var msg = $"{period.Day.Date} | {period.PeriodIndex}. | {period.Class.Name}{grouptext} | {FormatPeriod(period)} => Dropped";
                    messageBuffer.Add((channel, msg));
                }
            });
        }

        private string FormatPeriod(LivePeriodBase period)
            => period.Type switch {
                PeriodType.Normal => $"{period.Subject?.ShortName} ({period.Teacher?.FullName}) {(period.ChangeInfo != null ? $"({period.ChangeInfo})" : null)}",
                PeriodType.Removed => $"Removed ({period.RemovedInfo})",
                PeriodType.Absent => $"Absent ({period.AbsenceInfoShort}, {period.AbsenceInfoReason})",
                _ => throw new InvalidOperationException()
            };
    }
}
