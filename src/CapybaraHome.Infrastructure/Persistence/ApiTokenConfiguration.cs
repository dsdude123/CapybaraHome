using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="ApiToken"/> to its table.</summary>
public class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    /// <summary>Configures the token mapping.</summary>
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("ApiTokens");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Prefix).IsRequired().HasMaxLength(16);
        builder.Property(t => t.SecretHash).IsRequired().HasMaxLength(64);

        builder.HasIndex(t => t.SecretHash).IsUnique();

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
