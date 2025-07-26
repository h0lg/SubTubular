namespace SubTubular.Shell;

/// <summary>Provides formatted and highlighted output for Console.</summary>
internal sealed class ConsoleOutputWriter : OutputWriter, IDisposable
{
    internal ConsoleOutputWriter(OutputCommand command) : base(command) { }
    public override short GetWrittenInCurrentLine() => (short)Console.CursorLeft;

    // allowing for a buffer to avoid irregular text wrapping
    public override short GetWidth() => (short)(Console.WindowWidth - 1);

    public override void Write(string text) => Console.Write(text);
    public override void WriteUrl(string url) => Console.Write(url);

    public override void WriteHighlighted(string text)
    {
        ColorShell.SetText(ConsoleColor.Yellow);
        Console.Write(text);
        ColorShell.Reset();
    }

    public override void WriteLine(string? text = null)
    {
        if (text != null) Write(text);
        Console.WriteLine();
    }

    public override void WriteNotificationLine(string text)
    {
        ColorShell.SetText(ConsoleColor.DarkYellow);
        Console.WriteLine(text);
        ColorShell.Reset();
    }

    public override void WriteErrorLine(string text) => ColorShell.WriteErrorLine(text);
    public void Dispose() => ColorShell.Reset();
}

internal static class ColorShell
{
    internal static void Reset() => Console.ResetColor();
    internal static void SetText(ConsoleColor color) => Console.ForegroundColor = color;

    internal static void WriteErrorLine(string line)
    {
        SetText(ConsoleColor.Red);
        Console.Error.WriteLine(line);
        Reset();
    }
}