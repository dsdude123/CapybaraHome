using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="UserInvite"/> to its table.</summary>
public class UserInviteConfiguration : IEntityTypeConfiguration<UserInvite>
{
    /// <summary>Configures the invite mapping.</summary>
    public void Configure(EntityTypeBuilder<UserInvite> builder)
    {
        builder.ToTable("UserInvites");
        builder.Property(i => i.Email).IsRequired().HasMaxLength(320);
        builder.Ignore(i => i.IsOpen);
        builder.HasIndex(i => i.Email);
    }
}
