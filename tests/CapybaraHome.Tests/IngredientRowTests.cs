using CapybaraHome.Web;
using Microsoft.AspNetCore.Http;

namespace CapybaraHome.Tests;

/// <summary>Covers the ingredient lines an editor holds between form posts.</summary>
public class IngredientRowTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("save", null)]
    [InlineData("add-row", null)]
    [InlineData("remove-", null)]
    [InlineData("remove-what", null)]
    [InlineData("remove-0", 0)]
    [InlineData("remove-2", 2)]
    public void Only_a_remove_button_names_a_row(string? action, int? expected)
    {
        Assert.Equal(expected, IngredientRow.RemovedIndex(action));
    }

    [Fact]
    public void A_remove_button_names_the_row_it_sits_on()
    {
        Assert.Equal(3, IngredientRow.RemovedIndex(IngredientRow.RemoveAction(3)));
    }

    [Fact]
    public void Rows_are_read_back_in_the_order_they_were_rendered()
    {
        var form = new FormCollection(new()
        {
            ["quantity"] = new(["1 tin", string.Empty]),
            ["item"] = new(["first", "second", "third"]),
        });

        var rows = IngredientRow.ReadFrom(form);

        Assert.Equal(["first", "second", "third"], rows.Select(r => r.Item));

        // A line whose quantity box never arrived still reads back, so the list cannot shift.
        Assert.Equal(["1 tin", string.Empty, null], rows.Select(r => r.Quantity));
    }
}
