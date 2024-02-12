using bakawatch.BakaSync;
using bakawatch.BakaSync.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bakawatch.DiscordBot
{
    internal static class InteractionsHelper
    {
        public static string? EscapeBackticks(string? str)
            => str?.Replace("`", "\\`");

        public static async Task<Class> GetClass(BakaContext bakaContext, string className)
            => await bakaContext.Classes.FirstOrDefaultAsync(x => x.Name == className)
            ?? throw new InteractionError($"Class `{EscapeBackticks(className)}` does not exist");

        public static async Task<ClassGroup?> GetGroup(BakaContext bakaContext, string className, string? groupName) {
            ClassGroup? group = null;
            if (groupName != null
                && (group = await bakaContext.Groups.FirstOrDefaultAsync(x => x.Name == groupName && x.Class.Name == className)) == null) {

                throw new InteractionError($"Class `{EscapeBackticks(className)}` doesnt have a group named `{EscapeBackticks(groupName)}`");
            }
            return group;
        }

        public static async Task<(Class, ClassGroup?)> GetClassAndGroup(BakaContext bakaContext, string className, string? groupName)
            => (await GetClass(bakaContext, className), await GetGroup(bakaContext, className, groupName));

        public static async Task<Subject> GetSubject(BakaContext bakaContext, string subjectShortName)
            => await bakaContext.Subjects.FirstOrDefaultAsync(x => x.ShortName == subjectShortName)
            ?? throw new InteractionError($"Subject `{EscapeBackticks(subjectShortName)}` does not exist");

        public class InteractionError(string error) : Exception(error);
    }
}
