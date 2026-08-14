using System.Text.Json;
using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

public static class RecentCommands
{
    private static readonly string recentPath = Path.Combine(Folder.GetPath(Folders.storage), "recent.json");
    private static readonly Comparison<Item> byLastRunDesc = new((fst, snd) => snd.LastRun.CompareTo(fst.LastRun));
    private static readonly JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    public static async Task<List<Item>> ListAsync(CancellationToken token = default)
    {
        if (!File.Exists(recentPath)) return [];

        try
        {
            await using FileStream stream = new(recentPath, FileMode.Open);
            return await JsonSerializer.DeserializeAsync<List<Item>>(stream, options, token).ContinueAnywhere() ?? [];
        }
        catch (Exception ex)
        {
            var copyPath = Path.Combine(Path.GetDirectoryName(recentPath)!, $"recent {DateTime.Now:yyyy-MM-dd HHmmss}.json");
            File.Copy(recentPath, copyPath, overwrite: false); // create a copy of the recent file to avoid losing it completely, may still be manually fixed

            await ErrorLog.WriteAsync(ex.ToString(),
                header: "Error loading recent commands. A copy has been saved to " + copyPath,
                fileNameDescription: "loading recent commands").ContinueAnywhere();

            return [];
        }
    }

    public static async Task SaveAsync(IEnumerable<Item> commands, CancellationToken token = default)
    {
        foreach (var item in commands) item.Command?.RemoveEmptyScopes();
        await using FileStream stream = new(recentPath, FileMode.Create);
        await JsonSerializer.SerializeAsync(stream, commands, options, token).ContinueAnywhere();
    }

    public static void AddOrUpdate(this List<Item> list, OutputCommand command)
    {
        var item = list.SingleOrDefault(i => command.Equals(i.Command));

        if (item == null)
        {
            item = new();
            list.Insert(0, item);
        }

        item.LastRun = DateTime.Now;
        item.Command = command;
        item.Description = command.Describe();
        list.Sort(byLastRunDesc);
    }

    public sealed class Item
    {
        public string? Description { get; set; }
        public DateTime LastRun { get; set; }
        public OutputCommand? Command { get; set; }
    }
}
