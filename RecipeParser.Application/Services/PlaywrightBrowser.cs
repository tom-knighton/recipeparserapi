using Microsoft.Playwright;
using RecipeParser.Domain.Interfaces;

namespace RecipeParser.Application.Services;

public sealed class PlaywrightBrowser : IPlaywrightBrowser
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _pw;
    private IBrowser? _browser;

    public async Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
    {
        await EnsureStartedAsync();
        return await _browser!.NewContextAsync(options ?? new()
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36",
        });
    }

    private async Task EnsureStartedAsync()
    {
        if (_browser is not null) return;
        await _gate.WaitAsync();
        try
        {
            if (_browser is not null) return;
            _pw = await Playwright.CreateAsync();
            _browser = await _pw.Chromium.LaunchAsync(new() { Headless = true });
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _browser = null;
        _pw?.Dispose();
        _pw = null;
        _gate.Dispose();
    }
}
