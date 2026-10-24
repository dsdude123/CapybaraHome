using CapybaraHome.Application.Meals;

namespace CapybaraHome.Tests;

/// <summary>Covers the guess the splitting screen offers, which is only ever a starting point.</summary>
public class IngredientSplitterTests
{
    [Fact]
    public void A_bulleted_list_at_the_top_becomes_the_ingredients()
    {
        var suggestion = IngredientSplitter.Suggest("""
        - 2 cups rice
        - 1 tin tomatoes
        - salt

        Simmer it all for twenty minutes.
        """);

        Assert.Equal(["rice", "tomatoes", "salt"], suggestion.Ingredients.Select(i => i.Item));
        Assert.Equal(["2 cups", "1 tin", null], suggestion.Ingredients.Select(i => i.Quantity));
        Assert.Equal("Simmer it all for twenty minutes.", suggestion.Directions);
    }

    [Fact]
    public void An_ingredients_heading_is_taken_at_its_word()
    {
        var suggestion = IngredientSplitter.Suggest("""
        ## Ingredients
        Rice
        Tomatoes

        ## Method
        Cook the rice.
        Add the tomatoes.
        """);

        Assert.Equal(["Rice", "Tomatoes"], suggestion.Ingredients.Select(i => i.Item));
        Assert.Contains("Cook the rice.", suggestion.Directions);
        Assert.Contains("## Method", suggestion.Directions);
    }

    [Fact]
    public void Anything_before_an_ingredients_heading_stays_in_the_directions()
    {
        var suggestion = IngredientSplitter.Suggest("""
        Best made the day before.

        **Ingredients:**
        - 1 onion
        """);

        Assert.Equal("onion", Assert.Single(suggestion.Ingredients).Item);
        Assert.Contains("Best made the day before.", suggestion.Directions);
    }

    [Fact]
    public void A_quantity_keeps_its_unit_and_a_fraction_keeps_its_slash()
    {
        var suggestion = IngredientSplitter.Suggest("""
        - 1/2 onion, diced
        - 1.5 kg beef
        - 2-3 cloves garlic
        """);

        Assert.Equal(["1/2", "1.5 kg", "2-3 cloves"], suggestion.Ingredients.Select(i => i.Quantity));
        Assert.Equal(["onion, diced", "beef", "garlic"], suggestion.Ingredients.Select(i => i.Item));
    }

    [Fact]
    public void A_numbered_list_of_steps_is_left_as_directions()
    {
        var text = """
        1. Heat the pan.
        2. Add the onion.
        3. Serve.
        """;

        var suggestion = IngredientSplitter.Suggest(text);

        Assert.Empty(suggestion.Ingredients);
        Assert.Equal(text, suggestion.Directions);
    }

    [Fact]
    public void A_description_with_no_list_is_left_alone()
    {
        var suggestion = IngredientSplitter.Suggest("Just reheat it and eat it.");

        Assert.Empty(suggestion.Ingredients);
        Assert.Equal("Just reheat it and eat it.", suggestion.Directions);
    }

    [Fact]
    public void A_checklist_loses_its_boxes()
    {
        var suggestion = IngredientSplitter.Suggest("""
        - [ ] 2 cups rice
        - [x] 1 tin tomatoes
        """);

        Assert.Equal(["rice", "tomatoes"], suggestion.Ingredients.Select(i => i.Item));
    }

    [Fact]
    public void An_empty_description_suggests_nothing()
    {
        var suggestion = IngredientSplitter.Suggest(null);

        Assert.Empty(suggestion.Ingredients);
        Assert.Equal(string.Empty, suggestion.Directions);
    }

    [Fact]
    public void A_line_that_is_only_a_quantity_keeps_it_as_the_item()
    {
        var suggestion = IngredientSplitter.Suggest("""
        - 2 cups
        - 1 onion
        """);

        var only = suggestion.Ingredients[0];
        Assert.Equal("2 cups", only.Item);
        Assert.Null(only.Quantity);
    }
}
