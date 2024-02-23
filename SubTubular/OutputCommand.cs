namespace SubTubular;

public abstract class OutputCommand
{
    public required CommandScope Scope { get; set; }

    public short OutputWidth { get; set; } = 80;
    public bool OutputHtml { get; set; }
    public string? FileOutputPath { get; set; }
    public Shows? Show { get; set; }

    public bool HasOutputPath(out string? outputPath)
    {
        var fileOutputPath = FileOutputPath?.Trim('"');
        var hasOutputPath = fileOutputPath != null && !string.IsNullOrEmpty(fileOutputPath);

        if (hasOutputPath)
        {
            outputPath = fileOutputPath;
            return true;
        }
        else
        {
            outputPath = null;
            return false;
        }
    }

    public abstract string Describe();

    public enum Shows { file, folder }
}

public sealed class SearchCommand : OutputCommand
{
    public string? Query { get; set; }
    public ushort Padding { get; set; }

    // default to ordering by highest score which is probably most useful for most purposes
    public IEnumerable<OrderOptions> OrderBy { get; set; } = new[] { OrderOptions.score };

    public override string Describe() => "searching " + Scope.Describe() + " for " + Query;

    /// <summary>Mutually exclusive <see cref="OrderOptions"/>.</summary>
    internal static OrderOptions[] Orders = [OrderOptions.uploaded, OrderOptions.score];

    /// <summary><see cref="Orders"/> and modifiers.</summary>
    public enum OrderOptions { uploaded, score, asc }
}

public sealed class ListKeywords : OutputCommand
{
    public override string Describe() => "listing keywords in " + Scope.Describe();
}
