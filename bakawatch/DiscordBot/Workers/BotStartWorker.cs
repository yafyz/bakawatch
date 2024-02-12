using bakawatch.DiscordBot.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Workers {
    internal class BotStartWorker(DiscordService discordService) : BackgroundService {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await discordService.Start();
        }
    }
}
