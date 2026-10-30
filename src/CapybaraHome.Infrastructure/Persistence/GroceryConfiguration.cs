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

        // One list is open at a time. The index that holds that is written by hand in the
        // migration: it is over the expression ClosedAt IS NULL, because SQLite counts NULLs as
        // distinct, so a unique index over the column itself would let a second open list in.

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
        // NOCASE so the unique index below refuses "Milk" beside "milk", whatever writes it.
        builder.Property(f => f.Item).IsRequired().HasMaxLength(300).UseCollation("NOCASE");
        builder.Property(f => f.Quantity).HasMaxLength(100);
        builder.HasIndex(f => f.Item).IsUnique();
        builder.HasIndex(f => f.SortOrder);
    }
}
