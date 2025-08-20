namespace RecipeParser.Domain.Models;

public class RecipeRatings
{
    public double? OverallRating { get; set; }
    public ICollection<RecipeReviews> Reviews { get; set; } = [];
}

public class RecipeReviews
{
    public string Text { get; set; }
}