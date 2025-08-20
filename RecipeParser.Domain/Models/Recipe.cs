namespace RecipeParser.Domain.Models;

public class Recipe
{
    public string Title { get; set; }
    
    public string? Description { get; set; }
    public string? Author { get; set; }
    
    public string? ImageUrl { get; set; }
    
    public double? MinutesToPrepare { get; set; }
    public double? MinutesToCook { get; set; }
    public double? TotalMins { get; set; }
    
    public string? Serves { get; set; }
    
    public ICollection<string> Ingredients { get; set; } = new List<string>();

    public List<string?> Tags { get; set; } = [];
    
    public string Url { get; set; }

    public ICollection<RecipeStepSection> StepSections { get; set; } = [];
    
    public RecipeRatings Ratings { get; set; }
}