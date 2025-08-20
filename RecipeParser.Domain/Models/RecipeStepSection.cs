namespace RecipeParser.Domain.Models;

public class RecipeStepSection
{
    public string? Title { get; set; }
    public List<RecipeStep>? Steps { get; set; }
}