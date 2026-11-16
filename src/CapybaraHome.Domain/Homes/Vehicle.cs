namespace CapybaraHome.Domain.Homes;

/// <summary>A car the drives are made in, with what it costs to fuel.</summary>
public class Vehicle : IProfileOwned
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>The scenario this car belongs to.</summary>
    public int ProfileId { get; set; }

    /// <summary>The profile named by <see cref="ProfileId"/>.</summary>
    public SearchProfile? Profile { get; set; }

    /// <summary>What the car is called.</summary>
    public required string Name { get; set; }

    /// <summary>Average miles it does to the gallon.</summary>
    public double MilesPerGallon { get; set; }

    /// <summary>What a gallon costs now.</summary>
    public decimal FuelPricePerGallon { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
