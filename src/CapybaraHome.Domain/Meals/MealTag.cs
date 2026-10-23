namespace CapybaraHome.Domain.Meals;

/// <summary>A label on a meal, equivalent to a label in the JIRA data this replaces.</summary>
public class MealTag
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>Which side of a meal this tag describes.</summary>
    public MealTagFacet Facet { get; set; } = MealTagFacet.Trait;

    /// <summary>Meals carrying this tag.</summary>
    public ICollection<Meal> Meals { get; set; } = [];
}
