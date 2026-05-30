namespace RecipeParser.Domain.Discovery;

public sealed class DiscoveryOptions
{
    public bool UseOpenAIReasons { get; set; }
    public bool RunCuratedCandidateSyncOnStartup { get; set; } = true;
    public bool RunSourceCandidateSyncOnStartup { get; set; } = true;
    public bool RefreshSourcesWhenFeedIsSparse { get; set; } = true;
    public int CandidateRefreshIntervalMinutes { get; set; } = 360;
    public int SparseFeedCandidateThreshold { get; set; } = 40;
    public int SourceRequestTimeoutSeconds { get; set; } = 15;
    public int MaxCandidatesPerSource { get; set; } = 60;
    public int MaxDetailFetchesPerSource { get; set; } = 20;
    public int SourceParallelism { get; set; } = 4;
    public int SourceDetailParallelism { get; set; } = 4;
    public bool EnableUserSourceDiscovery { get; set; } = true;
    public int MaxUserSourceDomainsPerRefresh { get; set; } = 3;
    public int MaxCandidatesPerUserSource { get; set; } = 20;
    public int MaxDetailFetchesPerUserSource { get; set; } = 12;
    public int UserSourceParallelism { get; set; } = 2;
    public int ReasonGenerationParallelism { get; set; } = 6;
    public int UserSourceFailureCooldownHours { get; set; } = 24;
    public int UserSourceSuccessCooldownHours { get; set; } = 6;
    public int FeedCacheMinutes { get; set; } = 30;
    public int FeedbackRetentionDays { get; set; } = 180;
    public List<DiscoveryConfiguredCandidate> CuratedCandidates { get; set; } = [];
    public List<DiscoverySourceOptions> Sources { get; set; } = [];
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

public sealed class DiscoverySourceOptions
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<string> RssFeeds { get; set; } = [];
    public List<string> SitemapUrls { get; set; } = [];
    public List<string> IndexPages { get; set; } = [];
    public List<string> UrlMustContain { get; set; } = [];
    public List<string> DefaultTags { get; set; } = [];
    public int? MaxCandidates { get; set; }
    public int? MaxDetailFetches { get; set; }
    public bool RequireRecipeMetadata { get; set; }
}
