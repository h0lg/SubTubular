namespace SubTubular.Shell;

/// <summary>Provides formatted and highlighted output for Console.</summary>
internal sealed class ConsoleOutputWriter : OutputWriter, IDisposable
{
    private const ConsoleColor highlightColor = ConsoleColor.Yellow;
    private readonly ConsoleColor regularForeGround;

    internal ConsoleOutputWriter(OutputCommand command) : base(command)
        => regularForeGround = Console.ForegroundColor; // using current

    public override short GetWrittenInCurrentLine() => (short)Console.CursorLeft;

    // allowing for a buffer to avoid irregular text wrapping
    public override short GetWidth() => (short)(Console.WindowWidth - 1);

    public override void Write(string text) => Console.Write(text);
    public override void WriteUrl(string url) => Console.Write(url);
    private void ResetConsoleColor() => Console.ForegroundColor = regularForeGround;

    public override void WriteHighlighted(string text)
    {
        Console.ForegroundColor = highlightColor;
        Console.Write(text);
        ResetConsoleColor();
    }

    public override void WriteLine(string text = null)
    {
        if (text != null) Write(text);
        Console.WriteLine();
    }

    public void Dispose() => ResetConsoleColor();
}