using bakawatch.DiscordBot.Modules;
using bakawatch.DiscordBot.Services;
using bakawatch.DiscordBot.Workers;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot {
    internal class ServiceRegistrator {
        public static void RegisterServices(IServiceCollection services) {
            services.AddDbContext<DiscordContext>();

            services.AddSingleton(x =>new DiscordSocketConfig() {
                LogLevel = Discord.LogSeverity.Debug
            });
            services.AddSingleton(x => new InteractionServiceConfig() {
                ThrowOnError = true,
                DefaultRunMode = RunMode.Sync,
                LogLevel = Discord.LogSeverity.Debug
            });

            services.AddSingleton<DiscordSocketClient>(s => new DiscordSocketClient(s.GetRequiredService<DiscordSocketConfig>()));
            services.AddSingleton<InteractionService>(s => new InteractionService(s.GetRequiredService<DiscordSocketClient>(),
                                                                                  s.GetRequiredService<InteractionServiceConfig>()));
            services.AddSingleton<DiscordService>();
            services.AddHostedService<BotStartWorker>();

            services.AddScoped<DiscordPeriodNotificationService>();
            services.AddHostedService<DiscordPeriodNotificationWorker>();
            
            services.AddScoped<DiscordLocalService>();
         
            services.AddScoped<SubjectReminderService>();
            services.AddHostedService<SubjectReminderWorker>();
        }
    }
}
