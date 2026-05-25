using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using RecipeParser.Domain.Discovery;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Infrastructure.Discovery;
using RecipeParser.Infrastructure.Services;

namespace RecipeParser.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRecipeParserInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IPlaywrightBrowser, PlaywrightBrowser>();
        services.AddHttpClient<IPageFetcher, PlaywrightPageFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        });

        services.Configure<OpenAiRecipeParserOptions>(configuration.GetSection("OpenAI"));
        services.Configure<DiscoveryOptions>(configuration.GetSection("Discovery"));
        services.AddDbContext<DiscoveryDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Discovery")));

        services.AddScoped<IDiscoveryStore, EfDiscoveryStore>();
        services.AddScoped<IDiscoveryCandidateProvider, ConfiguredDiscoveryCandidateProvider>();
        services.AddScoped<IDiscoveryCandidateIngestionService, CuratedDiscoveryCandidateIngestionService>();
        services.AddHostedService<DiscoveryStartupSyncService>();

        services.AddHttpClient<IRecipeDescriptionParser, OpenAiRecipeDescriptionParser>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OpenAiRecipeParserOptions>>().Value;
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(30);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                client.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiKey);
        });
        services.AddHttpClient<IDiscoveryReasonService, DiscoveryReasonService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OpenAiRecipeParserOptions>>().Value;
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(15);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                client.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiKey);
        });

        return services;
    }
}
