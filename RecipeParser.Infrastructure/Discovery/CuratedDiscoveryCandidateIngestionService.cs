using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class CuratedDiscoveryCandidateIngestionService(
    IOptions<DiscoveryOptions> options,
    IDiscoveryStore store,
    ILogger<CuratedDiscoveryCandidateIngestionService> logger) : IDiscoveryCandidateIngestionService
{
    public async Task<int> SyncCuratedCandidates(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = options.Value.CuratedCandidates
            .Select(item => ToCandidate(item, now))
            .OfType<DiscoveryCandidate>()
            .GroupBy(c => c.NormalizedSourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (candidates.Count == 0)
        {
            logger.LogInformation("Discovery curated candidate sync skipped because no candidates are configured.");
            return 0;
        }

        await store.UpsertCandidates(candidates, ct);
        logger.LogInformation("Discovery curated candidate sync upserted {CandidateCount} candidates.", candidates.Count);
        return candidates.Count;
    }

    private static DiscoveryCandidate? ToCandidate(DiscoveryConfiguredCandidate item, DateTimeOffset now)
    {
        var normalizedUrl = DiscoveryUrlNormalizer.NormalizeUrl(item.SourceUrl);
        var domain = DiscoveryUrlNormalizer.DomainFromUrl(item.SourceUrl);

        if (normalizedUrl is null ||
            domain is null ||
            string.IsNullOrWhiteSpace(item.Title))
            return null;

        return new DiscoveryCandidate
        {
            SourceUrl = item.SourceUrl.Trim(),
            NormalizedSourceUrl = normalizedUrl,
            SourceDomain = domain,
            Title = item.Title.Trim(),
            ImageUrl = string.IsNullOrWhiteSpace(item.ImageUrl) ? null : item.ImageUrl.Trim(),
            TotalMinutes = item.TotalMinutes,
            Rating = item.Rating,
            RatingCount = item.RatingCount,
            Tags = item.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            FirstSeenAt = now,
            LastSeenAt = now,
            FreshnessScore = 1
        };
    }
}
