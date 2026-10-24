namespace CapybaraHome.Domain.Meals;

/// <summary>
/// How many meals a slot aims for, kept between visits to the planning page. A slot with no row
/// falls back to the default the page offers.
/// </summary>
public class MealPlanTarget
{
    /// <summary>Slot this count belongs to, and the primary key.</summary>
    public MealSlot Slot { get; set; }

    /// <summary>How many meals that slot aims for.</summary>
    public int Count { get; set; }
}
