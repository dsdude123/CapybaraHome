using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Domain.Reports;

/// <summary>
/// A category the report's task table is kept to. With no rows at all the table carries every
/// category, which is what the report did before it could be narrowed.
/// </summary>
public class ReportCategory
{
    /// <summary>The category, and the primary key.</summary>
    public int CategoryId { get; set; }

    /// <summary>Navigation to <see cref="CategoryId"/>.</summary>
    public Category? Category { get; set; }
}
