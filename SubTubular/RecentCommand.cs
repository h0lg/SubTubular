﻿using System.Text.Json;
using SubTubular.Extensions;

namespace SubTubular;

public static class RecentCommand
{
    private const string configExtension = ".js", recentsGlue = ",\n";
    private static string folder = Folder.GetPath(Folders.recent);

    static RecentCommand()
    {
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
    }

    private static string GetPath(string fileName) => Path.Combine(folder, fileName);
    private static string GetConfigPath(string fileName) => GetPath(fileName + configExtension);
    private static string GetRecentPath() => GetPath("recent.txt");

    private static async Task<string[]> LoadListAsync()
    {
        var recentPath = GetRecentPath();
        if (!File.Exists(recentPath)) return [];
        var joinedRecents = await File.ReadAllTextAsync(recentPath);
        return joinedRecents.Split(recentsGlue);
    }

    private static async Task UpdateListAsync(string[] recent)
        => await File.WriteAllTextAsync(GetRecentPath(), recent.Join(recentsGlue));

    public static async Task UpdateListAsync(string fileName)
    {
        var recent = await ListAsync();
        await UpdateListAsync(recent.OrderBy(name => name != fileName).ToArray());
    }

    public static async Task<string[]> ListAsync()
    {
        var existing = Directory.GetFiles(folder, "*" + configExtension).Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();
        var recent = await LoadListAsync();

        // drop non-existing recent entries
        recent = recent.Where(name => existing.Contains(name)).ToArray();

        // determine missing entries, prepend them and save the changes
        var missing = existing.Except(recent).ToArray();

        if (missing.Any())
        {
            recent = missing.Select(GetConfigPath).OrderByDescending(path => new FileInfo(path).CreationTime).Concat(recent).ToArray();
            await UpdateListAsync(recent);
        }

        return recent;
    }

    public static async Task SaveAsync(OutputCommand command)
    {
        string json = JsonSerializer.Serialize(command);
        string fileName = command.Describe().ToFileSafe();
        await File.WriteAllTextAsync(GetConfigPath(fileName), json);
    }

    public static async Task<OutputCommand?> LoadAsync(string fileName)
    {
        string json = await File.ReadAllTextAsync(GetConfigPath(fileName));
        return JsonSerializer.Deserialize<OutputCommand>(json);
    }

    public static void Remove(string fileName) => File.Delete(GetConfigPath(fileName));
}