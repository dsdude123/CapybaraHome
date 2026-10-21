using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="AppUser"/> to its table.</summary>
public class UserConfiguration : IEntityTypeConfiguration<AppUser>
{
    /// <summary>Configures the user mapping.</summary>
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.Property(u => u.Username).IsRequired().HasMaxLength(64);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(200);
        builder.Property(u => u.TotpSecret).HasMaxLength(64);
        builder.Ignore(u => u.TotpEnabled);

        builder.HasIndex(u => u.Username).IsUnique();
    }
}
