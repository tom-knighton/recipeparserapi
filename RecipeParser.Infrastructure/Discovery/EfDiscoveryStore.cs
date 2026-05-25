using Microsoft.EntityFrameworkCore;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class EfDiscoveryStore(DiscoveryDbContext db) : IDiscoveryStore
{
    public async Task<DiscoveryProfile> GetOrCreateProfile(string installationId, string? homeId, string? locale, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var profile = await db.DiscoveryProfiles
            .FirstOrDefaultAsync(p => p.InstallationId == installationId && p.HomeId == homeId, ct);

        if (profile is null)
        {
            profile = new DiscoveryProfile
            {
                InstallationId = installationId,
                HomeId = homeId,
                Locale = locale,
                CreatedAt = now,
                LastSeenAt = now
            };
            db.DiscoveryProfiles.Add(profile);
        }
        else
        {
            profile.LastSeenAt = now;
            if (!string.IsNullOrWhiteSpace(locale))
                profile.Locale = locale;
        }

        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task UpsertSourceAffinities(Guid profileId, IReadOnlyDictionary<string, int> domainCounts, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var domains = domainCounts.Keys.ToList();
        var existing = await db.DiscoverySourceAffinities
            .Where(a => a.ProfileId == profileId && domains.Contains(a.SourceDomain))
            .ToDictionaryAsync(a => a.SourceDomain, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var (domain, count) in domainCounts)
        {
            if (existing.TryGetValue(domain, out var affinity))
            {
                affinity.SeenCount += count;
                affinity.Weight = WeightFor(affinity.SeenCount);
                affinity.UpdatedAt = now;
            }
            else
            {
                db.DiscoverySourceAffinities.Add(new DiscoverySourceAffinity
                {
                    ProfileId = profileId,
                    SourceDomain = domain,
                    SeenCount = count,
                    Weight = WeightFor(count),
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, DiscoverySourceAffinity>> GetSourceAffinities(Guid profileId, CancellationToken ct = default)
    {
        return await db.DiscoverySourceAffinities
            .Where(a => a.ProfileId == profileId)
            .ToDictionaryAsync(a => a.SourceDomain, StringComparer.OrdinalIgnoreCase, ct);
    }

    public async Task UpsertCandidates(IReadOnlyList<DiscoveryCandidate> candidates, CancellationToken ct = default)
    {
        var normalizedUrls = candidates.Select(c => c.NormalizedSourceUrl).ToList();
        var existing = await db.DiscoveryCandidates
            .Where(c => normalizedUrls.Contains(c.NormalizedSourceUrl))
            .ToDictionaryAsync(c => c.NormalizedSourceUrl, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var candidate in candidates)
        {
            if (existing.TryGetValue(candidate.NormalizedSourceUrl, out var current))
            {
                current.SourceUrl = candidate.SourceUrl;
                current.SourceDomain = candidate.SourceDomain;
                current.Title = candidate.Title;
                current.ImageUrl = candidate.ImageUrl;
                current.TotalMinutes = candidate.TotalMinutes;
                current.Rating = candidate.Rating;
                current.RatingCount = candidate.RatingCount;
                current.Tags = candidate.Tags;
                current.LastSeenAt = candidate.LastSeenAt;
                current.FreshnessScore = candidate.FreshnessScore;
            }
            else
            {
                db.DiscoveryCandidates.Add(candidate);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(IReadOnlyCollection<string> sourceDomains, CancellationToken ct = default)
    {
        if (sourceDomains.Count == 0)
            return await db.DiscoveryCandidates.OrderByDescending(c => c.FreshnessScore).Take(250).ToListAsync(ct);

        var preferred = await db.DiscoveryCandidates
            .Where(c => sourceDomains.Contains(c.SourceDomain))
            .OrderByDescending(c => c.FreshnessScore)
            .Take(250)
            .ToListAsync(ct);

        if (preferred.Count > 0)
            return preferred;

        return await db.DiscoveryCandidates
            .OrderByDescending(c => c.FreshnessScore)
            .Take(250)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DiscoveryFeedbackEvent>> GetFeedback(Guid profileId, CancellationToken ct = default)
    {
        return await db.DiscoveryFeedbackEvents
            .Where(e => e.ProfileId == profileId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task AddFeedback(DiscoveryFeedbackEvent feedbackEvent, CancellationToken ct = default)
    {
        db.DiscoveryFeedbackEvents.Add(feedbackEvent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetFeedCache(Guid profileId, string cacheKey, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cache = await db.DiscoveryFeedCaches
            .FirstOrDefaultAsync(c => c.ProfileId == profileId && c.CacheKey == cacheKey && c.ExpiresAt > now, ct);
        return cache?.ResponseJson;
    }

    public async Task SetFeedCache(Guid profileId, string cacheKey, string responseJson, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cache = await db.DiscoveryFeedCaches
            .FirstOrDefaultAsync(c => c.ProfileId == profileId && c.CacheKey == cacheKey, ct);

        if (cache is null)
        {
            db.DiscoveryFeedCaches.Add(new DiscoveryFeedCache
            {
                ProfileId = profileId,
                CacheKey = cacheKey,
                ResponseJson = responseJson,
                ExpiresAt = expiresAt,
                CreatedAt = now
            });
        }
        else
        {
            cache.ResponseJson = responseJson;
            cache.ExpiresAt = expiresAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<int> DeleteExpiredFeedCaches(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return db.DiscoveryFeedCaches
            .Where(c => c.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);
    }

    public Task<int> DeleteFeedbackOlderThan(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        return db.DiscoveryFeedbackEvents
            .Where(e => e.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static double WeightFor(int seenCount) => Math.Log(Math.Max(1, seenCount) + 1, 2);
}
