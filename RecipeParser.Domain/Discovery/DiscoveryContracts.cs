using System.ComponentModel.DataAnnotations;

namespace RecipeParser.Domain.Discovery;

public sealed class DiscoveryFeedRequest
{
    [Required]
    [MinLength(1)]
    public string InstallationId { get; set; } = string.Empty;

    public string? HomeId { get; set; }
    public string? Locale { get; set; }
    public List<string> SourceDomains { get; set; } = [];
    public List<string> ExistingRecipeUrls { get; set; } = [];
    public DiscoveryWeatherContext? Weather { get; set; }
    public int Limit { get; set; } = 30;
}

public sealed class DiscoveryRegisterSourcesRequest
{
    [Required]
    [MinLength(1)]
    public string InstallationId { get; set; } = string.Empty;

    public string? HomeId { get; set; }
    public string? Locale { get; set; }
    public List<string> SourceDomains { get; set; } = [];
    public List<string> ExistingRecipeUrls { get; set; } = [];
}

public sealed class DiscoveryFeedbackRequest
{
    [Required]
    [MinLength(1)]
    public string InstallationId { get; set; } = string.Empty;

    public string? HomeId { get; set; }
    public string? CandidateId { get; set; }

    [Required]
    [MinLength(1)]
    public string SourceUrl { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string EventType { get; set; } = string.Empty;
}

public sealed class DiscoveryWeatherContext
{
    public string? Condition { get; set; }
    public double? TemperatureC { get; set; }
    public string? Season { get; set; }
}

public sealed class DiscoveryFeedResponse
{
    public List<DiscoveryFeedSection> Sections { get; set; } = [];
}

public sealed class DiscoveryFeedSection
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<DiscoveryFeedItem> Items { get; set; } = [];
}

public sealed class DiscoveryFeedItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceDomain { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public double? TotalMinutes { get; set; }
    public double? Rating { get; set; }
    public string? Reason { get; set; }
    public List<string> Tags { get; set; } = [];
}
