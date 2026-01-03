namespace RecipeParser.Domain.Interfaces;

public interface IPageFetcher
{
    Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRenderedJsonLdAsync(string url, TimeSpan timeout, CancellationToken ct = default);
}
