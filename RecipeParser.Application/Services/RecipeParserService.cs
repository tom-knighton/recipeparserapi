using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using AngleSharp;
using Jering.Javascript.NodeJS;
using Microsoft.Extensions.Logging;
using RecipeParser.Domain.Exceptions;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Domain.Models;

namespace RecipeParser.Application.Services;

public partial class RecipeParserService(
    INodeJSService node,
    IPageFetcher fetcher,
    IRecipeDescriptionParser? descriptionParser = null,
    ILogger<RecipeParserService>? logger = null): IRecipeParserService
{
    public async Task<Recipe> ParseRecipeByUrl(string url)
    {
        Recipe? recipe = null;
        if (url.StartsWith("https://www.instagram.com/reel/") || url.StartsWith("https://www.tiktok.com/@"))
        {
            recipe = await TryParseSocialPost(url);
        }
        else
        {
            recipe = await TryParseSchema(url);
        }

        if (recipe is null)
            throw new NoRecipeFoundException();

        var ingredientTasks = recipe.RawIngredients
            .Select(async ingredient =>
            {
                var result = await node.InvokeFromFileAsync<IngredientResult>(
                    "recipeParser.cjs",
                    exportName: "parseIngredientLine",
                    args: new object?[] { ingredient, "en", new { } }
                );
                return (ingredient, result);
            })
            .ToList();

        var ingredientResults = await Task.WhenAll(ingredientTasks);
        foreach (var (_, result) in ingredientResults)
        {
            if (result is not null)
                recipe.Ingredients.Add(result);
        }

        var stepTasks = new List<Task<(RecipeStep step, InstructionResult? result)>>();
        foreach (var stepSection in recipe.StepSections ?? [])
        {
            foreach (var step in stepSection.Steps ?? [])
            {
                var stepCopy = step;
                stepTasks.Add(
                    node.InvokeFromFileAsync<InstructionResult>(
                        "recipeParser.cjs",
                        exportName: "parseInstructionLine",
                        args: new object?[] { step.Step, "en", new { } }
                    ).ContinueWith(t => (stepCopy, t.Result))
                );
            }
        }

        var stepResults = await Task.WhenAll(stepTasks);
        foreach (var (step, result) in stepResults)
        {
            if (result?.TimeItems.Length > 0)
            {
                step.Times = result.TimeItems.Select(t => new RecipeStepTime
                {
                    TimeInSeconds = t.TimeInSeconds,
                    TimeText = t.TimeText,
                    TimeUnitText = t.TimeUnitText
                }).ToList();
            }

            if (result?.Temperature is not null and not 0)
            {
                step.Temperatures.Add(new RecipeStepTemperature()
                {
                    Temperature = result.Temperature ?? 0,
                    TemperatureText = result.TemperatureText ?? "",
                    TemperatureUnitText = result.TemperatureUnitText ?? ""
                });
            }
        }
        
        return recipe;
    }

    private async Task<Recipe?> TryParseSchema(string url)
    {
        var sourceUri = Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ? parsedUri : null;
        if (sourceUri is not null)
        {
            var apiRecipe = await TryParseTasteOfHomeApi(sourceUri);
            if (apiRecipe is not null)
                return apiRecipe;
        }

        HttpRequestException? staticFetchException = null;

        try
        {
            var staticHtml = await fetcher.GetStaticHtmlAsync(url);
            var staticScripts = ExtractLdJsonStrings(staticHtml);

            var recipeNodes = CollectRecipeNodes(staticScripts);
            if (IsComplete(recipeNodes))
                return MapRecipeFromSchema(MergeRecipeNodes(recipeNodes), url);
        }
        catch (HttpRequestException ex) when (ShouldTryRenderedFallback(ex.StatusCode))
        {
            logger?.LogInformation(
                ex,
                "Static recipe fetch failed for {Url} with {StatusCode}; trying rendered JSON-LD fallback.",
                url,
                ex.StatusCode);
            staticFetchException = ex;
        }

        List<JsonObject> renderedNodes;
        try
        {
            logger?.LogInformation("Trying rendered JSON-LD fallback for {Url}.", url);
            var renderedScripts = await fetcher.GetRenderedJsonLdAsync(url, timeout: TimeSpan.FromSeconds(15));
            renderedNodes = CollectRecipeNodes(renderedScripts);
        }
        catch (Exception ex) when (staticFetchException is not null)
        {
            logger?.LogWarning(
                ex,
                "Rendered JSON-LD fallback failed for {Url}; returning original static fetch failure {StatusCode}.",
                url,
                staticFetchException.StatusCode);
            throw staticFetchException;
        }

        if (renderedNodes.Count > 0)
        {
            logger?.LogInformation("Rendered JSON-LD fallback found {RecipeNodeCount} recipe node(s) for {Url}.", renderedNodes.Count, url);
            return MapRecipeFromSchema(MergeRecipeNodes(renderedNodes), url);
        }

        if (staticFetchException is not null)
        {
            logger?.LogInformation(
                "Rendered JSON-LD fallback found no recipe nodes for {Url}; returning original static fetch failure {StatusCode}.",
                url,
                staticFetchException.StatusCode);
            throw staticFetchException;
        }

        return null;
    }

    private async Task<Recipe?> TryParseTasteOfHomeApi(Uri sourceUri)
    {
        if (!IsTasteOfHomeRecipeUrl(sourceUri, out var slug))
            return null;

        var apiUrl = $"https://www.tasteofhome.com/wp-json/wp/v2/recipe?slug={Uri.EscapeDataString(slug)}";
        try
        {
            logger?.LogInformation("Trying Taste of Home recipe API {ApiUrl} for {Url}.", apiUrl, sourceUri);
            var json = await fetcher.GetStaticHtmlAsync(apiUrl);
            var root = JsonNode.Parse(json);
            var post = root is JsonArray posts ? posts.OfType<JsonObject>().FirstOrDefault() : root as JsonObject;
            if (post?["recipe_schema"] is not JsonObject schema)
            {
                logger?.LogInformation("Taste of Home recipe API returned no recipe_schema for {Url}.", sourceUri);
                return null;
            }

            var recipe = MapRecipeFromSchema(schema, sourceUri.ToString());
            if (recipe is null)
                logger?.LogInformation("Taste of Home recipe API returned recipe_schema that could not be mapped for {Url}.", sourceUri);
            else
                logger?.LogInformation("Taste of Home recipe API parsed recipe for {Url}.", sourceUri);

            return recipe;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger?.LogInformation(ex, "Taste of Home recipe API failed for {ApiUrl}; falling back to page parsing.", apiUrl);
            return null;
        }
    }

    private static bool IsTasteOfHomeRecipeUrl(Uri uri, out string slug)
    {
        slug = "";
        if (!uri.Host.EndsWith("tasteofhome.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("recipes", StringComparison.OrdinalIgnoreCase))
            return false;

        slug = segments[1];
        return !string.IsNullOrWhiteSpace(slug);
    }

    private static bool ShouldTryRenderedFallback(HttpStatusCode? statusCode)
    {
        return statusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.NotAcceptable
            or HttpStatusCode.TooManyRequests;
    }

    private async Task<Recipe?> TryParseSocialPost(string url)
    {
        var staticHtml = await fetcher.GetStaticHtmlAsync(url);
        var post = ExtractSocialPost(staticHtml, url);

        foreach (var recipeUrl in GetPotentialRecipeUrls(post, url))
        {
            try
            {
                var linkedRecipe = await TryParseSchema(recipeUrl);
                if (linkedRecipe is not null)
                    return linkedRecipe;
            }
            catch
            {
                // Social posts often include dead or tracking-wrapped links. Keep falling back.
            }
        }

        if (descriptionParser is null || string.IsNullOrWhiteSpace(post.Description))
            return null;

        return await descriptionParser.ParseRecipeFromDescription(post.Description, url);
    }
    
    private static IReadOnlyList<string> ExtractLdJsonStrings(string html)
    {
        var list = new List<string>();
        var doc = AngleSharp.BrowsingContext.New(AngleSharp.Configuration.Default)
            .OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
        foreach (var s in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var txt = s.TextContent;
            if (!string.IsNullOrWhiteSpace(txt)) list.Add(txt);
        }
        return list;
    }

    private static SocialPost ExtractSocialPost(string html, string sourceUrl)
    {
        var doc = AngleSharp.BrowsingContext.New(AngleSharp.Configuration.Default)
            .OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

        var description =
            ExtractCaptionFromMeta(doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"))
            ?? ExtractCaptionFromMeta(doc.QuerySelector("meta[name='twitter:title']")?.GetAttribute("content"))
            ?? ExtractCaptionFromMeta(doc.QuerySelector("meta[property='twitter:title']")?.GetAttribute("content"))
            ?? ExtractCaptionFromMeta(doc.QuerySelector("meta[property='og:description']")?.GetAttribute("content"))
            ?? ExtractCaptionFromMeta(doc.QuerySelector("meta[name='twitter:description']")?.GetAttribute("content"))
            ?? ExtractCaptionFromMeta(doc.QuerySelector("meta[property='twitter:description']")?.GetAttribute("content"))
            ?? ExtractCaptionFromMeta(doc.QuerySelector("meta[name='description']")?.GetAttribute("content"))
            ?? doc.QuerySelector("meta[property='og:description']")?.GetAttribute("content")
            ?? doc.QuerySelector("meta[name='twitter:description']")?.GetAttribute("content")
            ?? doc.QuerySelector("meta[property='twitter:description']")?.GetAttribute("content")
            ?? doc.QuerySelector("meta[itemprop='description']")?.GetAttribute("content")
            ?? doc.QuerySelector("[itemprop='caption']")?.TextContent
            ?? doc.QuerySelector("figcaption")?.TextContent
            ?? doc.QuerySelector("meta[name='description']")?.GetAttribute("content")
            ?? ExtractDescriptionFromJsonLd(doc);

        description = NormalizeSocialDescription(description);

        var links = new List<SocialPostLink>();
        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (Uri.TryCreate(new Uri(sourceUrl), href, out var uri))
                links.Add(new SocialPostLink(uri.ToString(), anchor.TextContent?.Trim() ?? ""));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            foreach (Match match in UrlRegex().Matches(description))
                links.Add(new SocialPostLink(match.Value.TrimEnd('.', ',', ')'), ""));
        }

        return new SocialPost(description, links);
    }

    private static string? ExtractCaptionFromMeta(string? value)
    {
        value = NormalizeSocialDescription(value);
        if (value is null)
            return null;

        var quotedCaption = InstagramQuotedCaptionRegex().Match(value);
        if (quotedCaption.Success)
            return NormalizeSocialDescription(quotedCaption.Groups["caption"].Value);

        return null;
    }

    private static string? ExtractDescriptionFromJsonLd(AngleSharp.Dom.IDocument doc)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            foreach (var node in ExpandCandidates(script.TextContent))
            {
                var description = node["caption"]?.ToString()
                                  ?? node["description"]?.ToString()
                                  ?? node["text"]?.ToString();

                description = NormalizeSocialDescription(description);
                if (description is not null)
                    return description;
            }
        }

        return null;
    }

    private static string? NormalizeSocialDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var cleaned = Regex.Replace(description, @"\s+", " ").Trim();
        if (IsSocialBoilerplate(cleaned))
            return null;

        return IsSocialBoilerplate(cleaned) ? null : cleaned;
    }

    private static bool IsSocialBoilerplate(string value)
    {
        var normalized = value.Trim().TrimEnd('.', '|').Trim();
        return normalized.Equals("Instagram", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("TikTok", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("TikTok - Make Your Day", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("Threads", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("X", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetPotentialRecipeUrls(SocialPost post, string sourceUrl)
    {
        var sourceHost = new Uri(sourceUrl).Host;

        return post.Links
            .Where(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
                           && !IsSocialHost(uri.Host)
                           && !uri.Host.Equals(sourceHost, StringComparison.OrdinalIgnoreCase)
                           && LooksLikeRecipeLink(uri, link.Text))
            .Select(link => link.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRecipeLink(Uri uri, string linkText)
    {
        var haystack = $"{uri.Host} {uri.AbsolutePath} {linkText}".ToLowerInvariant();
        return haystack.Contains("recipe")
               || haystack.Contains("recipes")
               || haystack.Contains("ingredients")
               || haystack.Contains("method")
               || haystack.Contains("cook");
    }

    private static bool IsSocialHost(string host)
    {
        var lowerHost = host.ToLowerInvariant();
        return lowerHost.Contains("instagram.")
               || lowerHost.Contains("tiktok.")
               || lowerHost.Contains("facebook.")
               || lowerHost.Contains("threads.")
               || lowerHost.Contains("twitter.")
               || lowerHost.Contains("x.com")
               || lowerHost.Contains("youtube.")
               || lowerHost.Contains("youtu.be");
    }

    private static List<JsonObject> CollectRecipeNodes(IEnumerable<string> scripts)
    {
        var acc = new List<JsonObject>();
        foreach (var json in scripts)
        foreach (var node in ExpandCandidates(json))
            if (IsRecipeNode(node)) acc.Add(node);
        return acc;
    }

    private static bool IsComplete(List<JsonObject> nodes)
    {
        if (nodes.Count == 0) return false;
        var merged = MergeRecipeNodes(nodes);
        return merged.ContainsKey("comment") && (merged.ContainsKey("recipeIngredient") || merged.ContainsKey("aggregateRating"));
    }
    
    private static JsonObject MergeRecipeNodes(List<JsonObject> nodes)
    {
        if (nodes.Count == 1) return nodes[0];
        var merged = new JsonObject();
        foreach (var node in nodes)
        {
            foreach (var prop in node)
            {
                merged[prop.Key] = prop.Value?.Deserialize<JsonNode>();
            }
        }
        return merged;
    }
    
    static IEnumerable<JsonObject> ExpandCandidates(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { yield break; }

        if (root is JsonArray arr)
        {
            foreach (var n in arr.SelectMany(Flatten)) if (n is not null) yield return n!;
        }
        else
        {
            foreach (var n in Flatten(root)) if (n is not null) yield return n!;
        }
    }
    
    static bool IsRecipeNode(JsonObject node, string? previousRecipeId = null)
    {
        var id = node.TryGetPropertyValue("@id", out var idNode) && idNode is JsonValue idValue ? idValue.ToString() : null;
        var hasType = node.TryGetPropertyValue("@type", out var t);
        var hasValidId = id is null || previousRecipeId is null || id.Equals(previousRecipeId, StringComparison.OrdinalIgnoreCase);

        if (!hasType && !hasValidId) return false;

        if (t is not null)
        {
            if (t is JsonValue v && v.TryGetValue<string>(out var typeStr) && typeStr.Equals("Recipe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        
            if (t is JsonArray a && a.Any(x => x is JsonValue v && v.TryGetValue<string>(out var s) && s.Equals("Recipe", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }


        if (hasValidId)
        {
            return true;
        }

        return false;
    }
    
    static IEnumerable<JsonObject?> Flatten(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            if (obj.TryGetPropertyValue("@graph", out var g) && g is JsonArray ga)
                foreach (var n in ga.SelectMany(Flatten)) yield return n;
        }
        else if (node is JsonArray arr)
        {
            foreach (var n in arr.SelectMany(Flatten)) yield return n;
        }
    }
    
    
    static Recipe? MapRecipeFromSchema(JsonObject r, string url)
    {
        var title = r["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var author = r["author"] switch
        {
            JsonObject o => o["name"]?.ToString(),
            JsonArray a => a.Select(x => x?["name"]?.ToString()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            JsonValue v => v.ToString(),
            _ => null
        };
        
        var desc = r["description"]?.ToString();

        var yield = r["recipeYield"] switch
        {
            JsonArray a => a.FirstOrDefault()?.ToString(),
            JsonValue v => v.ToString(),
            _ => null
        };

        var ingredients = r["recipeIngredient"] is JsonArray ingrArr
            ? ingrArr.Select(x => x?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
            : Array.Empty<string>();

        var instructions = ExtractInstructions(r);

        var prep = ParseIsoDuration(r["prepTime"]?.ToString());
        var cook = ParseIsoDuration(r["cookTime"]?.ToString());
        var total = ParseIsoDuration(r["totalTime"]?.ToString()) ?? (prep.HasValue || cook.HasValue ? (prep ?? TimeSpan.Zero) + (cook ?? TimeSpan.Zero) : null);

        var categories = r["recipeCategory"] switch
        {
            JsonArray a => a.Select(x => x?.ToString()),
            JsonValue v => v.ToString().Split(","),
            _ => []
        };
        
        var cuisines = r["recipeCuisine"] switch
        {
            JsonArray a => a.Select(x => x?.ToString()),
            JsonValue v => v.ToString().Split(","),
            _ => []
        };
        
        var keywords = r["keywords"] switch
        {
            JsonArray a => a.Select(x => x?.ToString()),
            JsonValue v => v.ToString().Split(","),
            _ => []
        };
        
        var id = r["@id"]?.ToString();
        
        var images = ExtractImages(r);
        
        var reviews = new List<RecipeReviews>();
        switch (r["review"])
        {
            case JsonArray reviewArr:
            {
                foreach (var reviewNode in reviewArr)
                {
                    if (reviewNode is not JsonObject reviewObj) continue;
                    var text = reviewObj["reviewBody"]?.ToString();
                    int? rating = null;
                    if (reviewObj["reviewRating"] is JsonObject ratingObj && ratingObj["ratingValue"] != null)
                    {
                        if (int.TryParse(ratingObj["ratingValue"]?.ToString(), out var parsedRating))
                        {
                            rating = parsedRating;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(text))
                        reviews.Add(new RecipeReviews { Text = text, Rating = rating });
                }

                break;
            }
            case JsonObject singleReviewObj:
            {
                var text = singleReviewObj["reviewBody"]?.ToString();
                int? rating = null;
                if (singleReviewObj["reviewRating"] is JsonObject ratingObj && ratingObj["ratingValue"] != null)
                {
                    if (int.TryParse(ratingObj["ratingValue"]?.ToString(), out var parsedRating))
                    {
                        rating = parsedRating;
                    }
                }
                if (!string.IsNullOrWhiteSpace(text))
                    reviews.Add(new RecipeReviews { Text = text, Rating = rating });
                break;
            }
        }
        
        switch (r["comment"])
        {
            case JsonArray commentArr:
            {
                foreach (var commentNode in commentArr)
                {
                    if (commentNode is not JsonObject commentObj) continue;
                    var text = commentObj["text"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        reviews.Add(new RecipeReviews { Text = text, Rating = null });
                }
                break;
            }
            case JsonObject singleCommentObj:
            {
                var text = singleCommentObj["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    reviews.Add(new RecipeReviews { Text = text, Rating = null });
                break;
            }
        }

        double? aggregateRating = r["aggregateRating"] switch
        {
            JsonObject o when o["ratingValue"] is JsonValue v => double.TryParse(v.ToString(), out var rVal) ? rVal : null,
            JsonValue v => double.TryParse(v.ToString(), out var rVal) ? rVal : null,
            _ => null
        };
        
        int? totalRatings = r["aggregateRating"] switch
        {
            JsonObject o when o["ratingCount"] is JsonValue v && int.TryParse(v.ToString(), out var rCount) => rCount,
            JsonObject o when o["reviewCount"] is JsonValue v && int.TryParse(v.ToString(), out var rCount) => rCount,
            JsonValue v when int.TryParse(v.ToString(), out var rCount) => rCount,
            _ => null
        };

        return new Recipe
        {
            Title = title,
            Description = desc,
            ImageUrl = images.FirstOrDefault()?.ToString(),
            Author = author,
            MinutesToCook = cook?.TotalMinutes,
            MinutesToPrepare = prep?.TotalMinutes,
            TotalMins = total?.TotalMinutes,
            Serves = yield,
            RawIngredients = ingredients,
            Tags = categories
                .Concat(cuisines)
                .Concat(keywords)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList(),
            Url = id ?? url,
            StepSections = instructions.ToList(),
            Ratings = new  RecipeRatings
            {
                OverallRating = aggregateRating,
                TotalRatings = totalRatings ?? reviews.Count,
                Reviews = reviews.Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(r => new RecipeReviews { Text = r.Text, Rating = r.Rating }).ToList()
            }
        };
    }

    static IReadOnlyList<RecipeStepSection> ExtractInstructions(JsonObject r)
    {
        var steps = new List<RecipeStepSection>();
        if (r["recipeInstructions"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                var step = new RecipeStepSection();
                step.Steps = [];
                if (n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    step.Steps = [new RecipeStep { Step = s.Trim() }];
                else if (n is JsonObject o)
                {
                    if (o["text"] is JsonNode t && !string.IsNullOrWhiteSpace(t.ToString()))
                        step.Steps.Add(new RecipeStep { Step = t.ToString().Trim()});
                    else if (o["itemListElement"] is JsonArray inner)
                        step.Steps.AddRange(inner.SelectMany(x =>
                            x is JsonObject io && io["text"] is JsonNode it && !string.IsNullOrWhiteSpace(it.ToString())
                                ? new[] { new RecipeStep { Step = it.ToString().Trim() } }
                                : []));
                }
                steps.Add(step);
            }
            
            if (steps.All(s => string.IsNullOrWhiteSpace(s.Title)))
            {
                steps =
                [
                    new RecipeStepSection
                    {
                        Steps = steps.SelectMany(s => s.Steps ?? []).ToList(),
                    }
                ];
            }
            return steps;
        }
        
        return [];
    }

    static IReadOnlyList<Uri> ExtractImages(JsonObject r)
    {
        var acc = new List<Uri>();
        switch (r["image"])
        {
            case JsonObject o when o.TryGetPropertyValue("url", out var url) && url is JsonValue v && v.TryGetValue<string>(out var s) && Uri.TryCreate(s, UriKind.Absolute, out var u):
                acc.Add(u);
                break;
            case JsonValue v when v.TryGetValue<string>(out var s) && Uri.TryCreate(s, UriKind.Absolute, out var u):
                acc.Add(u);
                break;
            case JsonArray a:
                foreach (var n in a)
                {
                    if (n is JsonValue vv && vv.TryGetValue<string>(out var ss) && Uri.TryCreate(ss, UriKind.Absolute, out var uu))
                        acc.Add(uu);
                    if (n is JsonObject o && o.TryGetPropertyValue("url", out var url) && url is JsonValue v && v.TryGetValue<string>(out var s) && Uri.TryCreate(s, UriKind.Absolute, out var u))
                        acc.Add(u);
                }
                break;
        }
        return acc;
    }

    static TimeSpan? ParseIsoDuration(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        try { return System.Xml.XmlConvert.ToTimeSpan(iso); } catch { return null; }
    }

    private sealed record SocialPost(string? Description, List<SocialPostLink> Links);
    private sealed record SocialPostLink(string Url, string Text);

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex("^[^:]+:\\s*\"(?<caption>.+)\"$")]
    private static partial Regex InstagramQuotedCaptionRegex();
}
