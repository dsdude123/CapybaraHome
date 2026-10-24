using CapybaraHome.Domain.Grocery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="GroceryList"/> to its table.</summary>
public class GroceryListConfiguration : IEntityTypeConfiguration<GroceryList>
{
    /// <summary>Configures the list mapping.</summary>
    public void Configure(EntityTypeBuilder<GroceryList> builder)
    {
        builder.HasIndex(l => l.ClosedAt);

        builder.HasMany(l => l.Items)
            .WithOne(i => i.List)
            .HasForeignKey(i => i.GroceryListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="GroceryItem"/> to its table.</summary>
public class GroceryItemConfiguration : IEntityTypeConfiguration<GroceryItem>
{
    /// <summary>Configures the item mapping.</summary>
    public void Configure(EntityTypeBuilder<GroceryItem> builder)
    {
        builder.Property(i => i.Item).IsRequired().HasMaxLength(300);
        builder.Property(i => i.Quantity).HasMaxLength(100);
        builder.Property(i => i.Source).HasMaxLength(500);
        builder.HasIndex(i => new { i.GroceryListId, i.SortOrder });
    }
}

/// <summary>Maps <see cref="FrequentGroceryItem"/> to its table.</summary>
public class FrequentGroceryItemConfiguration : IEntityTypeConfiguration<FrequentGroceryItem>
{
    /// <summary>Configures the frequent item mapping.</summary>
    public void Configure(EntityTypeBuilder<FrequentGroceryItem> builder)
    {
        builder.Property(f => f.Item).IsRequired().HasMaxLength(300);
        builder.Property(f => f.Quantity).HasMaxLength(100);
        builder.HasIndex(f => f.SortOrder);
    }
}
