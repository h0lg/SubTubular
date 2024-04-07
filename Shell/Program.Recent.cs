using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    static partial class CommandHandler
    {
        private static Command ConfigureRecent(Func<SearchCommand, Task> search, Func<ListKeywords, Task> listKeywords)
        {
            Command recent = new("recent", "List, run or remove recently run commands.");
            recent.AddAlias("r");
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
                var saved = await RecentCommand.ListAsync();

                if (saved.Length == 0)
                {
                    Console.WriteLine("There are no saved configurations.");
                    return;
                }

                var numberedConfigs = saved.Select((name, index) => (name, number: index + 1)).ToArray();
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
                var fileName = await GetConfigFileName(ctx.Parsed(number));
                var command = await RecentCommand.LoadAsync(fileName!);
                if (command == null) Console.WriteLine($"Command {number} couldn't be loaded.");

                if (command is SearchCommand searchCmd) await search(searchCmd);
                else if (command is ListKeywords listCmd) await listKeywords(listCmd);
                else throw new NotSupportedException("Unsupported command type " + command!.GetType());

                await RecentCommand.UpdateListAsync(fileName!);
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
                var fileName = await GetConfigFileName(ctx.Parsed(number));
                RecentCommand.Remove(fileName!);
            });

            return remove;
        }

        public static async Task<string?> GetConfigFileName(int number)
        {
            var index = number - 1;
            string[] configs = await RecentCommand.ListAsync();
            if (configs.Length <= index) return null;
            return configs[index];
        }
    }
}