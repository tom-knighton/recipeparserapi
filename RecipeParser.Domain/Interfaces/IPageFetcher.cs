namespace RecipeParser.Domain.Interfaces;

public interface IPageFetcher
{
    Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default);
    Task<(string Html, IReadOnlyList<string> JsonLd)> GetRenderedJsonLdAsync(string url, TimeSpan timeout, CancellationToken ct = default);
}
