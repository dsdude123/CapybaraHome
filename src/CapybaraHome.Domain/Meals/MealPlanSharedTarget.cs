namespace CapybaraHome.Domain.Meals;

/// <summary>
/// How many of the planned meals are meant to be shared ones, kept between visits to the
/// planning page. The table holds one row at most; with no row the page's default stands.
/// </summary>
public class MealPlanSharedTarget
{
    /// <summary>Primary key. Only one row is ever stored, so it is always one.</summary>
    public int Id { get; set; } = 1;

    /// <summary>How many of the planned meals are meant to be shared.</summary>
    public int Count { get; set; }
}
