using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    static partial class CommandHandler
    {
        private static Command ConfigureRecent(Func<SearchCommand, Task> search, Func<ListKeywords, Task> listKeywords)
        {
            Command recent = new("recent", "List, run or remove recently run commands.");
            recent.AddAlias("rc");
            recent.AddCommand(ConfigureListRecent());
            recent.AddCommand(ConfigureRunRecent(search, listKeywords));
            recent.AddCommand(ConfigureRemoveRecent());
            return recent;
        }

        private static Command ConfigureListRecent()
        {
            Command list = new("list", "List recently run commands.");
            list.AddAlias("l");

            list.SetHandler(async () =>
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

        private static Command ConfigureRunRecent(Func<SearchCommand, Task> search, Func<ListKeywords, Task> listKeywords)
        {
            Command run = new("run", "Run a recently run command by its number.");
            run.AddAlias("r");

            Argument<ushort> number = new("command number", "The number of the command from the recent list to run.");
            run.AddArgument(number);

            run.SetHandler(async (ctx) =>
            {
                var commands = await RecentCommands.ListAsync();
                var command = commands.GetByNumber(ctx.Parsed(number));

                if (command == null) Console.WriteLine($"Command {number} couldn't be found.");
                else
                {
                    if (command.Command is SearchCommand searchCmd) await search(searchCmd);
                    else if (command.Command is ListKeywords listCmd) await listKeywords(listCmd);
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
            remove.AddAlias("x");

            Argument<ushort> number = new("command number", "The number of the command from the recent list to remove.");
            remove.AddArgument(number);

            remove.SetHandler(async (ctx) =>
            {
                var commands = await RecentCommands.ListAsync();
                var command = commands.GetByNumber(ctx.Parsed(number));

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