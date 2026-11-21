using CapybaraHome.Web;
using Microsoft.Extensions.Time.Testing;

namespace CapybaraHome.Tests;

/// <summary>Covers the notes kept from a refused tour: read back by ticket, and dropped once stale.</summary>
public class TourDraftStoreTests
{
    [Fact]
    public void Notes_are_read_back_by_their_ticket_until_they_expire()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        var store = new TourDraftStore(clock);

        Assert.Null(store.Keep(null));
        Assert.Null(store.Keep("   "));

        var ticket = store.Keep("Bright and quiet.");
        Assert.Equal("Bright and quiet.", store.Read(ticket));
        Assert.Null(store.Read(null));
        Assert.Null(store.Read("no-such-ticket"));

        clock.Advance(TourDraftStore.Lifetime);
        Assert.Null(store.Read(ticket));

        var later = store.Keep("A second tour.");
        Assert.Null(store.Read(ticket));
        Assert.Equal("A second tour.", store.Read(later));
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(16L * 1024 * 1024, "16 MB")]
    [InlineData(8L * 1024 * 1024 * 1024, "8 GB")]
    [InlineData(1288490189L, "1.2 GB")]
    public void A_file_size_is_written_in_the_largest_unit_that_fits(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizes.Label(bytes));
    }
}
