namespace ModManager.Application.Models;

/// <summary>
/// A fetched page's URL (post-redirect) and raw HTML, handed to a strategy's parser.
/// </summary>
public sealed record PageContent(Uri Url, string Html);
