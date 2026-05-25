using RecipeParser.Domain.Discovery;

namespace RecipeParser.Application.Services;

public sealed class DiscoveryRankingService(IDiscoveryStore store) : IDiscoveryRankingService
{
    private static readonly HashSet<string> ColdWeatherTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "soup", "stew", "curry", "baking", "comfort", "roast", "pasta"
    };

    private static readonly HashSet<string> WarmWeatherTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "salad", "grill", "bbq", "fresh", "quick", "no-cook", "summer"
    };

    public async Task<IReadOnlyList<DiscoveryCandidateRanking>> Rank(
        DiscoveryProfile profile,
        IReadOnlyList<DiscoveryCandidate> candidates,
        DiscoveryFeedRequest request,
        CancellationToken ct = default)
    {
        var affinities = await store.GetSourceAffinities(profile.Id, ct);
        var feedback = await store.GetFeedback(profile.Id, ct);
        var excludedUrls = request.ExistingRecipeUrls
            .Select(DiscoveryUrlNormalizer.NormalizeUrl)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var feedbackByUrl = feedback
            .GroupBy(f => f.NormalizedSourceUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(candidate => !excludedUrls.Contains(candidate.NormalizedSourceUrl))
            .Select(candidate =>
            {
                var score = 1.0 + candidate.FreshnessScore;
                var reasonCode = "popular";

                if (affinities.TryGetValue(candidate.SourceDomain, out var affinity))
                {
                    score += Math.Min(3.0, affinity.Weight);
                    reasonCode = "sourceAffinity";
                }

                if (candidate.Rating is >= 4.5)
                    score += 0.8;
                else if (candidate.Rating is >= 4.0)
                    score += 0.4;

                if (candidate.TotalMinutes is > 0 and <= 30)
                    score += 0.35;

                if (WeatherMatches(candidate, request.Weather))
                {
                    score += 0.75;
                    reasonCode = "weather";
                }

                if (feedbackByUrl.TryGetValue(candidate.NormalizedSourceUrl, out var events))
                {
                    if (events.Any(e => e.EventType.Equals("importSucceeded", StringComparison.OrdinalIgnoreCase)))
                        score += 1.2;
                    if (events.Any(e => e.EventType.Equals("open", StringComparison.OrdinalIgnoreCase)))
                        score += 0.3;
                    if (events.Any(e => e.EventType.Equals("hidden", StringComparison.OrdinalIgnoreCase) ||
                                        e.EventType.Equals("notInterested", StringComparison.OrdinalIgnoreCase)))
                        score -= 100;
                }

                return new DiscoveryCandidateRanking(candidate, score, reasonCode);
            })
            .Where(ranking => ranking.Score > -10)
            .OrderByDescending(ranking => ranking.Score)
            .ToList();
    }

    private static bool WeatherMatches(DiscoveryCandidate candidate, DiscoveryWeatherContext? weather)
    {
        if (weather is null)
            return false;

        var tags = candidate.Tags;
        var condition = weather.Condition ?? "";
        var season = weather.Season ?? "";

        if ((weather.TemperatureC is <= 10 ||
             condition.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
             condition.Contains("snow", StringComparison.OrdinalIgnoreCase) ||
             season.Contains("winter", StringComparison.OrdinalIgnoreCase))
            && tags.Any(t => ColdWeatherTags.Contains(t)))
            return true;

        if ((weather.TemperatureC is >= 22 ||
             condition.Contains("sun", StringComparison.OrdinalIgnoreCase) ||
             season.Contains("summer", StringComparison.OrdinalIgnoreCase))
            && tags.Any(t => WarmWeatherTags.Contains(t)))
            return true;

        return false;
    }
}
