using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Discovery;
using Shouldly;

namespace RecipeParser.Services.UnitTests;

[TestFixture]
public sealed class DiscoveryServiceTests
{
    [Test]
    public async Task RegisterSources_UpdatesDomainAffinitiesFromDomainsAndUrls()
    {
        var store = new FakeDiscoveryStore();
        var profiles = new DiscoveryProfileService(store);
        var profile = await profiles.ResolveProfile("install-1", "home-1", "en-GB");

        await profiles.RegisterSources(
            profile,
            ["BBCGoodFood.com"],
            ["https://www.budgetbytes.com/chicken-lettuce-wraps/?utm=abc"]);

        var affinities = await store.GetSourceAffinities(profile.Id);
        affinities["bbcgoodfood.com"].SeenCount.ShouldBe(1);
        affinities["budgetbytes.com"].SeenCount.ShouldBe(1);
    }

    [Test]
    public async Task Feed_ExcludesExistingRecipeUrlsAfterNormalization()
    {
        var store = new FakeDiscoveryStore();
        var candidate = Candidate("https://www.bbcgoodfood.com/recipes/soup?utm=feed", "Soup", ["soup"]);
        var other = Candidate("https://www.bbcgoodfood.com/recipes/pasta", "Pasta", ["pasta"]);
        var service = BuildFeedService(store, [candidate, other]);

        var feed = await service.GetFeed(new DiscoveryFeedRequest
        {
            InstallationId = "install-1",
            SourceDomains = ["bbcgoodfood.com"],
            ExistingRecipeUrls = ["https://bbcgoodfood.com/recipes/soup"],
            Limit = 10
        });

        feed.Sections.First(s => s.Id == "for-you").Items.Single().Title.ShouldBe("Pasta");
    }

