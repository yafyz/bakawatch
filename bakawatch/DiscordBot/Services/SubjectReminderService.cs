using bakawatch.BakaSync;
using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync.Services;
using bakawatch.DiscordBot.Entities;
using bakawatch.DiscordBot.Modules;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Services
{
    public class SubjectReminderService(
        DiscordContext discordContext,
        BakaContext bakaContext,
        DiscordSocketClient discordClient,
        DiscordLocalService discordLocalService,
        TimetableService timetableService
    ) {
        public async Task AddReminder(
            Class @class,
            ClassGroup? group,
            Subject subject,
            string description,
            DateOnly date,
            int skipCount,
            IUserMessage message
        ) {
            var reminder = new SubjectReminder {
                Guid = Guid.NewGuid(),

                ClassBakaId = @class.BakaId,
                GroupName = group?.Name,

                SubjectShortName = subject.ShortName,
                Description = description,

                Date = date,
                ToSkipCount = skipCount,
                Message = await discordLocalService.GetMessage(message),
            };

            discordContext.SubjectReminders.Add(reminder);
            await UpdateReminderMessage(reminder, true);
        }

        public async Task<Period?> GetReminderPeriod(SubjectReminder reminder) {
            var p = await timetableService.GetPeriods(bakaContext, reminder.ClassBakaId, reminder.GroupName)
                .Where(x => x.Day.Date >= reminder.Date)
                .Where(x => x.Subject != null
                         && x.Subject.ShortName == reminder.SubjectShortName)
                .OrderBy(x => x.Day.Date)
                .GroupBy(x => x.Day)
                .Skip(reminder.ToSkipCount)
                .Select(x => x.First())
                .FirstOrDefaultAsync();

            return p;
        }

        public async Task UpdateReminderMessage(SubjectReminder reminder, bool firstUpdate = false) {
            var period = await GetReminderPeriod(reminder);
            var dateString = period != null ? $"{period.PeriodIndex}. | {period.Day.Date}" : $"exact date not currently known";

            var channel = (ITextChannel)reminder.Message.Channel.Resolve(discordClient);

            var @class = await bakaContext.Classes.FirstOrDefaultAsync(x => x.BakaId == reminder.ClassBakaId);
            var classString = reminder.GroupName != null ? $"{@class!.Name}:{reminder.GroupName}" : @class!.Name;

            var embed = new EmbedBuilder()
                .WithTitle($"Reminder {classString} {reminder.SubjectShortName}")
                .WithDescription(reminder.Description)
                .AddField(new EmbedFieldBuilder()
                    .WithName("Due date")
                    .WithValue(dateString)
                )
                .WithFooter(new EmbedFooterBuilder()
                    .WithText($"{reminder.ToSkipCount+1}{((reminder.ToSkipCount+1) % 10) switch {
                        1 => "st",
                        2 => "nd",
                        3 => "rd",
                        _ => "th"
                    }} {reminder.SubjectShortName} after {reminder.Date.AddDays(-1)}")
                )
                .WithColor(reminder.Finished ? Color.Red : Color.Green)
                .Build();

            await channel.ModifyMessageAsync(reminder.Message.ID, (x) => {
                x.Embeds = new([embed]);
                if (firstUpdate) {
                    x.Content = "";
                    x.Components = SubjectRemindersModule.BuildComponents(reminder.Guid);
                }
            });

            reminder.MessageUpdatePending = false;
            await discordContext.SaveChangesAsync();
        }

        public async Task UpdateReminder(SubjectReminder reminder) {
            var dateonlyTimestamp = DateOnly.FromDateTime(reminder.Timestamp);
            var now = DateTime.Now;
            var dateonlyNow = DateOnly.FromDateTime(now);

            var periods = timetableService.GetPeriods(bakaContext, reminder.ClassBakaId, reminder.GroupName)
                .Where(x => x.Day.Date >= reminder.Date // start at date
                         && x.Day.Date < dateonlyNow) // add a skip at the end of the day
                .Where(x => x.Subject != null && x.Subject.ShortName == reminder.SubjectShortName)
                .GroupBy(x => x.Day); // only skip once per day

            var period = await GetReminderPeriod(reminder);

            if (period != null && (reminder.LatestDate != period.Day.Date || reminder.LatestPeriodIndex != period.PeriodIndex)) {
                reminder.LatestDate = period.Day.Date;
                reminder.LatestPeriodIndex = period.PeriodIndex;
                reminder.MessageUpdatePending = true;
            } else if (period == null && (reminder.LatestDate != DateOnly.MaxValue || reminder.LatestPeriodIndex != -1)) {
                reminder.LatestDate = DateOnly.MaxValue;
                reminder.LatestPeriodIndex = -1;
                reminder.MessageUpdatePending = true;
            }

            reminder.SkippedCount = await periods.CountAsync();
            reminder.Timestamp = now;

            var isFinished = reminder.SkipsRemaining < 0 && reminder.Date < dateonlyNow;
            if (reminder.Finished != isFinished) {
                reminder.Finished = isFinished;
                reminder.MessageUpdatePending = true;
            }

            discordContext.Update(reminder);
            await discordContext.SaveChangesAsync();
        }

        public async Task<SubjectReminder?> GetReminderByGuid(Guid guid) {
            return await discordContext.SubjectReminders
                .Include(x => x.Message)
                .Include(x => x.Message.Channel)
                .FirstOrDefaultAsync(x => x.Guid == guid);
        }

        public async Task AddSkips(SubjectReminder reminder, int offset) {
            reminder.ToSkipCount = Math.Max(reminder.ToSkipCount + offset, 0);
            discordContext.Update(reminder);
            await discordContext.SaveChangesAsync();

            await UpdateReminder(reminder);
            if (reminder.MessageUpdatePending)
                await UpdateReminderMessage(reminder);
        }

        public async Task DeleteReminder(SubjectReminder reminder) {
            discordContext.Remove(reminder);
            await discordContext.SaveChangesAsync();
            
            var channel = (ITextChannel)reminder.Message.Channel.Resolve(discordClient);
            await channel.DeleteMessageAsync(reminder.Message.ID);
        }
    }
}
