namespace RecipeParser.Domain.Discovery;

public sealed class DiscoveryProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InstallationId { get; set; } = string.Empty;
    public string? HomeId { get; set; }
    public string? Locale { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class DiscoverySourceAffinity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string SourceDomain { get; set; } = string.Empty;
    public int SeenCount { get; set; }
    public double Weight { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DiscoveryCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceUrl { get; set; } = string.Empty;
    public string NormalizedSourceUrl { get; set; } = string.Empty;
    public string SourceDomain { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public double? TotalMinutes { get; set; }
    public double? Rating { get; set; }
    public int? RatingCount { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public double FreshnessScore { get; set; }
}

public sealed class DiscoveryFeedbackEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Guid? CandidateId { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string NormalizedSourceUrl { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DiscoveryFeedCache
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
