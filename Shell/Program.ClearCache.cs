﻿using System.CommandLine;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task ApplyClearCacheAsync(ClearCache command)
    {
        (IEnumerable<string> cachesDeleted, IEnumerable<string> indexesDeleted) = await CacheClearer.Process(command);

        if (command.Mode != ClearCache.Modes.summary)
        {
            Console.WriteLine(cachesDeleted.Join(" "));
            Console.WriteLine();
            Console.WriteLine(indexesDeleted.Join(" "));
            Console.WriteLine();
        }

        var be = command.Mode == ClearCache.Modes.simulate ? "would be" : "were";
        Console.WriteLine($"{cachesDeleted.Count()} info caches and {indexesDeleted.Count()} full-text indexes {be} deleted.");
    }

    static partial class CommandHandler
    {
        private static Command ConfigureClearCache(Func<ClearCache, Task> handle)
        {
            const string scopeName = "scope", idsName = "ids";

            Command clearCache = new(clearCacheCommand, "Deletes cached metadata and full-text indexes for "
                + $"{nameof(ClearCache.Scopes.channels)}, {nameof(ClearCache.Scopes.playlists)} and {nameof(ClearCache.Scopes.videos)}.");

            clearCache.AddAlias("clear");

            Argument<ClearCache.Scopes> scope = new(scopeName,
                "The type of caches to delete."
                + $" For {nameof(ClearCache.Scopes.playlists)} and {nameof(ClearCache.Scopes.channels)}"
                + $" this will include the associated {nameof(ClearCache.Scopes.videos)}.");

            clearCache.AddArgument(scope);

            //TODO update to require e.g. an explicit "all videos" as scopes for deleting all videos.
            Argument<IEnumerable<string>> ids = new(idsName,
                $"The space-separated IDs or URLs of elements in the '{scope}' to delete caches for."
                + $" Can be used with every '{scope}' but '{nameof(ClearCache.Scopes.all)}'"
                + $" while supporting user names, channel handles and slugs besides IDs for '{nameof(ClearCache.Scopes.channels)}'."
                + $" If not set, all elements in the specified '{scope}' are considered for deletion."
                + quoteIdsStartingWithDash);

            clearCache.AddArgument(ids);

            Option<ushort?> notAccessedForDays = new(new[] { "--last-access", "-l" },
                "The maximum number of days since the last access of a cache file for it to be excluded from deletion."
                + " Effectively only deletes old caches that haven't been accessed for this number of days."
                + $" Ignored for explicitly set '{idsName}'.");

            clearCache.AddOption(notAccessedForDays);

            Option<ClearCache.Modes> mode = new(new[] { "--mode", "-m" }, () => ClearCache.Modes.summary,
                "The deletion mode;"
                + $" '{nameof(ClearCache.Modes.summary)}' only outputs how many of what file type were deleted."
                + $" '{nameof(ClearCache.Modes.verbose)}' outputs the deleted file names as well as the summary."
                + $" '{nameof(ClearCache.Modes.simulate)}' lists all file names that would be deleted by running the command instead of deleting them."
                + " You can use this to preview the files that would be deleted.");

            clearCache.AddOption(mode);

            clearCache.SetHandler(async (scope, ids, notAccessedForDays, mode) => await handle(new ClearCache
            {
                Scope = scope,
                Ids = ids,
                NotAccessedForDays = notAccessedForDays,
                Mode = mode
            }), scope, ids, notAccessedForDays, mode);

            return clearCache;
        }
    }
}