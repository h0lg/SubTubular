using System.Text.Json;

namespace SubTubular;

public static class RecentCommands
{
    private static readonly string recentPath = Path.Combine(Folder.GetPath(Folders.storage), "recent.json");
    private static readonly Comparison<Item> byLastRunDesc = new((fst, snd) => snd.LastRun.CompareTo(fst.LastRun));

    public static async Task<List<Item>> ListAsync()
    {
        if (!File.Exists(recentPath)) return [];

        try
        {
            string json = await File.ReadAllTextAsync(recentPath);
            return JsonSerializer.Deserialize<List<Item>>(json) ?? [];
        }
        catch (Exception ex)
        {
            var copyPath = Path.Combine(Path.GetDirectoryName(recentPath)!, $"recent {DateTime.Now:yyyy-MM-dd HHmmss}.json");
            File.Copy(recentPath, copyPath, overwrite: false); // create a copy of the recent file to avoid losing it completely, may still be manually fixed

            await ErrorLog.WriteAsync(ex.ToString(),
                header: "Error loading recent commands. A copy has been saved to " + copyPath,
                fileNameDescription: "loading recent commands");

            return [];
        }
    }

    public static async Task SaveAsync(IEnumerable<Item> commands)
    {
        string json = JsonSerializer.Serialize(commands);
        await File.WriteAllTextAsync(recentPath, json);
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
