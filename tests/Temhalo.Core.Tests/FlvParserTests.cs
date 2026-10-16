using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Xunit;
using static Temhalo.Core.Tests.FlvTestData;

namespace Temhalo.Core.Tests;

public sealed class FlvParserTests
{
    [Fact]
    public async Task FragmentedWirePreservesExtendedTimestampAndPreviousTagSizes()
    {
        byte[] wire = File((FlvTagType.Video, 0xDEADBEEF, Video(true, -33)));
        using var input = new FragmentedStream(wire);
        var budget = new FlvMemoryBudget(1024 * 1024);
        var reader = new FlvTagReader(input, budget, new MonotonicMediaClock());
        Assert.Equal(new FlvHeader(true, true), await reader.ReadHeaderAsync(CancellationToken.None));
        using var tag = await reader.ReadTagAsync(CancellationToken.None);
        Assert.NotNull(tag);
        Assert.Equal(0xDEADBEEFu, tag.Timestamp);
        Assert.Equal(Video(true, -33), tag.Payload.ToArray());
        using var output = new MemoryStream();
        var writer = new FlvTagWriter(output);
        await writer.WriteHeaderAsync(new(true, true), CancellationToken.None);
        await writer.WriteTagAsync(tag, tag.Timestamp, CancellationToken.None);
        Assert.Equal(wire, output.ToArray());
        Assert.Null(await reader.ReadTagAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0)] // bad signature
    [InlineData(3, 2)] // bad version
    [InlineData(4, 0x85)] // reserved flags
    [InlineData(12, 1)] // PreviousTagSize0
    [InlineData(13, 0x29)] // filtered tag
    [InlineData(21, 1)] // nonzero StreamID
    [InlineData(38, 1)] // incorrect PreviousTagSize
    public async Task RejectsMalformedFramingWithoutLeakingBudget(int offset, byte value)
    {
        byte[] wire = File((FlvTagType.Video, 0, Video(true)));
        wire[offset] = value;
        using var input = new MemoryStream(wire);
        var budget = new FlvMemoryBudget(1024 * 1024);
        var reader = new FlvTagReader(input, budget, new MonotonicMediaClock());
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await reader.ReadHeaderAsync(CancellationToken.None);
            using var tag = await reader.ReadTagAsync(CancellationToken.None);
        });
        Assert.Equal(0, budget.UsedBytes);
    }

    [Fact]
    public async Task TruncationAndOversizedLengthFailBeforeUnboundedAllocation()
    {
        byte[] wire = File((FlvTagType.Video, 0, Video(true)));
        var budget = new FlvMemoryBudget(1024 * 1024);
        using (var input = new MemoryStream(wire[..^2]))
        {
            var reader = new FlvTagReader(input, budget, new MonotonicMediaClock());
            await reader.ReadHeaderAsync(CancellationToken.None);
            await Assert.ThrowsAsync<EndOfStreamException>(async () => await reader.ReadTagAsync(CancellationToken.None));
        }
        Assert.Equal(0, budget.UsedBytes);
        wire[14] = 0xFF;
        using var oversized = new MemoryStream(wire);
        var oversizedReader = new FlvTagReader(oversized, budget, new MonotonicMediaClock());
        await oversizedReader.ReadHeaderAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await oversizedReader.ReadTagAsync(CancellationToken.None));
        Assert.Equal(0, budget.PeakBytes - (Video(true).Length + 128));
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
