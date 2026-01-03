using NUnit.Framework;
using Shouldly;

namespace RecipeParser.Services.UnitTests;

public partial class RecipeParserServiceTests
{
    
    [Test]
    public async Task ParseGoodFoods_ByUrl_ReturnsExpectedStructure()
    {
        // Arrange
        var url = "https://www.bbcgoodfood.com/recipes/swedish-meatball-burgers";
        
        // Act
        var result = await _sut.ParseRecipeByUrl(url);
        
        // Assert
        result.ShouldNotBeNull();
        result.Title.ShouldBe("Swedish meatball burgers");
        result.Author.ShouldBe("Barney Desmazery");
        
        result.ImageUrl.ShouldBe("https://images.immediate.co.uk/production/volatile/sites/30/2020/08/swedish-meatball-burgers-e01dcfe.jpg?resize=440,400");
        
        result.MinutesToCook.ShouldBe(10);
        result.MinutesToPrepare.ShouldBe(15);
        result.TotalMins.ShouldBe(25);
        
        result.Serves.ShouldBe("6");
        
        result.RawIngredients.Count.ShouldBe(7);
        result.RawIngredients.First().ShouldBe("500g lean beef or pork mince");
        result.RawIngredients.Last().ShouldBe("burger buns sliced cheese, lettuce, sliced tomato and lingonberry sauce (optional), to serve");
        
        result.Description.ShouldBe("Love Swedish meatballs? Here we’ve transformed them into a burger that kids will adore – choose beef or pork mince, the flavours will be the same");
        
        result.Tags.Count.ShouldBe(4);
        
        result.Url.ShouldBe("https://www.bbcgoodfood.com/recipes/swedish-meatball-burgers#Recipe");
        
        result.StepSections.Count.ShouldBe(1);
        result.StepSections.First().Steps.Count.ShouldBe(3);
        result.StepSections.First().Steps.First().Step
            .ShouldBe(
                "Tip the mince, onion, egg, breadcrumbs, nutmeg and garlic powder into a large bowl and generously season with black pepper. Mix everything together using your hands, then shape the mixture into six patties. Transfer to a plate, cover and chill for 1 hr or up to a day.");
        result.StepSections.First().Steps.Last().Step.ShouldBe("Serve the burgers in the buns topped with the lettuce, tomato and lingonberry sauce, if you like.");
        
        result.Ratings.Reviews.Count.ShouldBeGreaterThanOrEqualTo(0);
        result.Ratings.TotalRatings.ShouldBeGreaterThanOrEqualTo(0);
        if (result.Ratings.Reviews.Count > 0)
            result.Ratings.Reviews.Any(r => r.Rating != null).ShouldBeTrue();
    }
    
    [Test]
    public async Task ParseGoodFoods_ByUrl_ReturnsExpectedReviewStats_WithoutComments()
    {
        // Arrange
        var url = "https://www.bbcgoodfood.com/recipes/chicken-alfredo";
        
        // Act
        var result = await _sut.ParseRecipeByUrl(url);
        
        // Assert
        result.ShouldNotBeNull();
        result.Ratings.OverallRating.ShouldBe(4);
        result.Ratings.TotalRatings.ShouldBe(92);
    }
}