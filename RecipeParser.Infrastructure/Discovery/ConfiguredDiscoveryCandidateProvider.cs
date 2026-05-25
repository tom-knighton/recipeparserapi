using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class ConfiguredDiscoveryCandidateProvider(
    IDiscoveryStore store) : IDiscoveryCandidateProvider
{
    public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
        DiscoveryProfile profile,
        IReadOnlyCollection<string> preferredDomains,
        CancellationToken ct = default)
    {
        return store.GetCandidates(preferredDomains, ct);
    }
}
