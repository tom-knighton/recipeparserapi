using RecipeParser.Domain.Discovery;

namespace RecipeParser.Application.Services;

public sealed class DiscoveryProfileService(IDiscoveryStore store) : IDiscoveryProfileService
{
    public Task<DiscoveryProfile> ResolveProfile(
        string installationId,
        string? homeId,
        string? locale,
        CancellationToken ct = default)
    {
        return store.GetOrCreateProfile(
            installationId.Trim(),
            string.IsNullOrWhiteSpace(homeId) ? null : homeId.Trim(),
            string.IsNullOrWhiteSpace(locale) ? null : locale.Trim(),
            ct);
    }

    public Task RegisterSources(
        DiscoveryProfile profile,
        IEnumerable<string> sourceDomains,
        IEnumerable<string> sourceUrls,
        CancellationToken ct = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in sourceDomains.Select(DiscoveryUrlNormalizer.NormalizeDomain).OfType<string>())
            counts[domain] = counts.GetValueOrDefault(domain) + 1;

        foreach (var domain in sourceUrls.Select(DiscoveryUrlNormalizer.DomainFromUrl).OfType<string>())
            counts[domain] = counts.GetValueOrDefault(domain) + 1;

        return counts.Count == 0
            ? Task.CompletedTask
            : store.UpsertSourceAffinities(profile.Id, counts, ct);
    }
}
