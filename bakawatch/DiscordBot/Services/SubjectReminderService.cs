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
        TimetableService timetableService,
        SyncOptimizationService syncOptimizationService
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
            await UpdateReminder(reminder, true);
            await UpdateReminderMessage(reminder, true);
            await syncOptimizationService.Add(@class.BakaId);
        }

        private IQueryable<IGrouping<TimetableDay, LivePeriod>> GetPeriodQueryForReminder(SubjectReminder reminder)
            => timetableService.GetClassPeriods(bakaContext, reminder.ClassBakaId, reminder.GroupName)
                .Where(x => x.Day.Date >= reminder.Date)
                .Where(x => x.Subject != null
                         && x.Subject.ShortName == reminder.SubjectShortName)
                .OrderBy(x => x.Day.Date)
                .GroupBy(x => x.Day);

        public async Task<ClassPeriod?> GetReminderPeriod(SubjectReminder reminder) {
            var p = await GetPeriodQueryForReminder(reminder)
                .Skip(reminder.ToSkipCount)
                .Select(x => x.First())
                .FirstOrDefaultAsync();

            return p != null ? new(p) : null;
        }

        public async Task<(PermanentClassPeriod, DateOnly)?> GetReminderPermanentPeriod(SubjectReminder reminder) {
            var query = GetPeriodQueryForReminder(reminder)
                .Select(x => x.First());
            var liveCount = await query.CountAsync();

            var skipsRemaining = reminder.ToSkipCount - liveCount;

            if (skipsRemaining < 0)
                throw new ArgumentException("reminder can finish from live periods");

            var ptm = await timetableService.GetPermanentClassTimetable(
                bakaContext,
                await bakaContext.Classes.Where(x => x.BakaId.Value == reminder.ClassBakaId.Value).SingleAsync()
            );

            var doesEvenExist = ptm.Periods
                    .Where(x => x.Subject?.ShortName == reminder.SubjectShortName)
                    .Where(x => x.Groups.Any(x => x.Name == (reminder.GroupName ?? ClassGroup.DefaultGroupName)));

            if (!doesEvenExist.Any())
                return null;

            var lastPeriod = await timetableService
                .GetClassPeriods(bakaContext, reminder.ClassBakaId, reminder.GroupName)
                .OrderBy(x => x.Day.Date)
                .LastAsync();
            var nextDate = timetableService.NextWeekStart(lastPeriod?.Day.Date ?? reminder.Date);

            PermanentPeriod period = null!;

            for(; skipsRemaining >= 0; nextDate = nextDate.AddDays(1)) {
                var oddness = ptm.Periods.First().OddOrEvenWeek switch {
                    OddEven.None => OddEven.None,
                    _ => timetableService.GetWeekOddness(nextDate)
                };

                var p = ptm.Periods
                    .Where(x => x.OddOrEvenWeek == oddness)
                    .Where(x => x.DayOfWeek == nextDate.DayOfWeek)
                    .Where(x => x.Subject?.ShortName == reminder.SubjectShortName)
                    .Where(x => x.Groups.Any(x => x.Name == (reminder.GroupName ?? ClassGroup.DefaultGroupName)));

                if (p.Any()) {
                    skipsRemaining -= 1;
                    period = p.First();
                }
            }

            nextDate = nextDate.AddDays(-1);

            return (new(period), nextDate);
        }

        public async Task UpdateReminderMessage(SubjectReminder reminder, bool firstUpdate = false) {
            string dateString = reminder.LatestPeriodIndex switch {
                -1 => "exact date not currently known",
                _ => $"{reminder.LatestPeriodIndex}. | {reminder.LatestDate}"
            };

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

            var periods = timetableService.GetClassPeriods(bakaContext, reminder.ClassBakaId, reminder.GroupName)
                .Where(x => x.Day.Date >= reminder.Date // start at date
                         && x.Day.Date < dateonlyNow) // add a skip at the end of the day
                .Where(x => x.Subject != null && x.Subject.ShortName == reminder.SubjectShortName)
                .GroupBy(x => x.Day); // only skip once per day

            DateOnly periodDate = DateOnly.MaxValue;
            int periodIndex = -1;
            bool periodFound = false;

            var period = await GetReminderPeriod(reminder);
            if (period == null) {
                var permPeriodData = await GetReminderPermanentPeriod(reminder);
                if (permPeriodData.HasValue) {
                    (var permPeriod, periodDate) = permPeriodData.Value;
                    periodIndex = permPeriod.PeriodIndex;
                    periodFound = true;
                }
            } else {
                periodDate = period.Day.Date;
                periodIndex = period.PeriodIndex;
                periodFound = true;
            }

            if (periodFound && (reminder.LatestDate != periodDate || reminder.LatestPeriodIndex != periodIndex)) {
                reminder.LatestDate = periodDate;
                reminder.LatestPeriodIndex = periodIndex;
                reminder.MessageUpdatePending = true;
            } else if (!periodFound && (reminder.LatestDate != DateOnly.MaxValue || reminder.LatestPeriodIndex != -1)) {
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
