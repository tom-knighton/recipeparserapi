using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Discovery;

namespace RecipeParser.Infrastructure.Discovery;

public sealed partial class DiscoveryCandidateIngestionService(
    IOptions<DiscoveryOptions> options,
    IDiscoveryStore store,
    HttpClient httpClient,
    ILogger<DiscoveryCandidateIngestionService> logger) : IDiscoveryCandidateIngestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SyncLock = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> UserSourceLastSuccess = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTimeOffset> UserSourceLastFailure = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> SyncCandidates(CancellationToken ct = default) => SyncCandidates(forceCurated: true, forceSources: true, ct);

    public Task<int> SyncCuratedCandidates(CancellationToken ct = default) => SyncCandidates(forceCurated: true, forceSources: false, ct);

    public Task<int> SyncSourceCandidates(CancellationToken ct = default) => SyncCandidates(forceCurated: false, forceSources: true, ct);

    public async Task<int> SyncUserSourceCandidates(IReadOnlyCollection<string> sourceDomains, CancellationToken ct = default)
    {
        if (!options.Value.EnableUserSourceDiscovery || sourceDomains.Count == 0)
            return 0;

        var now = DateTimeOffset.UtcNow;
        var knownDomains = options.Value.Sources
            .Select(s => DiscoveryUrlNormalizer.NormalizeDomain(s.Domain))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domains = sourceDomains
            .Select(NormalizeUserDomain)
            .OfType<string>()
            .Where(domain => !knownDomains.Contains(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(options.Value.MaxUserSourceDomainsPerRefresh, 1, 10))
            .ToList();

        if (domains.Count == 0)
            return 0;

        if (!await SyncLock.WaitAsync(0, ct))
        {
            logger.LogInformation("Discovery user source sync skipped because another sync is already running.");
            return 0;
        }

        try
        {
            var candidates = new List<DiscoveryCandidate>();
            foreach (var domain in domains)
            {
                if (ShouldSkipUserDomain(domain, now))
                    continue;

                try
                {
                    if (!await IsSafePublicDomain(domain, ct))
                    {
                        MarkUserSourceFailure(domain, now);
                        logger.LogInformation("Discovery user source {Domain} skipped because it did not resolve to a public address.", domain);
                        continue;
                    }

                    var source = UserSource(domain);
                    var sourceCandidates = await FetchSource(source, now, ct);
                    if (sourceCandidates.Count == 0)
                    {
                        MarkUserSourceFailure(domain, now);
                        continue;
                    }

                    MarkUserSourceSuccess(domain, now);
                    candidates.AddRange(sourceCandidates);
                    logger.LogInformation(
                        "Discovery user source {Domain} produced {CandidateCount} candidates.",
                        domain,
                        sourceCandidates.Count);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MarkUserSourceFailure(domain, now);
                    logger.LogInformation(ex, "Discovery user source {Domain} failed during candidate sync.", domain);
                }
            }

            candidates = candidates
                .GroupBy(c => c.NormalizedSourceUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.FreshnessScore).First())
                .ToList();

            if (candidates.Count == 0)
                return 0;

            await store.UpsertCandidates(candidates, ct);
            return candidates.Count;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<int> SyncCandidates(bool forceCurated, bool forceSources, CancellationToken ct)
    {
        if (!await SyncLock.WaitAsync(0, ct))
        {
            logger.LogInformation("Discovery candidate sync skipped because another sync is already running.");
            return 0;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var candidates = new List<DiscoveryCandidate>();

            if (forceCurated)
                candidates.AddRange(BuildCuratedCandidates(now));

            if (forceSources)
                candidates.AddRange(await FetchSourceCandidates(now, ct));

            candidates = candidates
                .GroupBy(c => c.NormalizedSourceUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.FreshnessScore).First())
                .ToList();

            if (candidates.Count == 0)
            {
                logger.LogInformation("Discovery candidate sync completed without new candidates.");
                return 0;
            }

            await store.UpsertCandidates(candidates, ct);
            logger.LogInformation("Discovery candidate sync upserted {CandidateCount} candidates.", candidates.Count);
            return candidates.Count;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private List<DiscoveryCandidate> BuildCuratedCandidates(DateTimeOffset now)
    {
        var candidates = options.Value.CuratedCandidates
            .Select(item => ToCandidate(
                item.SourceUrl,
                item.Title,
                item.ImageUrl,
                item.TotalMinutes,
                item.Rating,
                item.RatingCount,
                item.Tags,
                now,
                now,
                0.7))
            .OfType<DiscoveryCandidate>()
            .ToList();

        if (candidates.Count == 0)
            logger.LogInformation("Discovery curated candidate sync skipped because no candidates are configured.");

        return candidates;
    }

    private async Task<List<DiscoveryCandidate>> FetchSourceCandidates(DateTimeOffset now, CancellationToken ct)
    {
        var candidates = new List<DiscoveryCandidate>();
        var sources = options.Value.Sources.Where(s => s.Enabled).ToList();

        foreach (var source in sources)
        {
            try
            {
                var sourceCandidates = await FetchSource(source, now, ct);
                candidates.AddRange(sourceCandidates);
                logger.LogInformation(
                    "Discovery source {SourceName} produced {CandidateCount} candidates.",
                    SourceName(source),
                    sourceCandidates.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discovery source {SourceName} failed during candidate sync.", SourceName(source));
            }
        }

        return candidates;
    }

    private async Task<List<DiscoveryCandidate>> FetchSource(
        DiscoverySourceOptions source,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var maxCandidates = Math.Clamp(source.MaxCandidates ?? options.Value.MaxCandidatesPerSource, 1, 250);
        var maxDetailFetches = Math.Clamp(source.MaxDetailFetches ?? options.Value.MaxDetailFetchesPerSource, 0, maxCandidates);
        var candidates = new List<DiscoveryCandidate>();

        foreach (var feedUrl in source.RssFeeds.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            try
            {
                var xml = await GetString(feedUrl, ct);
                candidates.AddRange(ParseFeed(xml, source, now));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Discovery feed fetch failed for {FeedUrl}.", feedUrl);
            }

            if (candidates.Count >= maxCandidates)
                break;
        }

        if (candidates.Count < maxCandidates)
        {
            foreach (var sitemapUrl in source.SitemapUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                try
                {
                    var urls = await FetchSitemapUrls(sitemapUrl, source, maxCandidates - candidates.Count, ct);
                    candidates.AddRange(urls.Select(url => ToCandidate(url, TitleFromUrl(url), null, null, null, null, source.DefaultTags, now, now, 0.9)).OfType<DiscoveryCandidate>());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Discovery sitemap fetch failed for {SitemapUrl}.", sitemapUrl);
                }

                if (candidates.Count >= maxCandidates)
                    break;
            }
        }

        if (candidates.Count < maxCandidates)
        {
            foreach (var pageUrl in source.IndexPages.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                string html;
                try
                {
                    html = await GetString(pageUrl, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Discovery index page fetch failed for {PageUrl}.", pageUrl);
                    continue;
                }

                foreach (var feedUrl in ExtractAlternateFeeds(html, new Uri(pageUrl), source).Take(3))
                {
                    try
                    {
                        var xml = await GetString(feedUrl, ct);
                        candidates.AddRange(ParseFeed(xml, source, now));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogDebug(ex, "Discovery alternate feed fetch failed for {FeedUrl}.", feedUrl);
                    }

                    if (candidates.Count >= maxCandidates)
                        break;
                }

                var urls = ExtractLinks(html, new Uri(pageUrl), source).Take(maxCandidates - candidates.Count);
                candidates.AddRange(urls.Select(url => ToCandidate(url, TitleFromUrl(url), null, null, null, null, source.DefaultTags, now, now, 0.8)).OfType<DiscoveryCandidate>());
                if (candidates.Count >= maxCandidates)
                    break;
            }
        }

        candidates = candidates
            .GroupBy(c => c.NormalizedSourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(maxCandidates)
            .ToList();

        for (var i = 0; i < Math.Min(maxDetailFetches, candidates.Count); i++)
        {
            candidates[i] = await EnrichFromRecipePage(candidates[i], source, now, ct);
        }

        return candidates
            .Where(c => IsAllowedCandidate(c.SourceUrl, source) &&
                        (!source.RequireRecipeMetadata || c.Tags.Contains("recipe-metadata", StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task<List<string>> FetchSitemapUrls(
        string sitemapUrl,
        DiscoverySourceOptions source,
        int limit,
        CancellationToken ct)
    {
        var urls = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(sitemapUrl);

        while (pending.Count > 0 && urls.Count < limit)
        {
            var current = pending.Dequeue();
            var xml = await GetString(current, ct);
            var document = XDocument.Parse(xml, LoadOptions.None);
            var rootName = document.Root?.Name.LocalName;

            if (rootName?.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase) == true)
            {
                foreach (var loc in document.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).Where(IsAbsoluteHttpUrl).Take(12))
                    pending.Enqueue(loc);
            }
            else
            {
                urls.AddRange(document.Descendants()
                    .Where(e => e.Name.LocalName == "loc")
                    .Select(e => e.Value.Trim())
                    .Where(url => IsAbsoluteHttpUrl(url) && IsAllowedCandidate(url, source))
                    .Take(limit - urls.Count));
            }
        }

        return urls;
    }

    private async Task<DiscoveryCandidate> EnrichFromRecipePage(
        DiscoveryCandidate candidate,
        DiscoverySourceOptions source,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var html = await GetString(candidate.SourceUrl, ct);
            var recipe = JsonLdRecipeMetadata.FromHtml(html);
            if (recipe is null)
                return candidate;

            return ToCandidate(
                recipe.Url ?? candidate.SourceUrl,
                recipe.Title ?? candidate.Title,
                recipe.ImageUrl ?? candidate.ImageUrl,
                recipe.TotalMinutes ?? candidate.TotalMinutes,
                recipe.Rating ?? candidate.Rating,
                recipe.RatingCount ?? candidate.RatingCount,
                source.DefaultTags.Concat(recipe.Tags).Append("recipe-metadata").ToList(),
                candidate.FirstSeenAt == default ? now : candidate.FirstSeenAt,
                now,
                candidate.FreshnessScore) ?? candidate;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Discovery detail enrichment failed for {SourceUrl}.", candidate.SourceUrl);
            return candidate;
        }
    }

    private async Task<string> GetString(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.SourceRequestTimeoutSeconds, 3, 60)));
        return await httpClient.GetStringAsync(url, timeout.Token);
    }

    private static IEnumerable<DiscoveryCandidate> ParseFeed(
        string xml,
        DiscoverySourceOptions source,
        DateTimeOffset now)
    {
        var document = XDocument.Parse(xml, LoadOptions.None);
        var entries = document.Descendants().Where(e => e.Name.LocalName is "item" or "entry");

        foreach (var entry in entries)
        {
            var title = HtmlDecode(entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value);
            var link = FeedLink(entry);
            if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(title) || !IsAllowedCandidate(link, source))
                continue;

            var published = ParseDate(
                entry.Elements().FirstOrDefault(e => e.Name.LocalName is "pubDate" or "published" or "updated")?.Value) ?? now;
            var image = FeedImage(entry);
            var categories = entry.Elements()
                .Where(e => e.Name.LocalName == "category")
                .Select(e => HtmlDecode(e.Value))
                .Where(t => !string.IsNullOrWhiteSpace(t));

            yield return ToCandidate(
                link,
                title,
                image,
                null,
                null,
                null,
                source.DefaultTags.Concat(categories).ToList(),
                published,
                now,
                FreshnessFromDate(published, now))!;
        }
    }

    private static string? FeedLink(XElement entry)
    {
        var explicitLink = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link")?.Value;
        if (IsAbsoluteHttpUrl(explicitLink))
            return explicitLink!.Trim();

        return entry.Elements()
            .Where(e => e.Name.LocalName == "link")
            .Select(e => e.Attribute("href")?.Value)
            .FirstOrDefault(IsAbsoluteHttpUrl)
            ?.Trim();
    }

    private static string? FeedImage(XElement entry)
    {
        var media = entry.Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "content" or "thumbnail" && IsAbsoluteHttpUrl(e.Attribute("url")?.Value))
            ?.Attribute("url")?.Value;
        if (IsAbsoluteHttpUrl(media))
            return media!.Trim();

        var enclosure = entry.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "enclosure" && e.Attribute("type")?.Value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            ?.Attribute("url")?.Value;
        if (IsAbsoluteHttpUrl(enclosure))
            return enclosure!.Trim();

        var embeddedHtml = entry.Elements()
            .Where(e => e.Name.LocalName is "description" or "encoded" or "summary" or "content")
            .Select(e => e.Value)
            .FirstOrDefault(value => ImageSrcRegex().IsMatch(value));
        var embedded = embeddedHtml is null ? null : ImageSrcRegex().Match(embeddedHtml).Groups["src"].Value;
        return IsAbsoluteHttpUrl(embedded) ? HtmlDecode(embedded) : null;
    }

    private static IEnumerable<string> ExtractLinks(string html, Uri pageUri, DiscoverySourceOptions source)
    {
        foreach (Match match in HrefRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!Uri.TryCreate(pageUri, href, out var url))
                continue;

            var absolute = url.GetLeftPart(UriPartial.Path);
            if (IsAllowedCandidate(absolute, source))
                yield return absolute;
        }
    }

    private static IEnumerable<string> ExtractAlternateFeeds(string html, Uri pageUri, DiscoverySourceOptions source)
    {
        foreach (Match match in AlternateFeedRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!Uri.TryCreate(pageUri, href, out var url))
                continue;

            var absolute = url.GetLeftPart(UriPartial.Path);
            if (IsAbsoluteHttpUrl(absolute) && IsAllowedHost(absolute, source))
                yield return absolute;
        }
    }

    private static DiscoveryCandidate? ToCandidate(
        string sourceUrl,
        string? title,
        string? imageUrl,
        double? totalMinutes,
        double? rating,
        int? ratingCount,
        IEnumerable<string> tags,
        DateTimeOffset firstSeen,
        DateTimeOffset lastSeen,
        double freshnessScore)
    {
        var normalizedUrl = DiscoveryUrlNormalizer.NormalizeUrl(sourceUrl);
        var domain = DiscoveryUrlNormalizer.DomainFromUrl(sourceUrl);

        if (normalizedUrl is null ||
            domain is null ||
            string.IsNullOrWhiteSpace(title))
            return null;

        return new DiscoveryCandidate
        {
            SourceUrl = sourceUrl.Trim(),
            NormalizedSourceUrl = normalizedUrl,
            SourceDomain = domain,
            Title = HtmlDecode(title),
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) || !IsAbsoluteHttpUrl(imageUrl) ? null : imageUrl.Trim(),
            TotalMinutes = totalMinutes,
            Rating = rating,
            RatingCount = ratingCount,
            Tags = tags.SelectMany(SplitTags)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length is > 1 and < 48)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToList(),
            FirstSeenAt = ToUtcOffset(firstSeen),
            LastSeenAt = ToUtcOffset(lastSeen),
            FreshnessScore = Math.Clamp(freshnessScore, 0.05, 1.5)
        };
    }

    private static IEnumerable<string> SplitTags(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            yield break;

        foreach (var value in tag.Split([',', '|', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            yield return value;
    }

    private static bool IsAllowedCandidate(string? url, DiscoverySourceOptions source)
    {
        if (!IsAbsoluteHttpUrl(url))
            return false;

        if (!IsAllowedHost(url!, source))
            return false;

        return source.UrlMustContain.Count == 0 ||
               source.UrlMustContain.Any(part => url!.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedHost(string url, DiscoverySourceOptions source)
    {
        var domain = DiscoveryUrlNormalizer.DomainFromUrl(url!);
        if (!string.IsNullOrWhiteSpace(source.Domain) &&
            !string.Equals(domain, DiscoveryUrlNormalizer.NormalizeDomain(source.Domain), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private bool ShouldSkipUserDomain(string domain, DateTimeOffset now)
    {
        if (UserSourceLastSuccess.TryGetValue(domain, out var lastSuccess) &&
            now - lastSuccess < TimeSpan.FromHours(Math.Clamp(options.Value.UserSourceSuccessCooldownHours, 1, 168)))
            return true;

        return UserSourceLastFailure.TryGetValue(domain, out var lastFailure) &&
               now - lastFailure < TimeSpan.FromHours(Math.Clamp(options.Value.UserSourceFailureCooldownHours, 1, 168));
    }

    private static void MarkUserSourceSuccess(string domain, DateTimeOffset now)
    {
        UserSourceLastSuccess[domain] = now;
        UserSourceLastFailure.Remove(domain);
    }

    private static void MarkUserSourceFailure(string domain, DateTimeOffset now)
    {
        UserSourceLastFailure[domain] = now;
    }

    private DiscoverySourceOptions UserSource(string domain)
    {
        var maxCandidates = Math.Clamp(options.Value.MaxCandidatesPerUserSource, 1, 60);
        var maxDetails = Math.Clamp(options.Value.MaxDetailFetchesPerUserSource, 1, maxCandidates);

        return new DiscoverySourceOptions
        {
            Name = $"User source {domain}",
            Domain = domain,
            RssFeeds =
            [
                $"https://www.{domain}/feed/",
                $"https://{domain}/feed/",
                $"https://www.{domain}/rss/",
                $"https://{domain}/rss/",
                $"https://www.{domain}/atom.xml",
                $"https://{domain}/atom.xml",
                $"https://www.{domain}/feed.xml",
                $"https://{domain}/feed.xml"
            ],
            SitemapUrls =
            [
                $"https://www.{domain}/sitemap.xml",
                $"https://{domain}/sitemap.xml"
            ],
            IndexPages =
            [
                $"https://www.{domain}/",
                $"https://{domain}/"
            ],
            DefaultTags = ["from-your-sites"],
            MaxCandidates = maxCandidates,
            MaxDetailFetches = maxDetails,
            RequireRecipeMetadata = true
        };
    }

    private static string? NormalizeUserDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
            return DiscoveryUrlNormalizer.DomainFromUrl(trimmed);

        if (trimmed.Contains('/', StringComparison.Ordinal) ||
            trimmed.Contains('\\', StringComparison.Ordinal) ||
            trimmed.Contains('@', StringComparison.Ordinal) ||
            trimmed.Contains(':', StringComparison.Ordinal))
            return null;

        return DiscoveryUrlNormalizer.NormalizeDomain(trimmed);
    }

    private static async Task<bool> IsSafePublicDomain(string domain, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(domain, ct);
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal &&
                   !address.IsIPv6Multicast &&
                   !address.IsIPv6SiteLocal &&
                   !address.Equals(IPAddress.IPv6Loopback) &&
                   !address.Equals(IPAddress.IPv6None);
        }

        return false;
    }

    private static string TitleFromUrl(string url)
    {
        var path = new Uri(url).AbsolutePath.Trim('/');
        var last = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Recipe";
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(last.Replace('-', ' ').Replace('_', ' '));
    }

    private static string HtmlDecode(string? value) => WebUtility.HtmlDecode(value ?? string.Empty).Trim();

    private static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? ToUtcOffset(parsed)
            : null;

    private static DateTimeOffset ToUtcOffset(DateTimeOffset value) => value.ToUniversalTime();

    private static double FreshnessFromDate(DateTimeOffset published, DateTimeOffset now)
    {
        published = ToUtcOffset(published);
        now = ToUtcOffset(now);
        var ageDays = Math.Max(0, (now - published).TotalDays);
        return 0.5 + Math.Exp(-ageDays / 21);
    }

    private static string SourceName(DiscoverySourceOptions source) =>
        string.IsNullOrWhiteSpace(source.Name) ? source.Domain : source.Name;

    [GeneratedRegex("""href=["'](?<href>[^"'#]+)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("""<img[^>]+src=["'](?<src>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ImageSrcRegex();

    [GeneratedRegex("""<link[^>]+(?:type=["']application/(?:rss|atom)\+xml["'][^>]+href=["'](?<href>[^"']+)["']|href=["'](?<href>[^"']+)["'][^>]+type=["']application/(?:rss|atom)\+xml["'])""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AlternateFeedRegex();

    private sealed partial record JsonLdRecipeMetadata(
        string? Url,
        string? Title,
        string? ImageUrl,
        double? TotalMinutes,
        double? Rating,
        int? RatingCount,
        List<string> Tags)
    {
        public static JsonLdRecipeMetadata? FromHtml(string html)
        {
            foreach (Match match in JsonLdRegex().Matches(html))
            {
                var json = WebUtility.HtmlDecode(match.Groups["json"].Value);
                using var document = JsonDocument.Parse(json);
                var recipe = FindRecipe(document.RootElement);
                if (recipe.HasValue)
                    return FromElement(recipe.Value);
            }

            return null;
        }

        private static JsonLdRecipeMetadata FromElement(JsonElement element)
        {
            var tags = new List<string>();
            tags.AddRange(ReadStringArray(element, "keywords"));
            tags.AddRange(ReadStringArray(element, "recipeCategory"));
            tags.AddRange(ReadStringArray(element, "recipeCuisine"));

            var rating = ReadProperty(element, "aggregateRating");

            return new JsonLdRecipeMetadata(
                ReadString(element, "url") ?? ReadString(element, "mainEntityOfPage"),
                ReadString(element, "name") ?? ReadString(element, "headline"),
                ReadImage(element),
                ParseDuration(ReadString(element, "totalTime") ?? ReadString(element, "cookTime") ?? ReadString(element, "prepTime")),
                ReadDouble(rating, "ratingValue"),
                ReadInt(rating, "ratingCount") ?? ReadInt(rating, "reviewCount"),
                tags);
        }

        private static JsonElement? FindRecipe(JsonElement element)
        {
            if (IsRecipe(element))
                return element;

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var recipe = FindRecipe(item);
                    if (recipe.HasValue)
                        return recipe;
                }
            }

            if (ReadProperty(element, "@graph") is { } graph)
                return FindRecipe(graph);

            return null;
        }

        private static bool IsRecipe(JsonElement element)
        {
            if (ReadProperty(element, "@type") is not { } type)
                return false;

            return type.ValueKind switch
            {
                JsonValueKind.String => type.GetString()?.Contains("Recipe", StringComparison.OrdinalIgnoreCase) == true,
                JsonValueKind.Array => type.EnumerateArray().Any(item => item.GetString()?.Contains("Recipe", StringComparison.OrdinalIgnoreCase) == true),
                _ => false
            };
        }

        private static string? ReadImage(JsonElement element)
        {
            if (ReadProperty(element, "image") is not { } image)
                return null;

            return image.ValueKind switch
            {
                JsonValueKind.String => image.GetString(),
                JsonValueKind.Array => image.EnumerateArray().Select(ReadImageValue).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                JsonValueKind.Object => ReadImageValue(image),
                _ => null
            };
        }

        private static string? ReadImageValue(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object
                ? ReadString(element, "url")
                : element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        private static List<string> ReadStringArray(JsonElement element, string propertyName)
        {
            if (ReadProperty(element, propertyName) is not { } property)
                return [];

            return property.ValueKind switch
            {
                JsonValueKind.String => [property.GetString() ?? string.Empty],
                JsonValueKind.Array => property.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList(),
                _ => []
            };
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            if (ReadProperty(element, propertyName) is not { } property)
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Object => ReadString(property, "@id") ?? ReadString(property, "url"),
                _ => null
            };
        }

        private static double? ReadDouble(JsonElement? element, string propertyName)
        {
            if (element is not { } value || ReadProperty(value, propertyName) is not { } property)
                return null;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
                return number;

            return double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        private static int? ReadInt(JsonElement? element, string propertyName)
        {
            if (element is not { } value || ReadProperty(value, propertyName) is not { } property)
                return null;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
                return number;

            return int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        private static JsonElement? ReadProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName))
                    return property.Value;
            }

            return null;
        }

        private static double? ParseDuration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return XmlConvert.ToTimeSpan(value).TotalMinutes;
            }
            catch
            {
                return null;
            }
        }

        [GeneratedRegex("""<script[^>]+type=["']application/ld\+json["'][^>]*>(?<json>.*?)</script>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
        private static partial Regex JsonLdRegex();
    }
}
