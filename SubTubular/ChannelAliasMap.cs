using System.Collections.Concurrent;

namespace SubTubular;

/// <summary>Maps valid channel aliases by <see cref="Type"/> and <see cref="Value"/>
/// to an accessible <see cref="ChannelId"/> or null if none was found.</summary>
public sealed class ChannelAliasMap
{
    public required string Type { get; set; }
    public required string Value { get; set; }
    public string? ChannelId { get; set; }

    internal static (string, string) GetTypeAndValue(object alias) => (alias.GetType().Name, alias.ToString()!);

    #region STORAGE
    internal const string StorageKey = "known channel alias maps";

    private static ConcurrentDictionary<(string Type, string Value), ChannelAliasMap>? localCache; // for less file I/O during concurrent validations
    private static readonly TimeSpan inactivityPeriod = TimeSpan.FromSeconds(5); // inactivity period before saving changes and/or discarding local cache
    private static Timer? inactivityTimer; // helps scheduling the persistence of changes and clearing of cache
    private static bool changesMade; // tracks if changes were made to the local cache
    private static readonly SemaphoreSlim access = new(1, 1); // ensures thread-safe access and updates to local cache and dataStore version

    public override int GetHashCode() => HashCode.Combine(Type, Value); // for safely using HashSet

    /// <summary>Loads the current <see cref="ChannelAliasMap"/>s from the <see cref="localCache"/>
    /// or the <paramref name="dataStore"/>.</summary>
    internal static async Task<HashSet<ChannelAliasMap>> LoadListAsync(DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            await LoadLocalCacheAsync(dataStore);
            return [.. localCache!.Values];
        }
        finally
        {
            access.Release();
        }
    }

    /// <summary>Adds <paramref name="entries"/> to the <see cref="localCache"/>
    /// and saves them via <see cref="LoadLocalCacheAsync(DataStore)"/> to the <paramref name="dataStore"/>.</summary>
    internal static async Task AddEntriesAsync(IEnumerable<ChannelAliasMap> entries, DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            await LoadLocalCacheAsync(dataStore);

            foreach (var entry in entries)
            {
                var key = (entry.Type, entry.Value);
                if (localCache!.ContainsKey(key)) continue;
                localCache[key] = entry;
                changesMade = true; // only if entry was added
            }
        }
        finally
        {
            access.Release();
        }
    }

    /// <summary>Removes <paramref name="entries"/> from the <see cref="localCache"/>
    /// and saves changes via <see cref="LoadLocalCacheAsync(DataStore)"/> to the <paramref name="dataStore"/>.</summary>
    internal static async Task RemoveEntriesAsync(IEnumerable<ChannelAliasMap> entries, DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            await LoadLocalCacheAsync(dataStore);

            foreach (var entry in entries)
            {
                if (localCache!.TryRemove((entry.Type, entry.Value), out _))
                    changesMade = true; // Mark changes as made if an entry was actually removed
            }
        }
        finally
        {
            access.Release();
        }
    }

    private static async Task LoadLocalCacheAsync(DataStore dataStore)
    {
        // load data from the data store if the local cache is empty
        if (localCache == null)
        {
            var stored = await dataStore.GetAsync<HashSet<ChannelAliasMap>>(StorageKey) ?? [];
            localCache = new();

            foreach (var entry in stored)
                localCache[(entry.Type, entry.Value)] = entry;
        }

        // to make sure cache is cleared eventually and postpone clearing it during a longer running validation
        DebounceClearCache(dataStore);
    }

    /// <summary>Resets the <see cref="inactivityTimer"/> and schedules <see cref="PersistCacheAsync(DataStore)"/>
    /// to the <paramref name="dataStore"/> for when the <see cref="inactivityPeriod"/> expires.</summary>
    private static void DebounceClearCache(DataStore dataStore)
    {
        if (inactivityTimer == null)
            inactivityTimer = new Timer(async _ => await PersistCacheAsync(dataStore), null, inactivityPeriod, Timeout.InfiniteTimeSpan);
        else inactivityTimer.Change(inactivityPeriod, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Persists the <see cref="localCache"/>
    /// to the <paramref name="dataStore"/> if <see cref="changesMade"/>.</summary>
    private static async Task PersistCacheAsync(DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            if (localCache != null)
            {
                if (changesMade)
                {
                    await dataStore.SetAsync(StorageKey, localCache.Values.ToHashSet());
                    changesMade = false; // mark persisted
                }

                localCache.Clear(); // to free up memory
                localCache = null; // to mark it not loaded
            }

            // Stop the inactivity timer after saving
            inactivityTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            inactivityTimer?.Dispose();
            inactivityTimer = null;
        }
        finally
        {
            access.Release();
        }
    }
    #endregion
}