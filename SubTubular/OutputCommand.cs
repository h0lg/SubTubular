namespace SubTubular;

public abstract class OutputCommand
{
    public required CommandScope Scope { get; set; }

    public bool OutputHtml { get; set; }
    public string? FileOutputPath { get; set; }
    public Shows? Show { get; set; }

    public abstract string Describe();

    public enum Shows { file, folder }
}

public sealed class SearchCommand : OutputCommand
{
    public string? Query { get; set; }
    public ushort Padding { get; set; }
    public override string Describe() => "searching " + Scope.Describe() + " for " + Query;
}

public sealed class ListKeywords : OutputCommand
{
    public override string Describe() => "listing keywords in " + Scope.Describe();
}
