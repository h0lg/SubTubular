using AngleSharp;
using AngleSharp.Dom;

namespace SubTubular;

public class HtmlOutputWriter : FileOutputWriter, IDisposable
{
    private readonly IDocument document;
    private readonly IElement output;

    public HtmlOutputWriter(OutputCommand command) : base(command, ".html")
    {
        var context = BrowsingContext.New(Configuration.Default);
        document = context.OpenNewAsync().Result;

        var style = document.CreateElement("style");
        style.TextContent = "pre > em { background-color: yellow }";
        document.Head!.Append(style);

        output = document.CreateElement("pre");
        document.Body!.Append(output);
    }

    public override void WriteCounted(string text) => output.InnerHtml += text;
    public override void WriteLineBreak() => output.InnerHtml += Environment.NewLine;

    public override void WriteHighlighted(string text)
    {
        var em = document.CreateElement("em");
        em.TextContent = text;
        output.Append(em);
    }

    public override void WriteUrl(string url)
    {
        var hlink = document.CreateElement("a");
        hlink.SetAttribute("href", url);
        hlink.SetAttribute("target", "_blank");
        hlink.TextContent = url;
        output.Append(hlink);
    }

    public override string Flush() => document.DocumentElement.OuterHtml;
    public void Dispose() => document.Dispose();
}
