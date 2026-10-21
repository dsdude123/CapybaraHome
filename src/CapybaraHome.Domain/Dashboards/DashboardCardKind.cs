namespace CapybaraHome.Domain.Dashboards;

/// <summary>What a dashboard card shows.</summary>
public enum DashboardCardKind
{
    /// <summary>Progress towards finishing every task in one category.</summary>
    CategoryProgress,

    /// <summary>A count of the tasks matching the card's filter.</summary>
    TaskCount,

    /// <summary>A list of the tasks matching the card's filter.</summary>
    TaskList,
}
