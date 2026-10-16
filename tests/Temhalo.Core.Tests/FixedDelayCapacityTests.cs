using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Xunit;

namespace Temhalo.Core.Tests;

public sealed class FixedDelayCapacityTests
{
    [Fact]
    public async Task HoldsFiveMinutesAtEightMegabitsWithActualPayloadAllocations()
    {
        // Virtual time removes a five-minute wait, but these are real 300 MB of owned payloads.
        // NAL framing is synthetic; decodability is covered separately by the native integration.
        var clock = new ManualMediaClock();
        using var input = new CapacitySource(clock);
        var engine = new FixedDelayMediaTransfer(new()
        {
            DelaySeconds = 300, MaximumBufferBytes = 512L * 1024 * 1024, WarningUsageRatio = 0.5
        }, clock);
        Task running = engine.TransferAsync(input, Stream.Null, CancellationToken.None);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.InputCompleted);
        Assert.Equal(0, engine.Snapshot.EmittedMediaTags);
        Assert.InRange(engine.Snapshot.BufferBytes, 300_000_000, 310_000_000);
        Assert.True(engine.Snapshot.BufferWarning);
        clock.AdvanceTo(600_000);
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(300, engine.Snapshot.EmittedMediaTags);
        Assert.True(engine.Snapshot.WarningObserved);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
        Assert.True(engine.Snapshot.MinimumMediaAgeMilliseconds >= 300_000);
    }

    private sealed class CapacitySource : Stream
    {
        private readonly ManualMediaClock _clock;
        private readonly byte[] _prefix = FlvTestData.File((FlvTagType.Video, 0, FlvTestData.Avc), (FlvTagType.Audio, 0, FlvTestData.Aac));
        private readonly byte[] _frame;
        private int _prefixOffset, _frameOffset, _frames;

        public CapacitySource(ManualMediaClock clock)
        {
            _clock = clock;
            byte[] payload = new byte[1_000_000];
            payload[0] = 0x17; payload[1] = 1;
            int nalLength = payload.Length - 9;
            payload[5] = (byte)(nalLength >> 24); payload[6] = (byte)(nalLength >> 16);
            payload[7] = (byte)(nalLength >> 8); payload[8] = (byte)nalLength; payload[9] = 0x65;
            using var packet = new MemoryStream();
            FlvTestData.WriteTag(packet, FlvTagType.Video, 0, payload);
            _frame = packet.ToArray();
            _frameOffset = _frame.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_prefixOffset < _prefix.Length)
            {
                int prefixCount = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
                _prefix.AsMemory(_prefixOffset, prefixCount).CopyTo(buffer); _prefixOffset += prefixCount;
                return ValueTask.FromResult(prefixCount);
            }
            if (_frameOffset == _frame.Length)
            {
                if (_frames == 300) return ValueTask.FromResult(0);
                uint timestamp = (uint)(++_frames * 1000);
                _frame[4] = (byte)(timestamp >> 16); _frame[5] = (byte)(timestamp >> 8);
                _frame[6] = (byte)timestamp; _frame[7] = (byte)(timestamp >> 24);
                _clock.AdvanceTo(timestamp);
                _frameOffset = 0;
            }
            int count = Math.Min(buffer.Length, _frame.Length - _frameOffset);
            _frame.AsMemory(_frameOffset, count).CopyTo(buffer); _frameOffset += count;
            return ValueTask.FromResult(count);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
