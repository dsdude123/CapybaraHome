using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Web;

/// <summary>Turns the tag facets into the words the UI shows.</summary>
public static class MealTagFacetNames
{
    /// <summary>Every facet, in the order the filters offer them.</summary>
    public static readonly MealTagFacet[] All =
    [
        MealTagFacet.Cuisine,
        MealTagFacet.Protein,
        MealTagFacet.Effort,
        MealTagFacet.Appliance,
        MealTagFacet.Trait,
    ];

    /// <summary>Returns the label for <paramref name="facet"/>.</summary>
    public static string Label(MealTagFacet facet) => facet switch
    {
        MealTagFacet.Cuisine => "Cuisine",
        MealTagFacet.Protein => "Protein",
        MealTagFacet.Effort => "Effort",
        MealTagFacet.Appliance => "Appliance",
        MealTagFacet.Trait => "Trait",
        _ => facet.ToString(),
    };
}
