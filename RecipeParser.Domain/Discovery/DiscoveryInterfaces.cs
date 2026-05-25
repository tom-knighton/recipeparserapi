namespace RecipeParser.Domain.Discovery;

public interface IDiscoveryProfileService
{
    Task<DiscoveryProfile> ResolveProfile(
        string installationId,
        string? homeId,
        string? locale,
        CancellationToken ct = default);

    Task RegisterSources(
        DiscoveryProfile profile,
        IEnumerable<string> sourceDomains,
        IEnumerable<string> sourceUrls,
        CancellationToken ct = default);
}

public interface IDiscoveryCandidateProvider
{
    Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
        DiscoveryProfile profile,
        IReadOnlyCollection<string> preferredDomains,
        CancellationToken ct = default);
}

public interface IDiscoveryCandidateIngestionService
{
    Task<int> SyncCuratedCandidates(CancellationToken ct = default);
}

public interface IDiscoveryRankingService
{
    Task<IReadOnlyList<DiscoveryCandidateRanking>> Rank(
        DiscoveryProfile profile,
        IReadOnlyList<DiscoveryCandidate> candidates,
        DiscoveryFeedRequest request,
        CancellationToken ct = default);
}

public interface IDiscoveryFeedService
{
    Task<DiscoveryFeedResponse> GetFeed(DiscoveryFeedRequest request, CancellationToken ct = default);
    Task RegisterSources(DiscoveryRegisterSourcesRequest request, CancellationToken ct = default);
    Task RecordFeedback(DiscoveryFeedbackRequest request, CancellationToken ct = default);
}

public interface IDiscoveryReasonService
{
    Task<string> CreateReason(
        DiscoveryProfile profile,
        DiscoveryCandidate candidate,
        DiscoveryCandidateRanking ranking,
        DiscoveryWeatherContext? weather,
        CancellationToken ct = default);
}

public interface IDiscoveryStore
{
    Task<DiscoveryProfile> GetOrCreateProfile(string installationId, string? homeId, string? locale, CancellationToken ct = default);
    Task UpsertSourceAffinities(Guid profileId, IReadOnlyDictionary<string, int> domainCounts, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, DiscoverySourceAffinity>> GetSourceAffinities(Guid profileId, CancellationToken ct = default);
    Task UpsertCandidates(IReadOnlyList<DiscoveryCandidate> candidates, CancellationToken ct = default);
    Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(IReadOnlyCollection<string> sourceDomains, CancellationToken ct = default);
    Task<IReadOnlyList<DiscoveryFeedbackEvent>> GetFeedback(Guid profileId, CancellationToken ct = default);
    Task AddFeedback(DiscoveryFeedbackEvent feedbackEvent, CancellationToken ct = default);
    Task<string?> GetFeedCache(Guid profileId, string cacheKey, CancellationToken ct = default);
    Task SetFeedCache(Guid profileId, string cacheKey, string responseJson, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task<int> DeleteExpiredFeedCaches(CancellationToken ct = default);
    Task<int> DeleteFeedbackOlderThan(DateTimeOffset cutoff, CancellationToken ct = default);
}

public sealed record DiscoveryCandidateRanking(
    DiscoveryCandidate Candidate,
    double Score,
    string ReasonCode);
