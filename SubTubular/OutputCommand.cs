namespace SubTubular;

internal abstract class OutputCommand
{
    internal CommandScope Scope { get; set; }

    internal bool OutputHtml { get; set; }
    internal string FileOutputPath { get; set; }
    internal Shows? Show { get; set; }

    internal abstract string Describe();

    public enum Shows { file, folder }
}

internal sealed class SearchCommand : OutputCommand
{
    internal string Query { get; set; }
    internal ushort Padding { get; set; }
    internal override string Describe() => "searching " + Scope.Describe() + " for " + Query;
}

internal sealed class ListKeywords : OutputCommand
{
    internal override string Describe() => "listing keywords in " + Scope.Describe();
}
