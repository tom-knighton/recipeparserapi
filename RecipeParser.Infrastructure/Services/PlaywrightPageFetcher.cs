using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using RecipeParser.Domain.Interfaces;

namespace RecipeParser.Infrastructure.Services;

public sealed class PlaywrightPageFetcher(IPlaywrightBrowser browser, HttpClient httpClient) : IPageFetcher
{
    public async Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetRenderedJsonLdAsync(string url, TimeSpan timeout, CancellationToken ct = default)
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
