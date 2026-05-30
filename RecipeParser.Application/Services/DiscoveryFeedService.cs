using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Application.Services;

public sealed class DiscoveryFeedService(
    IDiscoveryProfileService profiles,
    IDiscoveryCandidateProvider candidates,
    IDiscoveryRankingService ranking,
    IDiscoveryReasonService reasons,
    IDiscoveryStore store,
    IOptions<DiscoveryOptions> options,
    ILogger<DiscoveryFeedService> logger) : IDiscoveryFeedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedFeedbackEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "impression", "open", "importStarted", "importSucceeded", "hidden", "notInterested"
    };

    public async Task<DiscoveryFeedResponse> GetFeed(DiscoveryFeedRequest request, CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var profile = await profiles.ResolveProfile(request.InstallationId, request.HomeId, request.Locale, ct);
        await profiles.RegisterSources(profile, request.SourceDomains, request.ExistingRecipeUrls, ct);

        var cacheKey = CacheKey(request);
        var cached = await store.GetFeedCache(profile.Id, cacheKey, ct);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            logger.LogInformation("Discovery feed cache hit for profile {ProfileId} in {ElapsedMs}ms.", profile.Id, totalStopwatch.ElapsedMilliseconds);
            return JsonSerializer.Deserialize<DiscoveryFeedResponse>(cached, JsonOptions) ?? new DiscoveryFeedResponse();
        }

        var preferredDomains = request.SourceDomains
            .Select(DiscoveryUrlNormalizer.NormalizeDomain)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidatesStopwatch = Stopwatch.StartNew();
        var discovered = await candidates.GetCandidates(profile, preferredDomains, ct);
        candidatesStopwatch.Stop();

        var rankingStopwatch = Stopwatch.StartNew();
        var ranked = await ranking.Rank(profile, discovered, request, ct);
        rankingStopwatch.Stop();

        var limit = Math.Clamp(request.Limit <= 0 ? 30 : request.Limit, 1, 60);
        var selected = ranked.Take(limit).ToList();

        var reasonsStopwatch = Stopwatch.StartNew();
        var reasonParallelism = Math.Clamp(options.Value.ReasonGenerationParallelism, 1, 12);
        using var reasonLimiter = new SemaphoreSlim(reasonParallelism, reasonParallelism);
        var itemTasks = selected.Select(async item =>
        {
            await reasonLimiter.WaitAsync(ct);
            try
            {
                var reason = await reasons.CreateReason(profile, item.Candidate, item, request.Weather, ct);
                return new DiscoveryFeedItem
                {
                    Id = item.Candidate.Id.ToString("N"),
                    Title = item.Candidate.Title,
                    SourceUrl = item.Candidate.SourceUrl,
                    SourceDomain = item.Candidate.SourceDomain,
                    ImageUrl = item.Candidate.ImageUrl,
                    TotalMinutes = item.Candidate.TotalMinutes,
                    Rating = item.Candidate.Rating,
                    Tags = item.Candidate.Tags,
                    Reason = reason
                };
            }
            finally
            {
                reasonLimiter.Release();
            }
        });
        var items = (await Task.WhenAll(itemTasks)).ToList();
        reasonsStopwatch.Stop();

        var response = BuildSections(items);
        var cacheMinutes = Math.Clamp(options.Value.FeedCacheMinutes, 1, 240);
        await store.SetFeedCache(profile.Id, cacheKey, JsonSerializer.Serialize(response, JsonOptions), DateTimeOffset.UtcNow.AddMinutes(cacheMinutes), ct);
        logger.LogInformation(
            "Discovery feed generated for profile {ProfileId} in {ElapsedMs}ms (candidates: {CandidatesMs}ms, ranking: {RankingMs}ms, reasons: {ReasonsMs}ms, discovered: {DiscoveredCount}, selected: {SelectedCount}).",
            profile.Id,
            totalStopwatch.ElapsedMilliseconds,
            candidatesStopwatch.ElapsedMilliseconds,
            rankingStopwatch.ElapsedMilliseconds,
            reasonsStopwatch.ElapsedMilliseconds,
            discovered.Count,
            selected.Count);
        return response;
    }

    public async Task RegisterSources(DiscoveryRegisterSourcesRequest request, CancellationToken ct = default)
    {
        var profile = await profiles.ResolveProfile(request.InstallationId, request.HomeId, request.Locale, ct);
        await profiles.RegisterSources(profile, request.SourceDomains, request.ExistingRecipeUrls, ct);
    }

    public async Task RecordFeedback(DiscoveryFeedbackRequest request, CancellationToken ct = default)
    {
        if (!SupportedFeedbackEvents.Contains(request.EventType))
            throw new ArgumentException($"Unsupported discovery feedback event '{request.EventType}'.", nameof(request));

        var normalizedUrl = DiscoveryUrlNormalizer.NormalizeUrl(request.SourceUrl);
        if (normalizedUrl is null)
            throw new ArgumentException("Feedback sourceUrl must be an absolute URL.", nameof(request));

        var profile = await profiles.ResolveProfile(request.InstallationId, request.HomeId, null, ct);
        await store.AddFeedback(new DiscoveryFeedbackEvent
        {
            ProfileId = profile.Id,
            CandidateId = Guid.TryParse(request.CandidateId, out var candidateId) ? candidateId : null,
            SourceUrl = request.SourceUrl,
            NormalizedSourceUrl = normalizedUrl,
            EventType = request.EventType,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
    }

    private static DiscoveryFeedResponse BuildSections(List<DiscoveryFeedItem> items)
    {
        var fromYourSites = items
            .Where(i => i.Reason?.Contains("often save", StringComparison.OrdinalIgnoreCase) == true)
            .Take(10)
            .ToList();
        var weather = items
            .Where(i => i.Reason?.Contains("weather", StringComparison.OrdinalIgnoreCase) == true ||
                        i.Reason?.Contains("evening", StringComparison.OrdinalIgnoreCase) == true ||
                        i.Reason?.Contains("warm day", StringComparison.OrdinalIgnoreCase) == true)
            .Take(10)
            .ToList();

        var sections = new List<DiscoveryFeedSection>
        {
            new()
            {
                Id = "for-you",
                Title = "For You",
                Items = items
            }
        };

        if (fromYourSites.Count > 0)
            sections.Add(new DiscoveryFeedSection { Id = "from-your-sites", Title = "From Your Sites", Items = fromYourSites });

        if (weather.Count > 0)
            sections.Add(new DiscoveryFeedSection { Id = "weather", Title = "Good For Today", Items = weather });

        sections.Add(new DiscoveryFeedSection { Id = "popular", Title = "Popular", Items = items.OrderByDescending(i => i.Rating ?? 0).Take(10).ToList() });
        return new DiscoveryFeedResponse { Sections = sections };
    }

    private static string CacheKey(DiscoveryFeedRequest request)
    {
        var normalizedUrls = request.ExistingRecipeUrls
            .Select(DiscoveryUrlNormalizer.NormalizeUrl)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase);
        var normalizedDomains = request.SourceDomains
            .Select(DiscoveryUrlNormalizer.NormalizeDomain)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase);

        var raw = string.Join("|", normalizedDomains)
                  + "::" + string.Join("|", normalizedUrls)
                  + "::" + request.Limit
                  + "::" + request.Weather?.Condition
                  + "::" + request.Weather?.TemperatureC
                  + "::" + request.Weather?.Season;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
