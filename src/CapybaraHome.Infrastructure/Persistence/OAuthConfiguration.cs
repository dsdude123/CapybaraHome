using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="OAuthClient"/> to its table.</summary>
public class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    /// <summary>Configures the client mapping.</summary>
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.ToTable("OAuthClients");
        builder.Property(c => c.ClientId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.ClientName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.SecretHash).HasMaxLength(64);
        builder.Property(c => c.RedirectUris).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.TokenEndpointAuthMethod).IsRequired().HasMaxLength(40);

        builder.HasIndex(c => c.ClientId).IsUnique();
    }
}

/// <summary>Maps <see cref="OAuthAuthorizationCode"/> to its table.</summary>
public class OAuthAuthorizationCodeConfiguration : IEntityTypeConfiguration<OAuthAuthorizationCode>
{
    /// <summary>Configures the code mapping.</summary>
    public void Configure(EntityTypeBuilder<OAuthAuthorizationCode> builder)
    {
        builder.ToTable("OAuthAuthorizationCodes");
        builder.Property(c => c.CodeHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.RedirectUri).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.CodeChallenge).IsRequired().HasMaxLength(128);
        builder.Property(c => c.Scope).HasMaxLength(400);
        builder.Property(c => c.Resource).HasMaxLength(2000);

        builder.HasIndex(c => c.CodeHash).IsUnique();

        builder.HasOne<OAuthClient>()
            .WithMany()
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="OAuthToken"/> to its table.</summary>
public class OAuthTokenConfiguration : IEntityTypeConfiguration<OAuthToken>
{
    /// <summary>Configures the token mapping.</summary>
    public void Configure(EntityTypeBuilder<OAuthToken> builder)
    {
        builder.ToTable("OAuthTokens");
        builder.Property(t => t.SecretHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Scope).HasMaxLength(400);
        builder.Property(t => t.Kind).HasConversion<int>();

        builder.HasIndex(t => t.SecretHash).IsUnique();

        builder.HasOne<OAuthClient>()
            .WithMany()
            .HasForeignKey(t => t.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
