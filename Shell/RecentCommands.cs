using System.CommandLine;

namespace SubTubular.Shell;

static partial class CommandInterpreter
{
    private static Command ConfigureRecent(Func<SearchCommand, CancellationToken, Task> search,
        Func<ListKeywords, CancellationToken, Task> listKeywords)
    {
        Command recent = new("recent", "List, run or remove recently run commands.");
        recent.Aliases.Add("rc");
        recent.Subcommands.Add(ConfigureListRecent());
        recent.Subcommands.Add(ConfigureRunRecent(search, listKeywords));
        recent.Subcommands.Add(ConfigureRemoveRecent());
        return recent;
    }

    private static Command ConfigureListRecent()
    {
        Command list = new("list", "List recently run commands.");
        list.Aliases.Add("l");

        list.SetAction(async _ =>
        {
            var saved = await RecentCommands.ListAsync();

            if (saved.Count == 0)
            {
                Console.WriteLine("There are no saved configurations.");
                return;
            }

            var numberedConfigs = saved.OrderByDescending(c => c.LastRun)
                .Select((cmd, index) => (cmd.Description, number: index + 1)).ToArray();

            var digits = numberedConfigs.Max(s => s.number).ToString().Length; // determines length of longest number

            foreach (var (name, number) in numberedConfigs)
                Console.WriteLine(number.ToString().PadLeft(digits) + " " + name);
        });

        return list;
    }

    private static Command ConfigureRunRecent(Func<SearchCommand, CancellationToken, Task> search,
        Func<ListKeywords, CancellationToken, Task> listKeywords)
    {
        Command run = new("run", "Run a recently run command by its number.");
        run.Aliases.Add("r");

        Argument<ushort> number = new("command number") { Description = "The number of the command from the recent list to run." };
        run.Arguments.Add(number);

        run.SetCancelableAction(async (parsed, token) =>
        {
            var commands = await RecentCommands.ListAsync();
            var command = commands.GetByNumber(parsed.GetValue(number));

            if (command == null) Console.WriteLine($"Command {number} couldn't be found.");
            else
            {
                if (command.Command is SearchCommand searchCmd) await search(searchCmd, token);
                else if (command.Command is ListKeywords listCmd) await listKeywords(listCmd, token);
                else throw new NotSupportedException("Unsupported command type " + command.Command?.GetType());

                command.LastRun = DateTime.Now;
                await RecentCommands.SaveAsync(commands);
            }
        });

        return run;
    }

    private static Command ConfigureRemoveRecent()
    {
        Command remove = new("remove", "Remove a command by its number from the recent list.");
        remove.Aliases.Add("x");

        Argument<ushort> number = new("command number") { Description = "The number of the command from the recent list to remove." };
        remove.Arguments.Add(number);

        remove.SetAction(async parsed =>
        {
            var commands = await RecentCommands.ListAsync();
            var command = commands.GetByNumber(parsed.GetValue(number));

            if (command == null) Console.WriteLine($"Command {number} couldn't be found.");
            else
            {
                commands.Remove(command);
                await RecentCommands.SaveAsync(commands);
            }
        });

        return remove;
    }
}

internal static class ListExtensions
{
    internal static T? GetByNumber<T>(this List<T> list, int number)
    {
        var index = number - 1;
        if (index < 0 || list.Count <= index) return default;
        return list[index];
    }
}