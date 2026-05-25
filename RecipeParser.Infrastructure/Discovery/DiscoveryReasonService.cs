using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RecipeParser.Domain.Discovery;
using RecipeParser.Infrastructure.Services;

namespace RecipeParser.Infrastructure.Discovery;

public sealed class DiscoveryReasonService(
    HttpClient httpClient,
    IOptions<OpenAiRecipeParserOptions> openAiOptions,
    IOptions<DiscoveryOptions> discoveryOptions) : IDiscoveryReasonService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> CreateReason(
        DiscoveryProfile profile,
        DiscoveryCandidate candidate,
        DiscoveryCandidateRanking ranking,
        DiscoveryWeatherContext? weather,
        CancellationToken ct = default)
    {
        if (!discoveryOptions.Value.UseOpenAIReasons || string.IsNullOrWhiteSpace(openAiOptions.Value.ApiKey))
            return FallbackReason(candidate, ranking, weather);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
            request.Content = JsonContent.Create(new
            {
                model = openAiOptions.Value.Model,
                instructions = "Write one short recipe discovery reason under 12 words. Do not invent facts. Mention only the supplied source affinity, weather, popularity, or speed signals.",
                input = $"Title: {candidate.Title}\nDomain: {candidate.SourceDomain}\nTags: {string.Join(", ", candidate.Tags)}\nReason signal: {ranking.ReasonCode}\nWeather: {weather?.Condition}, {weather?.TemperatureC}C, {weather?.Season}",
                max_output_tokens = 40
            }, options: JsonOptions);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return FallbackReason(candidate, ranking, weather);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var text = ExtractOutputText(json.RootElement)?.Trim();
            return string.IsNullOrWhiteSpace(text) ? FallbackReason(candidate, ranking, weather) : text.Trim('"');
        }
        catch
        {
            return FallbackReason(candidate, ranking, weather);
        }
    }

    private static string FallbackReason(DiscoveryCandidate candidate, DiscoveryCandidateRanking ranking, DiscoveryWeatherContext? weather)
    {
        return ranking.ReasonCode switch
        {
            "sourceAffinity" => $"You often save recipes from {candidate.SourceDomain}.",
            "weather" when weather?.TemperatureC is <= 10 => "Good for a cold evening.",
            "weather" when weather?.TemperatureC is >= 22 => "Good for a warm day.",
            "weather" => "Matched to today's weather.",
            _ when candidate.Rating is >= 4.5 => "Popular and highly rated.",
            _ when candidate.TotalMinutes is > 0 and <= 30 => "Quick enough for today.",
            _ => "Popular from a trusted recipe source."
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
}
