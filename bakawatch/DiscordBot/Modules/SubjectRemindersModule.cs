using bakawatch.BakaSync.Entities;
using bakawatch.BakaSync;
using Discord.Commands;
using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using bakawatch.DiscordBot.Services;
using Discord;

using static bakawatch.DiscordBot.InteractionsHelper;
using GroupAttribute = Discord.Interactions.GroupAttribute;
using Discord.WebSocket;

namespace bakawatch.DiscordBot.Modules
{
    [Group("reminder", "period reminders")]
    public class SubjectRemindersModule(BakaContext bakaContext, SubjectReminderService subjectReminderService) : InteractionModuleBase<SocketInteractionContext>
    {
        public const string POSTPONE_ID = "reminder_postpone";
        public const string SOONER_ID = "reminder_sooner";
        public const string DELETE_ID = "reminder_delete";

        [SlashCommand("add", "add a reminder")]
        public async Task Add(string className, string subjectShortName, string description, string? _date = null, int skipCount = 0, string? groupName = null) {
            className = className.Replace("`", "\\`");
            groupName = groupName?.Replace("`", "\\`");

            DateOnly date;
            if (_date == null) {
                date = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
            } else {
                date = DateOnly.Parse(_date);
            }

            Class @class;
            ClassGroup? group;
            Subject subject;
            try {
                (@class, group) = await GetClassAndGroup(bakaContext, className, groupName);
                subject = await GetSubject(bakaContext, subjectShortName);
            } catch (InteractionError e) {
                await RespondAsync(e.Message);
                return;
            }

            await RespondAsync("Creating...");

            var msg = await GetOriginalResponseAsync();
            await subjectReminderService.AddReminder(@class, group, subject, description, date, skipCount, msg);
        }

        public static MessageComponent BuildComponents(Guid guid) {
            return new ComponentBuilder()
                .WithButton("Postpone", POSTPONE_ID + guid.ToString())
                .WithButton("Sooner", SOONER_ID + guid.ToString())
                .WithButton("Delete", DELETE_ID + guid.ToString(), ButtonStyle.Danger)
                .Build();
        }

        [ComponentInteraction($"{POSTPONE_ID}*", ignoreGroupNames: true)]
        public async Task ReminderPostpone(string reminderGuid) {
            var reminder = await subjectReminderService.GetReminderByGuid(Guid.Parse(reminderGuid));
            if (reminder == null) return;

            await ((SocketMessageComponent)Context.Interaction).DeferAsync();
            await subjectReminderService.AddSkips(reminder, 1);
        }

        [ComponentInteraction($"{SOONER_ID}*", ignoreGroupNames: true)]
        public async Task ReminderSooner(string reminderGuid) {
            var reminder = await subjectReminderService.GetReminderByGuid(Guid.Parse(reminderGuid));
            if (reminder == null) return;


            await ((SocketMessageComponent)Context.Interaction).DeferAsync();
            await subjectReminderService.AddSkips(reminder, -1);
        }

        [ComponentInteraction($"{DELETE_ID}*", ignoreGroupNames: true)]
        public async Task ReminderDelete(string reminderGuid) {
            var reminder = await subjectReminderService.GetReminderByGuid(Guid.Parse(reminderGuid));
            if (reminder == null) return;

            await ((SocketMessageComponent)Context.Interaction).DeferAsync();
            await subjectReminderService.DeleteReminder(reminder);
        }
    }
}
