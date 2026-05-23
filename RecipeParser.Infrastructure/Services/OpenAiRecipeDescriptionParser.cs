using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Domain.Models;

namespace RecipeParser.Infrastructure.Services;

public sealed class OpenAiRecipeDescriptionParser(
    HttpClient httpClient,
    IOptions<OpenAiRecipeParserOptions> options,
    ILogger<OpenAiRecipeDescriptionParser> logger) : IRecipeDescriptionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<Recipe?> ParseRecipeFromDescription(string description, string sourceUrl, CancellationToken ct = default)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(description))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
        request.Content = JsonContent.Create(CreateRequest(description, sourceUrl), options: JsonOptions);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await LogOpenAiErrorResponse(response, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var outputText = ExtractOutputText(json.RootElement);

        if (string.IsNullOrWhiteSpace(outputText))
            return null;

        var parsed = JsonSerializer.Deserialize<ParsedSocialRecipe>(outputText, JsonOptions);
        if (parsed is null || !parsed.IsRecipe || string.IsNullOrWhiteSpace(parsed.Title))
            return null;

        return parsed.ToRecipe(sourceUrl);
    }

    private async Task LogOpenAiErrorResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        logger.LogError(
            "OpenAI recipe parsing request failed with {StatusCode} {ReasonPhrase}. RequestUri: {RequestUri}. ResponseHeaders: {ResponseHeaders}. ContentHeaders: {ContentHeaders}. ResponseBody: {ResponseBody}",
            (int)response.StatusCode,
            response.ReasonPhrase,
            response.RequestMessage?.RequestUri,
            string.Join("; ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")),
            string.Join("; ", response.Content.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")),
            responseBody);
    }

    private object CreateRequest(string description, string sourceUrl)
    {
        return new
        {
            model = options.Value.Model,
            instructions = "Extract one cooking recipe from social post text. Return null-like empty fields when a value is not present. Do not invent ingredients, quantities, steps, timings, ratings, or servings.",
            input = $"Source URL: {sourceUrl}\n\nPost description:\n{description}",
            max_output_tokens = 1800,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "social_recipe",
                    strict = false,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            isRecipe = new { type = "boolean" },
                            title = new { type = new[] { "string", "null" } },
                            description = new { type = new[] { "string", "null" } },
                            author = new { type = new[] { "string", "null" } },
                            imageUrl = new { type = new[] { "string", "null" } },
                            minutesToPrepare = new { type = new[] { "number", "null" } },
                            minutesToCook = new { type = new[] { "number", "null" } },
                            totalMins = new { type = new[] { "number", "null" } },
                            serves = new { type = new[] { "string", "null" } },
                            ingredients = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            tags = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            steps = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            }
                        },
                        required = new[] { "isRecipe" }
                    }
                }
            }
        };
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString();

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString();
            }
        }

        return null;
    }

    private sealed class ParsedSocialRecipe
    {
        public bool IsRecipe { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? ImageUrl { get; set; }
        public double? MinutesToPrepare { get; set; }
        public double? MinutesToCook { get; set; }
        public double? TotalMins { get; set; }
        public string? Serves { get; set; }
        public List<string> Ingredients { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        public List<string> Steps { get; set; } = [];

        public Recipe ToRecipe(string sourceUrl)
        {
            return new Recipe
            {
                Title = Title!,
                Description = Description,
                Author = Author,
                ImageUrl = ImageUrl,
                MinutesToPrepare = MinutesToPrepare,
                MinutesToCook = MinutesToCook,
                TotalMins = TotalMins,
                Serves = Serves,
                RawIngredients = Ingredients.Where(i => !string.IsNullOrWhiteSpace(i)).ToList(),
                Tags = Tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList<string?>(),
                Url = sourceUrl,
                StepSections =
                [
                    new RecipeStepSection
                    {
                        Steps = Steps
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => new RecipeStep { Step = s.Trim() })
                            .ToList()
                    }
                ],
                Ratings = new RecipeRatings()
            };
        }
    }
}
