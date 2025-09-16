using NUnit.Framework;
using Shouldly;

namespace RecipeParser.Services.UnitTests;

public partial class RecipeParserServiceTests
{
    [Test]
    public async Task ParseByUrl_BeatTheBudget_ReturnsExpectedStructure()
    {
        // Arrange
        var url = "https://beatthebudget.com/recipe/chicken-katsu-curry/";

        // Act
        var result = await _sut.ParseRecipeByUrl(url);

        // Assert
        result.ShouldNotBeNull();
        result.Title.ShouldBe("Chicken Katsu Curry");
        result.Author.ShouldBe("Mimi Harrison");
        
        result.ImageUrl.ShouldBe("https://beatthebudget.com/wp-content/uploads/2020/07/chicken-katsu-curry-featured-image-scaled.jpg");
        
        result.MinutesToCook.ShouldBe(30);
        result.MinutesToPrepare.ShouldBe(5);
        result.TotalMins.ShouldBe(35);
        
        result.Serves.ShouldBe("6");
        
        result.RawIngredients.Count.ShouldBe(18);
        result.RawIngredients.First().ShouldBe("650 g chicken breasts ((£4.00))");
        result.RawIngredients.Last().ShouldBe("Chilli flakes");
        
        result.Description.ShouldBe("One of the best fakeaway creations! My Wagamama inspired chicken katsu curry. With the crispy breaded chicken being baked, it’s lower in calories and just as delicious.");
        
        result.Tags.Count.ShouldBe(5);
        
        result.Url.ShouldBe("https://beatthebudget.com/recipe/chicken-katsu-curry/#recipe");
        
        result.StepSections.Count.ShouldBe(1);
        result.StepSections.First().Steps.Count.ShouldBe(6);
        result.StepSections.First().Steps.First().Step.ShouldBe("Start by adding the onion & carrots into a deep non-stick frying pan along with the coconut oil. Gently fry on a medium/ low heat for around 5 minutes. Season with salt.");
        result.StepSections.First().Steps.Last().Step.ShouldBe("After 20 minutes, the katsu sauce should have thickened slightly so it’s ready to blend.  Slice the chicken diagonally for that wagamama look and serve up with a portion of rice, a ladle of the sauce and the optional sliced spring onion & chilli flakes.");
        
        result.Ratings.OverallRating.ShouldBe(4.41);
        result.Ratings.Reviews.Count.ShouldBeGreaterThanOrEqualTo(6);
        result.Ratings.Reviews.First().Text.ShouldBe("Curry recipe was good but turned out a bit too salty. Chicken part does not work at all it ended up making the chicken soggy so i had to fry it in a pan...");
    }
}