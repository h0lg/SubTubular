using System.Text.Json;

namespace SubTubular;

public static class RecentCommands
{
    private static readonly string recentPath = Path.Combine(Folder.GetPath(Folders.storage), "recent.json");
    private static readonly Comparison<Item> byLastRunDesc = new((fst, snd) => snd.LastRun.CompareTo(fst.LastRun));

    public static async Task<List<Item>> ListAsync()
    {
        if (!File.Exists(recentPath)) return [];
        var json = await File.ReadAllTextAsync(recentPath);
        return JsonSerializer.Deserialize<List<Item>>(json!) ?? [];
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

    [Serializable]
    public sealed class Item
    {
        public string? Description { get; set; }
        public DateTime LastRun { get; set; }
        public OutputCommand? Command { get; set; }
    }
}
