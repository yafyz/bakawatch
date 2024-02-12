using bakawatch.DiscordBot.Workers;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot.Services {
    public class DiscordService(
            DiscordSocketClient discordClient,
            InteractionService interactionService,
            ILogger<DiscordService> logger,
            IServiceScopeFactory serviceScopeFactory
        ) {

        private readonly TaskCompletionSource _IsStartedTCS = new();
        public Task Started { get => _IsStartedTCS.Task; }

        public async Task Start() {
            logger.Log(LogLevel.Information, "Starting discord bot");

            var scope = serviceScopeFactory.CreateAsyncScope();
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), scope.ServiceProvider);

            discordClient.Ready += ClientReady;
            discordClient.InteractionCreated += HandleInteractionCreated;

            await discordClient.LoginAsync(TokenType.Bot, Const.discordBotToken);
            await discordClient.StartAsync();
        }

        private async Task HandleInteractionCreated(SocketInteraction interaction) {
            try {
                var scope = serviceScopeFactory.CreateAsyncScope();
                var context = new SocketInteractionContext(discordClient, interaction);

                _ = Task.Run(async () => {
                    var result = await interactionService.ExecuteCommandAsync(context, scope.ServiceProvider);

                    static async Task reportError(SocketInteraction interaction, string err) {
                        if (interaction.HasResponded) {
                            await interaction.FollowupAsync($"```{err}```");
                        } else {
                            await interaction.RespondAsync($"```{err}```", ephemeral: true);
                        }
                    }

                    if (!result.IsSuccess) {
                        if (result is ExecuteResult e) {
                            logger.LogError(e.Exception, "exception while handling interaction");
                            await reportError(interaction, e.Exception.ToString());
                        } else {
                            logger.Log(LogLevel.Warning, $"non success result while handling interaction\n{result.ErrorReason}");
                            await reportError(interaction, result.ErrorReason);
                        }
                    }
                });
            } catch (Exception e) {
                if (interaction.Type == InteractionType.ApplicationCommand) {
                    var res = await interaction.GetOriginalResponseAsync();
                    await res.DeleteAsync();
                }
                logger.LogError(e, "Exception during command execution");
            }
        }

        private async Task ClientReady() {
            logger.Log(LogLevel.Information, "Registering slash commands");
#if DEBUG
            ulong test_guild = 842850942184718336;
            await interactionService.RegisterCommandsToGuildAsync(test_guild);
#else
            ulong test_guild = 750797183716163635;
            //await interactionService.RegisterCommandsGloballyAsync();
            await interactionService.RegisterCommandsToGuildAsync(test_guild);
#endif
            logger.Log(LogLevel.Information, "Discord bot started");

            _IsStartedTCS.SetResult();
        }
    }
}
