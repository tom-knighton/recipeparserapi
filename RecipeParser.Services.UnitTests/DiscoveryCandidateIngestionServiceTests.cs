using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RecipeParser.Domain.Discovery;
using RecipeParser.Infrastructure.Discovery;
using Shouldly;

namespace RecipeParser.Services.UnitTests;

public sealed class DiscoveryCandidateIngestionServiceTests
{
    [Test]
    public async Task SyncSourceCandidates_UpsertsRssRecipes()
    {
        var store = new FakeDiscoveryStore();
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => """
            <?xml version="1.0" encoding="utf-8"?>
            <rss xmlns:media="http://search.yahoo.com/mrss/" version="2.0">
              <channel>
                <item>
                  <title>Big Bean Ceviche</title>
                  <link>https://www.bonappetit.com/recipe/vegetarian-bean-ceviche</link>
                  <pubDate>Thu, 21 May 2026 18:00:00 +0000</pubDate>
                  <category>Recipes</category>
                  <category>Vegan</category>
                  <media:thumbnail url="https://assets.bonappetit.com/photos/bean.jpg" />
                </item>
                <item>
                  <title>Restaurant news</title>
                  <link>https://www.bonappetit.com/story/restaurants-world-cup-bubble</link>
                </item>
              </channel>
            </rss>
            """));

        var service = new DiscoveryCandidateIngestionService(
            Options.Create(new DiscoveryOptions
            {
                Sources =
                [
                    new DiscoverySourceOptions
                    {
                        Name = "Bon Appetit",
                        Domain = "bonappetit.com",
                        RssFeeds = ["https://www.bonappetit.com/feed/rss"],
                        UrlMustContain = ["/recipe/"],
                        DefaultTags = ["magazine"],
                        MaxDetailFetches = 0
                    }
                ]
            }),
            store,
            client,
            NullLogger<DiscoveryCandidateIngestionService>.Instance);

        var count = await service.SyncSourceCandidates();

        count.ShouldBe(1);
        var candidate = store.Candidates.Single();
        candidate.Title.ShouldBe("Big Bean Ceviche");
        candidate.SourceDomain.ShouldBe("bonappetit.com");
        candidate.Tags.ShouldContain("vegan");
        candidate.ImageUrl.ShouldBe("https://assets.bonappetit.com/photos/bean.jpg");
    }

    [Test]
    public async Task SyncUserSourceCandidates_RequiresRecipeMetadata()
    {
        var store = new FakeDiscoveryStore();
        using var client = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/feed/")
            {
                return """
                    <?xml version="1.0" encoding="utf-8"?>
                    <rss version="2.0">
                      <channel>
                        <item>
                          <title>Real Dinner</title>
                          <link>https://example.com/real-dinner/</link>
                        </item>
                        <item>
                          <title>Kitchen News</title>
                          <link>https://example.com/kitchen-news/</link>
                        </item>
                      </channel>
                    </rss>
                    """;
            }

            if (request.RequestUri?.AbsolutePath == "/real-dinner/")
            {
                return """
                    <html><head>
                      <script type="application/ld+json">
                      {
                        "@context": "https://schema.org",
                        "@type": "Recipe",
                        "name": "Real Dinner",
                        "image": "https://example.com/real-dinner.jpg",
                        "totalTime": "PT35M",
                        "keywords": "dinner, quick"
                      }
                      </script>
                    </head></html>
                    """;
            }

            return "<html><head><title>Not a recipe</title></head></html>";
        }));

        var service = new DiscoveryCandidateIngestionService(
            Options.Create(new DiscoveryOptions
            {
                MaxCandidatesPerUserSource = 4,
                MaxDetailFetchesPerUserSource = 4
            }),
            store,
            client,
            NullLogger<DiscoveryCandidateIngestionService>.Instance);

        var count = await service.SyncUserSourceCandidates(["example.com"]);

        count.ShouldBe(1);
        var candidate = store.Candidates.Single();
        candidate.Title.ShouldBe("Real Dinner");
        candidate.ImageUrl.ShouldBe("https://example.com/real-dinner.jpg");
        candidate.TotalMinutes.ShouldBe(35);
        candidate.Tags.ShouldContain("from-your-sites");
        candidate.Tags.ShouldContain("recipe-metadata");
        candidate.Tags.ShouldContain("quick");
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(request))
            });
        }
    }

    private sealed class FakeDiscoveryStore : IDiscoveryStore
    {
        public List<DiscoveryCandidate> Candidates { get; } = [];

        public Task<DiscoveryProfile> GetOrCreateProfile(string installationId, string? homeId, string? locale, CancellationToken ct = default) =>
            Task.FromResult(new DiscoveryProfile { InstallationId = installationId, HomeId = homeId, Locale = locale });

        public Task UpsertSourceAffinities(Guid profileId, IReadOnlyDictionary<string, int> domainCounts, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, DiscoverySourceAffinity>> GetSourceAffinities(Guid profileId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DiscoverySourceAffinity>>(new Dictionary<string, DiscoverySourceAffinity>());

        public Task UpsertCandidates(IReadOnlyList<DiscoveryCandidate> candidates, CancellationToken ct = default)
        {
            Candidates.AddRange(candidates);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(IReadOnlyCollection<string> sourceDomains, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryCandidate>>(Candidates);

        public Task<IReadOnlyList<DiscoveryFeedbackEvent>> GetFeedback(Guid profileId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryFeedbackEvent>>([]);

        public Task AddFeedback(DiscoveryFeedbackEvent feedbackEvent, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string?> GetFeedCache(Guid profileId, string cacheKey, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task SetFeedCache(Guid profileId, string cacheKey, string responseJson, DateTimeOffset expiresAt, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> DeleteExpiredFeedCaches(CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<int> DeleteFeedbackOlderThan(DateTimeOffset cutoff, CancellationToken ct = default) =>
            Task.FromResult(0);
    }
}
