using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Entities {
    public class LocalDChannel {
        [Key]
        public required ulong ChannelSnowflake { get; set; }
        public required ulong GuildSnowflake { get; set; }

        public Discord.IChannel Resolve(DiscordSocketClient discordClient) {
            return discordClient.Guilds
                .First(x => x.Id == GuildSnowflake)
                .Channels
                .First(x => x.Id == ChannelSnowflake);
        }
    }
}
