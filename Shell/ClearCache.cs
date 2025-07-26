using System.CommandLine;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class Program
{
    internal static async Task ApplyClearCacheAsync(ClearCache command)
    {
        (IEnumerable<string> cachesDeleted, IEnumerable<string> indexesDeleted) =
            await CacheManager.Clear(command, CreateDataStore(), CreateVideoIndexRepo());

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
}

static partial class CommandInterpreter
{
    private static Command ConfigureClearCache(Func<ClearCache, Task> handle)
    {
        const string scopeName = "scope", aliasesName = "aliases";

        Command clearCache = new(clearCacheCommand, "Deletes cached metadata and full-text indexes for "
            + $"`{nameof(ClearCache.Scopes.channels)}`, `{nameof(ClearCache.Scopes.playlists)}` and `{nameof(ClearCache.Scopes.videos)}`.");

        clearCache.Aliases.Add("clear");

        Argument<ClearCache.Scopes> scope = new(scopeName)
        {
            Description = "The type of caches to delete."
                + $" For `{nameof(ClearCache.Scopes.playlists)}` and `{nameof(ClearCache.Scopes.channels)}`"
                + $" this will include the associated `{nameof(ClearCache.Scopes.videos)}`."
        };

        clearCache.Arguments.Add(scope);

        //TODO update to require e.g. an explicit "all videos" as scopes for deleting all videos.
        Argument<IEnumerable<string>> aliases = new(aliasesName)
        {
            Description = $"The space-separated IDs, URLs or aliases of elements in the `{scopeName}` to delete caches for."
                + $" Can be used with every `{scopeName}` but `{nameof(ClearCache.Scopes.all)}`"
                + $" while supporting user names, channel handles and slugs besides IDs for `{nameof(ClearCache.Scopes.channels)}`."
                + $" If not set, all elements in the specified `{scopeName}` are considered for deletion."
                + quoteIdsStartingWithDash
        };

        clearCache.Arguments.Add(aliases);

        Option<ushort?> notAccessedForDays = new("--last-access", "-l")
        {
            Description = "The maximum number of days since the last access of a cache file for it to be excluded from deletion."
                + " Effectively only deletes old caches that haven't been accessed for this number of days."
                + $" Ignored for explicitly set `{aliasesName}`."
        };

        clearCache.Options.Add(notAccessedForDays);

        Option<ClearCache.Modes> mode = new("--mode", "-m")
        {
            Description = "The deletion mode;"
                + $" `{nameof(ClearCache.Modes.summary)}` only outputs how many of what file type were deleted."
                + $" `{nameof(ClearCache.Modes.verbose)}` outputs the deleted file names as well as the summary."
                + $" `{nameof(ClearCache.Modes.simulate)}` lists all file names that would be deleted by running the command instead of deleting them."
                + " You can use this to preview the files that would be deleted.",
            DefaultValueFactory = _ => ClearCache.Modes.summary
        };

        clearCache.Options.Add(mode);

        clearCache.SetAction(async parsed => await handle(new ClearCache
        {
            Scope = parsed.GetValue(scope),
            Aliases = parsed.GetValue(aliases),
            NotAccessedForDays = parsed.GetValue(notAccessedForDays),
            Mode = parsed.GetValue(mode)
        }));

        return clearCache;
    }
}