using System.Security.Cryptography;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;

namespace bakawatch
{
    class Program
    {
        public async static Task Main(string[] args) {
            Console.WriteLine("bruh");

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = new CultureInfo("cs-CZ", false);

            var settings = new HostApplicationBuilderSettings {
                Args = args,
                EnvironmentName = "Development"
            };

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(settings);

            BakaSync.ServiceRegistrator.RegisterServices(builder.Services);
            DiscordBot.ServiceRegistrator.RegisterServices(builder.Services);

            var host = builder.Build();

            using (var scope = host.Services.CreateAsyncScope())
            using (var bakaContext = scope.ServiceProvider.GetRequiredService<BakaSync.BakaContext>())
            using (var discordContext = scope.ServiceProvider.GetRequiredService<DiscordBot.DiscordContext>()) {
            //    await bakaContext.Database.EnsureDeletedAsync();
                await bakaContext.Database.EnsureCreatedAsync();
            //    await discordContext.Database.EnsureDeletedAsync();
                await discordContext.Database.EnsureCreatedAsync();
            }

            await host.RunAsync();
        }
    }
}