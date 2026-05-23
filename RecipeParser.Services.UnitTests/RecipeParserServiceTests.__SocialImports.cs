using NUnit.Framework;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Exceptions;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Domain.Models;
using Shouldly;

namespace RecipeParser.Services.UnitTests;

public partial class RecipeParserServiceTests
{
    [Test]
    public async Task ParseByUrl_SocialPostWithRecipeLink_ParsesLinkedRecipeBeforeDescriptionFallback()
    {
        // Arrange
        const string socialUrl = "https://www.tiktok.com/@sporkast/video/123";
        const string recipeUrl = "https://example.com/recipes/chilli-crisp-noodles";
        var fetcher = new FakePageFetcher(new Dictionary<string, string>
        {
            [socialUrl] = """
                <html>
                    <head>
                        <meta property="og:description" content="Dinner in ten. Full recipe below." />
                    </head>
                    <body><a href="https://example.com/recipes/chilli-crisp-noodles">Full recipe</a></body>
                </html>
                """,
            [recipeUrl] = """
                <html>
                    <head>
                        <script type="application/ld+json">
                        {
                            "@type": "Recipe",
                            "@id": "https://example.com/recipes/chilli-crisp-noodles#recipe",
                            "name": "Chilli crisp noodles",
                            "author": { "name": "Sporkast" },
                            "recipeIngredient": ["200g noodles", "2 tbsp chilli crisp"],
                            "recipeInstructions": ["Boil the noodles.", "Toss with chilli crisp."],
                            "comment": []
                        }
                        </script>
                    </head>
                </html>
                """
        });
        var descriptionParser = new FakeDescriptionParser();
        var sut = new RecipeParserService(_mockNode, fetcher, descriptionParser);

        // Act
        var result = await sut.ParseRecipeByUrl(socialUrl);

        // Assert
        result.Title.ShouldBe("Chilli crisp noodles");
        result.Url.ShouldBe("https://example.com/recipes/chilli-crisp-noodles#recipe");
        result.RawIngredients.Count.ShouldBe(2);
        result.Ingredients.Count.ShouldBe(2);
        result.StepSections.Single().Steps!.Count.ShouldBe(2);
        descriptionParser.Calls.ShouldBe(0);
    }

    [Test]
    public async Task ParseByUrl_SocialPostWithoutRecipeLink_ParsesCaptionWithDescriptionParser()
    {
        // Arrange
        const string socialUrl = "https://www.instagram.com/reel/abc123/";
        var fetcher = new FakePageFetcher(new Dictionary<string, string>
        {
            [socialUrl] = """
                <html>
                    <head>
                        <meta property="og:description" content="Tomato eggs: fry tomatoes, add beaten eggs, finish with spring onion. Serves 2." />
                    </head>
                    <body><a href="https://www.instagram.com/sporkast">Profile</a></body>
                </html>
                """
        });
        var descriptionParser = new FakeDescriptionParser(new Recipe
        {
            Title = "Tomato eggs",
            Description = "Tomato eggs: fry tomatoes, add beaten eggs, finish with spring onion. Serves 2.",
            Serves = "2",
            RawIngredients = ["tomatoes", "eggs", "spring onion"],
            Url = socialUrl,
            StepSections =
            [
                new RecipeStepSection
                {
                    Steps =
                    [
                        new RecipeStep { Step = "Fry the tomatoes." },
                        new RecipeStep { Step = "Add beaten eggs and finish with spring onion." }
                    ]
                }
            ],
            Ratings = new RecipeRatings()
        });
        var sut = new RecipeParserService(_mockNode, fetcher, descriptionParser);

        // Act
        var result = await sut.ParseRecipeByUrl(socialUrl);

        // Assert
        result.Title.ShouldBe("Tomato eggs");
        result.Serves.ShouldBe("2");
        result.Ingredients.Count.ShouldBe(3);
        result.StepSections.Single().Steps!.Count.ShouldBe(2);
        descriptionParser.Calls.ShouldBe(1);
        descriptionParser.LastDescription!.ShouldContain("Tomato eggs");
    }

    [Test]
    public async Task ParseByUrl_InstagramReel_ExtractsCaptionFromStaticMetaTitle()
    {
        // Arrange
        const string socialUrl = "https://www.instagram.com/reel/abc123/";
        var fetcher = new FakePageFetcher(new Dictionary<string, string>
        {
            [socialUrl] = """
                <html>
                    <head>
                        <meta property="og:title" content="Sporkast on Instagram: &quot;Crispy potato salad. Boil potatoes, smash, roast hard, then toss with yoghurt, herbs and lemon. Full recipe below.&quot;" />
                        <meta property="og:description" content="Instagram" />
                        <title>Instagram</title>
                    </head>
                </html>
                """
        });
        var descriptionParser = new FakeDescriptionParser(new Recipe
        {
            Title = "Crispy potato salad",
            RawIngredients = ["potatoes", "yoghurt", "herbs", "lemon"],
            Url = socialUrl,
            StepSections = [new RecipeStepSection { Steps = [new RecipeStep { Step = "Roast the smashed potatoes." }] }],
            Ratings = new RecipeRatings()
        });
        var sut = new RecipeParserService(_mockNode, fetcher, descriptionParser);

        // Act
        var result = await sut.ParseRecipeByUrl(socialUrl);

        // Assert
        result.Title.ShouldBe("Crispy potato salad");
        descriptionParser.Calls.ShouldBe(1);
        descriptionParser.LastDescription.ShouldBe("Crispy potato salad. Boil potatoes, smash, roast hard, then toss with yoghurt, herbs and lemon. Full recipe below.");
    }

    [Test]
    public async Task ParseByUrl_SocialPostWithOnlyGenericTitle_DoesNotParseBoilerplateAsCaption()
    {
        // Arrange
        const string socialUrl = "https://www.instagram.com/reel/no-caption/";
        var fetcher = new FakePageFetcher(new Dictionary<string, string>
        {
            [socialUrl] = """
                <html>
                    <head>
                        <meta property="og:description" content="Instagram" />
                        <title>Instagram</title>
                    </head>
                </html>
                """
        });
        var descriptionParser = new FakeDescriptionParser();
        var sut = new RecipeParserService(_mockNode, fetcher, descriptionParser);

        // Act / Assert
        await Should.ThrowAsync<NoRecipeFoundException>(() => sut.ParseRecipeByUrl(socialUrl));
        descriptionParser.Calls.ShouldBe(0);
    }

    private sealed class FakeDescriptionParser(Recipe? recipe = null) : IRecipeDescriptionParser
    {
        public int Calls { get; private set; }
        public string? LastDescription { get; private set; }

        public Task<Recipe?> ParseRecipeFromDescription(string description, string sourceUrl, CancellationToken ct = default)
        {
            Calls++;
            LastDescription = description;
            return Task.FromResult(recipe);
        }
    }

    private sealed class FakePageFetcher(Dictionary<string, string> htmlByUrl) : IPageFetcher
    {
        public Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default)
        {
            return Task.FromResult(htmlByUrl.TryGetValue(url, out var html) ? html : "<html></html>");
        }

        public Task<IReadOnlyList<string>> GetRenderedJsonLdAsync(string url, TimeSpan timeout, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }
}
