using Temhalo.Core.Flv;

namespace Temhalo.Core.Abstractions;

/// <summary>Optional packet path. A sink must retain any tag it keeps after WriteTagAsync returns.</summary>
public interface IFlvPacketSink
{
    ValueTask WriteHeaderAsync(FlvHeader header, CancellationToken token);
    ValueTask WriteTagAsync(FlvTag tag, uint timestamp, CancellationToken token);
}
