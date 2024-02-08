using SubTubular.Extensions;

namespace SubTubular;

public abstract class FileOutputWriter : OutputWriter
{
    private readonly string extension;
    private short writtenInCurrentLine;

    internal FileOutputWriter(OutputCommand command, string extension) : base(command)
        => this.extension = extension;

    public override short GetWrittenInCurrentLine() => writtenInCurrentLine;
    public abstract void WriteCounted(string text);
    public abstract void WriteLineBreak();
    public abstract string Flush();

    public override void Write(string text)
    {
        WriteCounted(text);
        writtenInCurrentLine += (short)text.Length;
    }

    public override void WriteLine(string? text = null)
    {
        if (text != null) Write(text);
        WriteLineBreak();
        writtenInCurrentLine = 0;
    }

    /// <summary>Saves the output returned by <see cref="Flush"/>
    /// to the path returned by <see cref="GetOutputFilePath"/> and returns the path.</summary>
    public async ValueTask<string> SaveFile()
    {
        var path = GetOutputFilePath();
        await FileHelper.WriteTextAsync(Flush(), path);
        return path;
    }

    public virtual string GetOutputFilePath()
    {
        var hasOutputPath = command.HasOutputPath(out string? fileOutputPath);
        if (hasOutputPath && !fileOutputPath!.IsDirectoryPath()) return fileOutputPath!; // treat as full file path

        // generate default file path
        var fileName = command.Describe().ToFileSafe() + extension;
        var folder = hasOutputPath ? fileOutputPath! : Folder.GetPath(Folders.output);
        return Path.Combine(folder, fileName);
    }
}

public class TextOutputWriter : FileOutputWriter, IDisposable
{
    private readonly StringWriter textOut;

    public TextOutputWriter(OutputCommand command) : base(command, ".txt")
        => textOut = new StringWriter();

    public override void WriteCounted(string text) => textOut.Write(text);
    public override void WriteHighlighted(string text) => Write($"*{text}*");
    public override void WriteUrl(string url) => Write(url);
    public override void WriteLineBreak() => textOut.WriteLine();
    public override string Flush() => textOut.ToString();
    public void Dispose() => textOut.Dispose();
}