    [Test]
    public async Task Ranking_BoostsWeatherMatchesAndPenalizesHiddenFeedback()
    {
        var store = new FakeDiscoveryStore();
        var profile = await store.GetOrCreateProfile("install-1", null, null);
        var soup = Candidate("https://example.com/soup", "Soup", ["soup"]);
        var salad = Candidate("https://example.com/salad", "Salad", ["salad"]);
        await store.AddFeedback(new DiscoveryFeedbackEvent
        {
            ProfileId = profile.Id,
            SourceUrl = salad.SourceUrl,
            NormalizedSourceUrl = salad.NormalizedSourceUrl,
            EventType = "hidden",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var rankings = await new DiscoveryRankingService(store).Rank(
            profile,
            [soup, salad],
            new DiscoveryFeedRequest
            {
                InstallationId = "install-1",
                Weather = new DiscoveryWeatherContext { Condition = "rain", TemperatureC = 7 }
            });

        rankings.Single().Candidate.Title.ShouldBe("Soup");
        rankings.Single().ReasonCode.ShouldBe("weather");
    }

    [Test]
    public async Task StoreCandidateLookup_FallsBackToGeneralCandidatesWhenPreferredDomainsMiss()
    {
        var store = new FakeDiscoveryStore();
        var profile = await store.GetOrCreateProfile("install-1", null, null);
        var service = BuildFeedService(store, [
            Candidate("https://example.com/soup", "Soup", ["soup"])
        ]);

        var feed = await service.GetFeed(new DiscoveryFeedRequest
        {
            InstallationId = profile.InstallationId,
            SourceDomains = ["missing.example"],
            Limit = 10
        });

        feed.Sections.First(s => s.Id == "for-you").Items.Single().Title.ShouldBe("Soup");
    }

    [Test]
    public async Task Cleanup_RemovesExpiredCacheAndOldFeedback()
    {
        var store = new FakeDiscoveryStore();
        var profile = await store.GetOrCreateProfile("install-1", null, null);
        await store.SetFeedCache(profile.Id, "expired", "{}", DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.AddFeedback(new DiscoveryFeedbackEvent
        {
            ProfileId = profile.Id,
            SourceUrl = "https://example.com/old",
            NormalizedSourceUrl = "https://example.com/old",
            EventType = "open",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-200)
        });

        (await store.DeleteExpiredFeedCaches()).ShouldBe(1);
        (await store.DeleteFeedbackOlderThan(DateTimeOffset.UtcNow.AddDays(-180))).ShouldBe(1);
    }

    private static DiscoveryFeedService BuildFeedService(FakeDiscoveryStore store, IReadOnlyList<DiscoveryCandidate> candidates)
    {
        return new DiscoveryFeedService(
            new DiscoveryProfileService(store),
            new FakeCandidateProvider(candidates),
            new DiscoveryRankingService(store),
            new FakeReasonService(),
            store,
            Options.Create(new DiscoveryOptions()),
            NullLogger<DiscoveryFeedService>.Instance);
    }

    private static DiscoveryCandidate Candidate(string url, string title, List<string> tags)
    {
        return new DiscoveryCandidate
        {
            Id = Guid.NewGuid(),
            SourceUrl = url,
            NormalizedSourceUrl = DiscoveryUrlNormalizer.NormalizeUrl(url)!,
            SourceDomain = DiscoveryUrlNormalizer.DomainFromUrl(url)!,
            Title = title,
            Tags = tags,
            Rating = 4.6,
            TotalMinutes = 25,
            FreshnessScore = 1,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class FakeCandidateProvider(IReadOnlyList<DiscoveryCandidate> candidates) : IDiscoveryCandidateProvider
    {
        public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
            DiscoveryProfile profile,
            IReadOnlyCollection<string> preferredDomains,
            CancellationToken ct = default)
        {
            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeReasonService : IDiscoveryReasonService
    {
        public Task<string> CreateReason(
            DiscoveryProfile profile,
            DiscoveryCandidate candidate,
            DiscoveryCandidateRanking ranking,
            DiscoveryWeatherContext? weather,
            CancellationToken ct = default)
        {
            return Task.FromResult(ranking.ReasonCode);
        }
    }

    private sealed class FakeDiscoveryStore : IDiscoveryStore
    {
        private readonly Dictionary<(string InstallationId, string? HomeId), DiscoveryProfile> _profiles = [];
        private readonly Dictionary<Guid, Dictionary<string, DiscoverySourceAffinity>> _affinities = [];
        private readonly List<DiscoveryFeedbackEvent> _feedback = [];
        private readonly Dictionary<string, DiscoveryCandidate> _candidates = [];
        private readonly Dictionary<(Guid ProfileId, string CacheKey), (string Json, DateTimeOffset ExpiresAt)> _cache = [];

        public Task<DiscoveryProfile> GetOrCreateProfile(string installationId, string? homeId, string? locale, CancellationToken ct = default)
        {
            var key = (installationId, homeId);
            if (!_profiles.TryGetValue(key, out var profile))
            {
                profile = new DiscoveryProfile
                {
                    InstallationId = installationId,
                    HomeId = homeId,
                    Locale = locale,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                };
                _profiles[key] = profile;
            }

            return Task.FromResult(profile);
        }

        public Task UpsertSourceAffinities(Guid profileId, IReadOnlyDictionary<string, int> domainCounts, CancellationToken ct = default)
        {
            if (!_affinities.TryGetValue(profileId, out var map))
            {
                map = new Dictionary<string, DiscoverySourceAffinity>(StringComparer.OrdinalIgnoreCase);
                _affinities[profileId] = map;
            }

            foreach (var (domain, count) in domainCounts)
            {
                if (!map.TryGetValue(domain, out var affinity))
                {
                    affinity = new DiscoverySourceAffinity { ProfileId = profileId, SourceDomain = domain };
                    map[domain] = affinity;
                }

                affinity.SeenCount += count;
                affinity.Weight = Math.Log(affinity.SeenCount + 1, 2);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, DiscoverySourceAffinity>> GetSourceAffinities(Guid profileId, CancellationToken ct = default)
        {
            IReadOnlyDictionary<string, DiscoverySourceAffinity> result = _affinities.GetValueOrDefault(profileId)
                ?? new Dictionary<string, DiscoverySourceAffinity>(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }

        public Task UpsertCandidates(IReadOnlyList<DiscoveryCandidate> candidates, CancellationToken ct = default)
        {
            foreach (var candidate in candidates)
                _candidates[candidate.NormalizedSourceUrl] = candidate;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(IReadOnlyCollection<string> sourceDomains, CancellationToken ct = default)
        {
            var preferred = sourceDomains.Count == 0
                ? _candidates.Values.ToList()
                : _candidates.Values.Where(c => sourceDomains.Contains(c.SourceDomain)).ToList();
            IReadOnlyList<DiscoveryCandidate> result = preferred.Count > 0 ? preferred : _candidates.Values.ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<DiscoveryFeedbackEvent>> GetFeedback(Guid profileId, CancellationToken ct = default)
        {
            IReadOnlyList<DiscoveryFeedbackEvent> result = _feedback.Where(f => f.ProfileId == profileId).ToList();
            return Task.FromResult(result);
        }

        public Task AddFeedback(DiscoveryFeedbackEvent feedbackEvent, CancellationToken ct = default)
        {
            _feedback.Add(feedbackEvent);
            return Task.CompletedTask;
        }

        public Task<string?> GetFeedCache(Guid profileId, string cacheKey, CancellationToken ct = default)
        {
            return Task.FromResult(
                _cache.TryGetValue((profileId, cacheKey), out var cache) && cache.ExpiresAt > DateTimeOffset.UtcNow
                    ? cache.Json
                    : null);
        }

        public Task SetFeedCache(Guid profileId, string cacheKey, string responseJson, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            _cache[(profileId, cacheKey)] = (responseJson, expiresAt);
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredFeedCaches(CancellationToken ct = default)
        {
            var deleted = _cache.Count(c => c.Value.ExpiresAt <= DateTimeOffset.UtcNow);
            foreach (var key in _cache.Where(c => c.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(c => c.Key).ToList())
                _cache.Remove(key);
            return Task.FromResult(deleted);
        }

        public Task<int> DeleteFeedbackOlderThan(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            var deleted = _feedback.RemoveAll(f => f.CreatedAt < cutoff);
            return Task.FromResult(deleted);
        }
    }
}
