﻿using bakawatch.BakaSync;
using bakawatch.BakaSync.Entities;
using bakawatch.DiscordBot.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Services {
    public class DiscordPeriodNotificationService(DiscordContext discordContext, DiscordLocalService discordChannelService) {
        public async Task<bool> Subscribe(Discord.ITextChannel channel, ClassBakaId classId, string? groupName) {
            var exists = await discordContext.PeriodChangeNotifications
                .AnyAsync(x => x.ClassBakaId == classId && x.GroupName == (groupName ?? ClassGroup.DefaultGroupName));
            if (exists)
                return false;

            discordContext.PeriodChangeNotifications.Add(new PeriodChangeNotification {
                Channel = await discordChannelService.GetChannel(channel.Id, channel.GuildId),
                ClassBakaId = classId,
                GroupName = groupName ?? ClassGroup.DefaultGroupName
            });

            await discordContext.SaveChangesAsync();

            return true;
        }

        public async Task<bool> Unsubscribe(Discord.ITextChannel channel, ClassBakaId classId, string? groupName) {
            var n = await discordContext.PeriodChangeNotifications
                .Where(x => x.Channel.ChannelSnowflake == channel.Id)
                .FirstOrDefaultAsync(x => x.ClassBakaId == classId && x.GroupName == (groupName ?? ClassGroup.DefaultGroupName));
            if (n == null)
                return false;

            discordContext.PeriodChangeNotifications.Remove(n);
            await discordContext.SaveChangesAsync();
            return true;
        }

        public IAsyncEnumerable<PeriodChangeNotification>GetSubscriptionsFor(ClassBakaId classId, string? groupName) {
            return discordContext.PeriodChangeNotifications
                .Where(x => x.ClassBakaId == classId && x.GroupName == (groupName ?? ClassGroup.DefaultGroupName))
                .Include(x => x.Channel)
                .AsAsyncEnumerable();
        }
    }
}
