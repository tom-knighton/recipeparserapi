using RecipeParser.Domain.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class ConfiguredDiscoveryCandidateProvider(
    IDiscoveryStore store,
    IDiscoveryCandidateIngestionService ingestion,
    IOptions<DiscoveryOptions> options,
    ILogger<ConfiguredDiscoveryCandidateProvider> logger) : IDiscoveryCandidateProvider
{
    private static readonly SemaphoreSlim SparseRefreshLock = new(1, 1);
    private static DateTimeOffset _lastSparseRefresh = DateTimeOffset.MinValue;

    public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
        DiscoveryProfile profile,
        IReadOnlyCollection<string> preferredDomains,
        CancellationToken ct = default) => GetCandidatesInternal(preferredDomains, ct);

    private async Task<IReadOnlyList<DiscoveryCandidate>> GetCandidatesInternal(
        IReadOnlyCollection<string> preferredDomains,
        CancellationToken ct)
    {
        var candidates = await store.GetCandidates(preferredDomains, ct);
        await RefreshUserSources(preferredDomains, ct);
        candidates = await store.GetCandidates(preferredDomains, ct);

        if (!options.Value.RefreshSourcesWhenFeedIsSparse ||
            candidates.Count >= Math.Max(1, options.Value.SparseFeedCandidateThreshold))
            return candidates;

        var now = DateTimeOffset.UtcNow;
        var minInterval = TimeSpan.FromMinutes(Math.Clamp(options.Value.CandidateRefreshIntervalMinutes, 5, 1440));
        if (now - _lastSparseRefresh < minInterval)
            return candidates;

        if (!await SparseRefreshLock.WaitAsync(0, ct))
            return candidates;

        try
        {
            if (DateTimeOffset.UtcNow - _lastSparseRefresh < minInterval)
                return candidates;

            logger.LogInformation(
                "Discovery candidate store has {CandidateCount} candidates, below sparse threshold {SparseThreshold}. Refreshing configured sources.",
                candidates.Count,
                options.Value.SparseFeedCandidateThreshold);

            await ingestion.SyncSourceCandidates(ct);
            await RefreshUserSources(preferredDomains, ct);
            _lastSparseRefresh = DateTimeOffset.UtcNow;
            return await store.GetCandidates(preferredDomains, ct);
        }
        finally
        {
            SparseRefreshLock.Release();
        }
    }

    private async Task RefreshUserSources(IReadOnlyCollection<string> preferredDomains, CancellationToken ct)
    {
        if (!options.Value.EnableUserSourceDiscovery || preferredDomains.Count == 0)
            return;

        try
        {
            await ingestion.SyncUserSourceCandidates(preferredDomains, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Discovery user source refresh failed.");
        }
    }
}
