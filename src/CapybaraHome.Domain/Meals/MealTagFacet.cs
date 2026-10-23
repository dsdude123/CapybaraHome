namespace CapybaraHome.Domain.Meals;

/// <summary>Which side of a meal a tag describes, so the filters can be one picker per facet.</summary>
public enum MealTagFacet
{
    /// <summary>The cuisine the meal belongs to.</summary>
    Cuisine,

    /// <summary>The main protein.</summary>
    Protein,

    /// <summary>How much work it is, such as easy or quick.</summary>
    Effort,

    /// <summary>The appliance it is cooked with.</summary>
    Appliance,

    /// <summary>Anything else about the meal, such as whether it leaves leftovers.</summary>
    Trait,
}
