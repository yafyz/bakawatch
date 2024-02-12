using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync.Services
{
    internal class IdSyncService
    {
        public readonly TaskCompletionSource _IsInitializedTCS = new();
        public Task IsInitialized { get => _IsInitializedTCS.Task; }
    }
}
