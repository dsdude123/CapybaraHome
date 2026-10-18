using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Abstractions;

/// <summary>The persistence surface the application services work against.</summary>
public interface ICapybaraHomeDbContext
{
    /// <summary>Task categories.</summary>
    DbSet<Category> Categories { get; }

    /// <summary>Tasks.</summary>
    DbSet<TaskItem> Tasks { get; }

    /// <summary>Dashboard cards.</summary>
    DbSet<DashboardCard> DashboardCards { get; }

    /// <summary>Users allowed to sign in.</summary>
    DbSet<AppUser> Users { get; }

    /// <summary>Outstanding and spent invites.</summary>
    DbSet<UserInvite> UserInvites { get; }

    /// <summary>Bearer tokens issued to the connector.</summary>
    DbSet<ApiToken> ApiTokens { get; }

    /// <summary>Writes the pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
