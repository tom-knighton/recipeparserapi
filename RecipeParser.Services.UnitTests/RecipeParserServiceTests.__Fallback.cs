using System.Net;
using Jering.Javascript.NodeJS;
using NSubstitute;
using NUnit.Framework;
using RecipeParser.Application.Services;
using RecipeParser.Domain.Interfaces;
using RecipeParser.Domain.Models;
using Shouldly;

namespace RecipeParser.Services.UnitTests;

public class RecipeParserServiceFallbackTests
{
    [Test]
    public async Task ParseRecipeByUrl_WhenTasteOfHomeRecipe_UsesWordPressRecipeApiFirst()
    {
        var node = CreateNodeParser();
        var fetcher = new TasteOfHomeApiRecipeFetcher();
        var sut = new RecipeParserService(node, fetcher);

        var result = await sut.ParseRecipeByUrl("https://www.tasteofhome.com/recipes/kimchi-cauliflower-fried-rice/");

        result.Title.ShouldBe("Kimchi Cauliflower Fried Rice");
        result.MinutesToCook.ShouldBe(25);
        result.RawIngredients.ShouldContain("1 cup kimchi, chopped");
        result.Ratings.TotalRatings.ShouldBe(2);
        fetcher.StaticCalls.ShouldBe(1);
        fetcher.LastStaticUrl.ShouldBe("https://www.tasteofhome.com/wp-json/wp/v2/recipe?slug=kimchi-cauliflower-fried-rice");
        fetcher.RenderedCalls.ShouldBe(0);
    }

    [Test]
    public async Task ParseRecipeByUrl_WhenStaticFetchIsForbidden_TriesRenderedJsonLd()
    {
        var node = CreateNodeParser();
        var fetcher = new StaticForbiddenRenderedRecipeFetcher();
        var sut = new RecipeParserService(node, fetcher);

        var result = await sut.ParseRecipeByUrl("https://www.example.com/recipes/kimchi-cauliflower-fried-rice/");

        result.Title.ShouldBe("Kimchi Cauliflower Fried Rice");
        result.RawIngredients.ShouldContain("1 cup kimchi");
        fetcher.RenderedCalls.ShouldBe(1);
    }

    private static INodeJSService CreateNodeParser()
    {
        var node = Substitute.For<INodeJSService>();
        node.InvokeFromFileAsync<IngredientResult>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object[]>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var args = callInfo.ArgAt<object[]>(2);
                var line = args.FirstOrDefault()?.ToString() ?? "";
                return Task.FromResult(new IngredientResult(null, null, null, null, null, null, line, null, line, []));
            });
        node.InvokeFromFileAsync<InstructionResult>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object[]>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InstructionResult(null, [], null, null, null, null, null)));

        return node;
    }

    private sealed class TasteOfHomeApiRecipeFetcher : IPageFetcher
    {
        public int StaticCalls { get; private set; }
        public int RenderedCalls { get; private set; }
        public string? LastStaticUrl { get; private set; }

        public Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default)
        {
            StaticCalls++;
            LastStaticUrl = url;
            url.ShouldBe("https://www.tasteofhome.com/wp-json/wp/v2/recipe?slug=kimchi-cauliflower-fried-rice");

            return Task.FromResult(
                """
                [
                  {
                    "recipe_schema": {
                      "@context": "https://schema.org",
                      "@type": "Recipe",
                      "@id": "https://www.tasteofhome.com/recipes/kimchi-cauliflower-fried-rice/",
                      "name": "Kimchi Cauliflower Fried Rice",
                      "cookTime": "PT25M",
                      "recipeIngredient": ["1 cup kimchi, chopped"],
                      "recipeInstructions": ["Stir-fry everything until hot."],
                      "aggregateRating": {
                        "@type": "AggregateRating",
                        "ratingValue": 5,
                        "reviewCount": 2
                      }
                    }
                  }
                ]
                """);
        }

        public Task<IReadOnlyList<string>> GetRenderedJsonLdAsync(string url, TimeSpan timeout, CancellationToken ct = default)
        {
            RenderedCalls++;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private sealed class StaticForbiddenRenderedRecipeFetcher : IPageFetcher
    {
        public int RenderedCalls { get; private set; }

        public Task<string> GetStaticHtmlAsync(string url, CancellationToken ct = default)
        {
            throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
        }

        public Task<IReadOnlyList<string>> GetRenderedJsonLdAsync(string url, TimeSpan timeout, CancellationToken ct = default)
        {
            RenderedCalls++;
            return Task.FromResult<IReadOnlyList<string>>(
            [
                """
                {
                  "@context": "https://schema.org",
                  "@type": "Recipe",
                  "name": "Kimchi Cauliflower Fried Rice",
                  "recipeIngredient": ["1 cup kimchi"],
                  "recipeInstructions": ["Stir-fry everything until hot."]
                }
                """
            ]);
        }
    }
}
