using bakawatch.BakaSync;
using bakawatch.BakaSync.Entities;
using bakawatch.DiscordBot.Services;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static bakawatch.DiscordBot.InteractionsHelper;

namespace bakawatch.DiscordBot.Modules {
    [RequireUserPermission(Discord.GuildPermission.ManageChannels)]
    [Group("period_changed_notifications", "period change notifications")]
    public class PeriodChangedNotificationModule(BakaContext bakaContext, DiscordPeriodNotificationService periodNotificationService) : InteractionModuleBase<SocketInteractionContext> {
        
        //todo: add check that channel is from executing server

        [SlashCommand("subscribe", "add a notification for period changes to channel")]
        public async Task Add(
            ITextChannel channel,
            string className,
            string? groupName = null
        ) {
            className = EscapeBackticks(className);
            groupName = EscapeBackticks(groupName);

            Class @class;
            ClassGroup? group;
            try {
                (@class, group) = await GetClassAndGroup(bakaContext, className, groupName);
            } catch (InteractionError e) {
                await RespondAsync(e.Message);
                return;
            }

            var result = await periodNotificationService.Subscribe(channel, @class.BakaId, group?.Name);

            if (result) {
                await RespondAsync($"Channel <#{channel.Id}> subscribed to `{@class.Name}`{(group != null ? $":`{group.Name}`" : "")} period change notifications");
            } else {
                await RespondAsync($"Channel <#{channel.Id}> is already subscribed to `{@class.Name}`{(group != null ? $":`{group.Name}`" : "")} period change notifications");
            }
        }

        [SlashCommand("unsubscribe", "remove a notification for period changes to channel")]
        public async Task Remove(
            ITextChannel channel,
            string className,
            string? groupName = null
        ) {
            className = className.Replace("`", "\\`");
            groupName = groupName?.Replace("`", "\\`");

            var @class = await bakaContext.Classes.FirstOrDefaultAsync(x => x.Name == className);
            if (@class == null) {
                await RespondAsync($"Class `{className}` does not exist");
                return;
            }

            ClassGroup? group = null;
            if (groupName != null
                && (group = await bakaContext.Groups.FirstOrDefaultAsync(x => x.Name == groupName)) == null) {

                await RespondAsync($"Class `{className}` doesnt have a group named `{groupName}`");
                return;
            }

            var result = await periodNotificationService.Unsubscribe(channel, @class.BakaId, group?.Name);

            if (result) {
                await RespondAsync($"Channel <#{channel.Id}> unsubscribed from `{@class.Name}`{(group != null ? $":`{group.Name}`" : "")} period change notifications");
            } else {
                await RespondAsync($"Channel <#{channel.Id}> isnt subscribed to `{@class.Name}`{(group != null ? $":`{group.Name}`" : "")} period change notifications");
            }
        }
    }
}
