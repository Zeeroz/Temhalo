using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Xunit;

namespace Temhalo.Core.Tests;

public sealed class SpscFlvRingTests
{
    [Fact]
    public async Task PreservesFifoOrderThroughRepeatedWrapAndReleasesOwnedTags()
    {
        byte[] wire = FlvTestData.File(Enumerable.Range(0, 700)
            .Select(i => (FlvTagType.Script, (uint)i, new byte[] { (byte)i })).ToArray());
        using var input = new MemoryStream(wire);
        var budget = new FlvMemoryBudget(1024 * 1024);
        var reader = new FlvTagReader(input, budget, new MonotonicMediaClock());
        await reader.ReadHeaderAsync(CancellationToken.None);
        using (var ring = new SpscFlvRing(7, budget))
        {
            for (int round = 0; round < 100; round++)
            {
                for (int j = 0; j < 7; j++)
                {
                    var tag = await reader.ReadTagAsync(CancellationToken.None);
                    Assert.NotNull(tag);
                    Assert.True(ring.TryWrite(tag));
                }
                for (int j = 0; j < 7; j++)
                {
                    using var tag = ring.TryRead();
                    Assert.NotNull(tag);
                    Assert.Equal((uint)(round * 7 + j), tag.Timestamp);
                }
                Assert.Equal(0, ring.Count);
                Assert.Null(ring.TryRead());
            }
        }
        Assert.Equal(0, budget.UsedBytes);
    }
}
