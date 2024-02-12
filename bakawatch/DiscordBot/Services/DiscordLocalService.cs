using bakawatch.DiscordBot.Entities;
using Discord;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Services {
    public class DiscordLocalService(DiscordContext discordContext) {
        public async Task<LocalDChannel?> GetChannel(ulong channelId) {
            return await discordContext.DiscordChannels.FirstOrDefaultAsync(x => x.ChannelSnowflake ==  channelId);
        }

        public async Task<LocalDChannel> GetChannel(ulong channelId, ulong guildId) {
            var channel = await GetChannel(channelId);
            
            if (channel == null) {
                channel = new LocalDChannel() {
                    ChannelSnowflake = channelId,
                    GuildSnowflake = guildId
                };
                discordContext.DiscordChannels.Add(channel);
                await discordContext.SaveChangesAsync();
            }

            return channel;
        }

        public async Task<LocalDChannel> GetChannel(IGuildChannel channel)
            => await GetChannel(channel.Id, channel.GuildId);

        public async Task<LocalDMessage> GetMessage(IMessage message) {
            var msg = await discordContext.DiscordMessages
                .Include(x => x.Channel)
                .FirstOrDefaultAsync(x => x.ID == message.Id);

            if (msg == null) {
                msg = new LocalDMessage() {
                    ID = message.Id,
                    Channel = await GetChannel((IGuildChannel)message.Channel)
                };
                discordContext.DiscordMessages.Add(msg);
                await discordContext.SaveChangesAsync();
            }

            return msg;
        }
    }
}
