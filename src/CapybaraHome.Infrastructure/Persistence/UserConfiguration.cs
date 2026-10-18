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
        builder.Property(u => u.Issuer).IsRequired().HasMaxLength(300);
        builder.Property(u => u.Subject).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);

        builder.HasIndex(u => new { u.Issuer, u.Subject }).IsUnique();
        builder.HasIndex(u => u.Email);
    }
}
