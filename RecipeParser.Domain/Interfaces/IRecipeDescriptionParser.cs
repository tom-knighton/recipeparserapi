using RecipeParser.Domain.Models;

namespace RecipeParser.Domain.Interfaces;

public interface IRecipeDescriptionParser
{
    Task<Recipe?> ParseRecipeFromDescription(string description, string sourceUrl, CancellationToken ct = default);
}
