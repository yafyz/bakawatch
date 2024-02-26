using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    internal abstract class SyncWorkerBase : BackgroundService
    {
        // range from before and after midnight,
        // so its actually double the amount
        protected double MidnightPauseRange { get; set; } = 10 * 60 * 1000;

        protected int BreakLength { get; set; } = 10 * 1000;

        protected async Task<bool> WeekEdgeWait(CancellationToken ct) {
            // prevent date desync while parsing timetables
            // when on the edge of a week by pausing for a while

            var now = DateTime.Now;

            if (now.DayOfWeek == DayOfWeek.Sunday) {
                var midnight = DateOnly.FromDateTime(now.AddDays(1)).ToDateTime(TimeOnly.MinValue);
                var diff = midnight - now;

                if (Math.Abs(diff.TotalMilliseconds) < MidnightPauseRange) {
                    if (diff.TotalMilliseconds > 0) {
                        // before midnight,
                        // wait for after midnight + MidnightPauseRange
                        await Task.Delay((int)(diff.TotalMilliseconds + MidnightPauseRange), ct);
                    } else {
                        await Task.Delay((int)diff.TotalMilliseconds, ct);
                    }

                    return true;
                }
            }

            return false;
        }

        protected async Task TakeBreak(CancellationToken ct) {
            await Task.Delay(BreakLength, ct);
        }
    }
}
