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
using bakawatch.BakaSync;
using Microsoft.EntityFrameworkCore;

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
            timetableNotificationService.OnClassPeriodAdded += OnClassPeriodAdded;

            timetableNotificationService.OnTeacherPeriodChanged += OnTeacherPeriodChanged;
            
            logger.Log(LogLevel.Information, $"{nameof(DiscordPeriodNotificationWorker)} started");

            while (!stoppingToken.IsCancellationRequested) {
                await MessageWriter();
                await Task.Delay(2_000, stoppingToken);
            }

            logger.Log(LogLevel.Information, $"{nameof(DiscordPeriodNotificationWorker)} stopped");
        }

        private void OnClassPeriodAdded(ClassPeriod period) {
            // this code is peak
            Task.Run(async () => {
                // delay cuz i gotta refetch period but it wont be saved yet yeeeeeeeeeeeeee
                await Task.Delay(1000);
                try {
                    using var scope = serviceScopeFactory.CreateAsyncScope();
                    var periodNotifService = scope.ServiceProvider.GetRequiredService<DiscordPeriodNotificationService>();
                    var channelService = scope.ServiceProvider.GetRequiredService<DiscordLocalService>();
                    using var db = scope.ServiceProvider.GetRequiredService<BakaContext>();
                    var tms = scope.ServiceProvider.GetRequiredService<TimetableService>();

                    var subscriptions = periodNotifService.GetSubscriptionsFor(period.Group);
                    if (!await subscriptions.AnyAsync())
                        return;

                    var p = period.Period;
                    var tPeriod = await db.LivePeriods
                        .Include(x => x.Subject)
                        .Include(x => x.Room)
                        .Include(x => x.Day)
                        .Include(x => x.Day.Week)
                        .Include(x => x.Groups)
                        .ThenInclude(x => x.Class)
                        .Where(x => x.ID == p.ID)
                        .SingleAsync();
                    // i... dont know... anymore...
                    await db.Entry(tPeriod).Reference(x => x.Teacher).LoadAsync();
                    period = new(tPeriod);
                    var permPeriod = await tms.GetPermanentClassPeriod(db, period.Class, period.Day.Date.DayOfWeek, period.PeriodIndex, period.Day.Week.OddEven);

                    string text;
                    string? grouptext = period.Group.Name != ClassGroup.DefaultGroupName ? $":{period.Group.Name}" : null;
                    if (permPeriod == null) {
                        text = $"{period.Day.Date} | {period.PeriodIndex}. | {period.Class.Name}{grouptext} | Added {FormatPeriod(period.Period)}";
                    } else if (!permPeriod.CompareWithLive(period.Period)) {
                        text = $"{period.Day.Date} | {period.PeriodIndex}. | {period.Class.Name}{grouptext} | {FormatPeriod(permPeriod)} => {FormatPeriod(period.Period)}";
                    } else {
                        return;
                    }

                    await foreach (var subscription in subscriptions) {
                        var chan = (ITextChannel)subscription.Channel.Resolve(discordClient);
                        messageBuffer.Add((chan, text));
                    }
                } catch (Exception ex) {
                    logger.LogError(ex, $"Exception in {nameof(OnClassPeriodAdded)}");
                }
            });
        }

        private void OnTeacherPeriodChanged(TeacherPeriod currentPeriod, TeacherPeriod oldPeriod) {
            if (currentPeriod.HasAbsent && !oldPeriod.HasAbsent && currentPeriod.Groups.Count != 0) {
                Task.Run(async () => {
                    using var scope = serviceScopeFactory.CreateAsyncScope();
                    var periodNotifService = scope.ServiceProvider.GetRequiredService<DiscordPeriodNotificationService>();
                    var channelService = scope.ServiceProvider.GetRequiredService<DiscordLocalService>();
                    
                    foreach (var group in currentPeriod.Groups) {
                        await foreach(var sub in periodNotifService.GetSubscriptionsFor(group)) {
                            string? grouptext = group.Name != ClassGroup.DefaultGroupName ? $":{group.Name}" : null;
                            var msg = $"{currentPeriod.Day.Date} | {currentPeriod.PeriodIndex}. | {group.Class.Name}{grouptext} | Absent collision {FormatPeriod(currentPeriod.Period)}";
                            messageBuffer.Add(((ITextChannel)sub.Channel.Resolve(discordClient), msg));
                        }
                    }
                });
            }
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
                
                var group = currentPeriod.Group;

                await foreach (var channelNotif in periodNotifService.GetSubscriptionsFor(oldPeriod.Class.BakaId, group.Name)) {
                    var channel = (ITextChannel)channelNotif.Channel.Resolve(discordClient);

                    string? grouptext = group.Name != ClassGroup.DefaultGroupName ? $":{group.Name}" : null;

                    var msg = $"{currentPeriod.Day.Date} | {currentPeriod.PeriodIndex}. | {currentPeriod.Class.Name}{grouptext} | {FormatPeriod(oldPeriod.Period)} => {FormatPeriod(currentPeriod.Period)}";
                    messageBuffer.Add((channel, msg));
                }
            });
        }

        private void OnClassPeriodDropped(ClassPeriod period) {
            Task.Run(async () => {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                var periodNotifService = scope.ServiceProvider.GetRequiredService<DiscordPeriodNotificationService>();
                var channelService = scope.ServiceProvider.GetRequiredService<DiscordLocalService>();

                await foreach (var channelNotif in periodNotifService.GetSubscriptionsFor(period.Class.BakaId, period.Group.Name)) {
                    var channel = (ITextChannel)channelNotif.Channel.Resolve(discordClient);

                    string? grouptext = period.Group.Name != ClassGroup.DefaultGroupName ? $":{period.Group.Name}" : null;

                    var msg = $"{period.Day.Date} | {period.PeriodIndex}. | {period.Class.Name}{grouptext} | {FormatPeriod(period.Period)} => Dropped";
                    messageBuffer.Add((channel, msg));
                }
            });
        }

        private string FormatPeriod(LivePeriod period)
            => period.Type switch {
                PeriodType.Normal => $"{period.Subject?.ShortName} ({period.Teacher?.FullName}) {(period.ChangeInfo != null ? $"({period.ChangeInfo})" : null)}",
                PeriodType.Removed => $"Removed ({period.RemovedInfo})",
                PeriodType.Absent => $"Absent ({period.AbsenceInfoShort}, {period.AbsenceInfoReason})",
                _ => throw new InvalidOperationException()
            };

        private string FormatPeriod(PermanentPeriod period)
            => $"{period.Subject?.ShortName} ({period.Teacher?.FullName})";
    }
}
