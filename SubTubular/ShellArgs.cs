/*  Namespace does not match folder structure.
 *  It was deliberately chosen to signal that this namespace contains knowledge
 *  about the Shell project for sharing without directly referencing it. */
#pragma warning disable IDE0130
namespace SubTubular.Shell;
#pragma warning restore IDE0130

public static class Actions
{
    public const string search = "search", listKeywords = "keywords";
}

public static class Scopes
{
    public const string channels = "channels", playlists = "playlists", videos = "videos";
}

public static class Args
{
    public const string @for = "--for", pad = "--pad", orderBy = "--order-by",
        skip = "--skip", take = "--take", cacheHours = "--cache-hours";
}