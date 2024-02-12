using bakawatch.BakaSync.Services;
using bakawatch.BakaSync.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    internal class ServiceRegistrator
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddDbContext<BakaContext>();
            services.AddSingleton<BakaAPI>(_ => new BakaAPI("https://spsul.bakalari.cz/", Const.loginDetails));
            services.AddSingleton<BakaTimetableParser>();
            services.AddSingleton<TimetableNotificationService>();

            services.AddSingleton<IdSyncService>();
            services.AddSingleton<TimetableService>();

            services.AddHostedService<IdSyncWorker>();
            //services.AddHostedService<TimetableSyncWorker>();

            services.AddTransient<ClassTimetableSync>();
            services.AddHostedService<ClassTimetableSyncWorker>();
        }
    }
}
