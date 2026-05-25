namespace RecipeParser.Domain.Discovery;

public sealed class DiscoveryOptions
{
    public bool UseOpenAIReasons { get; set; }
    public bool RunCuratedCandidateSyncOnStartup { get; set; } = true;
    public int FeedCacheMinutes { get; set; } = 30;
    public int FeedbackRetentionDays { get; set; } = 180;
    public List<DiscoveryConfiguredCandidate> CuratedCandidates { get; set; } = [];
}

public sealed class DiscoveryConfiguredCandidate
{
    public string SourceUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public double? TotalMinutes { get; set; }
    public double? Rating { get; set; }
    public int? RatingCount { get; set; }
    public List<string> Tags { get; set; } = [];
}
