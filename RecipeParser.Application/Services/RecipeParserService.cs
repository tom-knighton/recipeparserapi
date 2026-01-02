using System.Text.Json.Nodes;
using AngleSharp;
using Jering.Javascript.NodeJS;
using RecipeParser.Domain.Exceptions;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Domain.Models;

namespace RecipeParser.Application.Services;

public class RecipeParserService(INodeJSService node): IRecipeParserService
{
    public async Task<Recipe> ParseRecipeByUrl(string url)
    {
        var jsonRecipe = await TryParseSchema(url);

        Recipe? recipe = null;
        if (jsonRecipe is not null)
        {
            recipe = jsonRecipe;
        }

        foreach (var ingredient in recipe?.RawIngredients ?? [])
        {
            var result = await node.InvokeFromFileAsync<IngredientResult>(
                "recipeParser.cjs",
                exportName: "parseIngredientLine",
                args: new object?[] { ingredient, "en", new { } }
            );
            
            if (result is not null)
                recipe?.Ingredients.Add(result);
        }

        foreach (var stepSection in recipe?.StepSections)
        {
            foreach (var step in stepSection.Steps)
            {
                var result = await node.InvokeFromFileAsync<InstructionResult>(
                    "recipeParser.cjs",
                    exportName: "parseInstructionLine",
                    args: new object?[] { step.Step, "en", new { } }
                );

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
        }
        
        if (recipe is not null)
            return recipe;

        throw new NoRecipeFoundException();
    }

    private async Task<Recipe?> TryParseSchema(string url)
    {
        var cfg = Configuration.Default.WithDefaultLoader();
        var ctx = BrowsingContext.New(cfg);
        var doc = await ctx.OpenAsync(url);

        var scripts = doc
            .QuerySelectorAll("script[type='application/ld+json']")
            .Select(s => s.TextContent)
            .Where(s => !string.IsNullOrWhiteSpace(s));

        foreach (var json in scripts)
        {
            foreach (var node in ExpandCandidates(json))
            {
                if (!IsRecipeNode(node)) continue;

                var r = MapRecipeFromSchema(node, url);
                if (r is not null)
                {
                    return r;
                }
            }
        }

        return null;
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
    
    static bool IsRecipeNode(JsonObject node)
    {
        if (!node.TryGetPropertyValue("@type", out var t)) return false;
        return t switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s.Equals("Recipe", StringComparison.OrdinalIgnoreCase),
            JsonArray a => a.Any(x => x is JsonValue v && v.TryGetValue<string>(out var s) && s.Equals("Recipe", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
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

        double? aggregateRating = r["aggregateRating"] switch
        {
            JsonObject o when o["ratingValue"] is JsonValue v => double.TryParse(v.ToString(), out var rVal) ? rVal : null,
            JsonValue v => double.TryParse(v.ToString(), out var rVal) ? rVal : null,
            _ => null
        };
        
        int? totalRatings = r["aggregateRating"] switch
        {
            JsonObject o when o["ratingCount"] is JsonValue v => int.TryParse(v.ToString(), out var rCount) ? rCount : null,
            JsonValue v => int.TryParse(v.ToString(), out var rCount) ? rCount : null,
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
                        Steps = steps.SelectMany(s => s.Steps).ToList(),
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
}