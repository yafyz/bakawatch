using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Services
{
    public class SyncOptimizationService {
        private readonly ILogger<SyncOptimizationService> logger;
        private readonly IServiceScopeFactory serviceScopeFactory;

        private HashSet<ClassBakaId> Classes = new();
        private HashSet<TeacherBakaId> Teachers = new();

        private Task RebuildingTask;

        public bool TeacherClassSyncPending { get; private set; }

        public SyncOptimizationService(ILogger<SyncOptimizationService> logger, IServiceScopeFactory serviceScopeFactory) {
            this.logger = logger;
            this.serviceScopeFactory = serviceScopeFactory;
            
            var initTCS = new TaskCompletionSource();
            RebuildingTask = initTCS.Task;

            Task.Run(async () => {
                await Task.Delay(1000);
                try {
                    await _RebuildOptimizations();
                } catch (Exception e) {
                    logger.LogCritical(e, "exception on initial optimization rebuild");
                }
                initTCS.SetResult();
            });
        }

        public async Task Add(ClassBakaId classBakaId) {
            if (Classes.Contains(classBakaId))
                return;
            logger.LogInformation($"optimization add class={classBakaId.Value}");
            Classes.Add(classBakaId);
            TeacherClassSyncPending = true;
        }

        public async Task AddRange(IEnumerable<ClassBakaId> ids) {
            foreach (var item in ids) {
                await Add(item);
            }
        }

        public async Task Remove(ClassBakaId classBakaId, bool rebuild = true) {
            if (Classes.Remove(classBakaId))
                logger.LogInformation($"optimization remove class={classBakaId.Value}");
            if (rebuild)
                await RebuildOptimizations();
        }

        public async Task<bool> ShouldCheck(ClassBakaId classBakaId) {
            await RebuildingTask;
            return Classes.Contains(classBakaId);
        }

        public async Task Add(TeacherBakaId teacherBakaId) {
            if (Teachers.Contains(teacherBakaId))
                return;
            logger.LogInformation($"optimization add teacher={teacherBakaId.Value}");
            Teachers.Add(teacherBakaId);
        }

        public async Task AddRange(IEnumerable<TeacherBakaId> ids) {
            foreach (var item in ids) {
                await Add(item);
            }
        }

        public async Task Remove(TeacherBakaId teacherBakaId, bool rebuild = true) {
            if (Teachers.Remove(teacherBakaId))
                logger.LogInformation($"optimization remove teacher={teacherBakaId.Value}");
            if (rebuild)
                await RebuildOptimizations();
        }

        public async Task<bool> ShouldCheck(TeacherBakaId teacherBakaId) {
            await RebuildingTask;
            return Teachers.Contains(teacherBakaId);
        }

        public event Func<IServiceProvider, Task>? OnOptimizationBuilding;

        public async Task RebuildOptimizations() {
            await RebuildingTask;
            var tcs = new TaskCompletionSource();
            RebuildingTask = tcs.Task;
            await _RebuildOptimizations();
            tcs.SetResult();
        }

        private async Task _RebuildOptimizations() {
            Classes = new();
            Teachers = new();

            if (OnOptimizationBuilding == null)
                return;

            var scope = serviceScopeFactory.CreateAsyncScope();

            var tasks = OnOptimizationBuilding
                .GetInvocationList()
                .Select(x => (Task)x.DynamicInvoke([scope.ServiceProvider])!);

            await Task.WhenAll(tasks);

            logger.LogInformation($"Rebuild optimizations (classes={Classes.Count}, teachers={Teachers.Count})");
        }

        public async Task OnClassesSynced() {
            if (!TeacherClassSyncPending)
                return;
            TeacherClassSyncPending = false;

            var strd = Classes.Select(x => x.Value);

            var scope = serviceScopeFactory.CreateAsyncScope();
            var bakaContext = scope.ServiceProvider.GetRequiredService<BakaContext>();
            var teacherList = await bakaContext.LivePeriods
                .Where(LivePeriodQuery.IsClassPeriod)
                .Where(x => x.Groups.Any(y => strd.Contains(y.Class.BakaId.Value)))
                .Where(x => x.Teacher != null)
                .GroupBy(x => x.Teacher)
                .Select(x => x.Key!.BakaId.Value)
                .ToListAsync();

            await AddRange(teacherList.Select(x => new TeacherBakaId(x)));
        }
    }
}
