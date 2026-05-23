namespace RecipeParser.Infrastructure.Services;

public sealed class OpenAiRecipeParserOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4.1-mini";
}
