using RecipeParser.Domain.Interfaces;

namespace RecipeParser.Application.Services;

using Microsoft.Playwright;

public sealed class PlaywrightPageFetcher(IPlaywrightBrowser browser) : IPageFetcher
{
    private static readonly HttpClient HttpClient = new();

    static PlaywrightPageFetcher()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
    }

    public async Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default)
    {
        return await HttpClient.GetStringAsync(url, ct);
    }

    public async Task<IReadOnlyList<string>> GetRenderedJsonLdAsync(
        string url, TimeSpan timeout, CancellationToken ct = default)
    {
        var ctx = await browser.NewContextAsync(new()
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36",
        });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)timeout.TotalMilliseconds });

        try { await page.ClickAsync("button:has-text('Accept all')", new() { Timeout = 10 }); } catch { /* ignore */ }

        try { await page.WaitForSelectorAsync("script[type='application/ld+json']", new() { Timeout = 10 }); } catch { /* ok if none */ }

        var jsons = await page.EvalOnSelectorAllAsync<string[]>(
            "script[type='application/ld+json']",
            "nodes => nodes.map(n => n.textContent ?? '')");

        await ctx.CloseAsync();
        return jsons.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    }
}
