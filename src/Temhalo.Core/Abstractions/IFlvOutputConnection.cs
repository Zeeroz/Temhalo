namespace Temhalo.Core.Abstractions;

/// <summary>Control-path lifecycle for one output. The consumer alone opens/writes/closes it.</summary>
public interface IFlvOutputConnection
{
    ValueTask<Stream> OpenAsync(CancellationToken cancellationToken);
    ValueTask CloseAsync(bool failed, CancellationToken cancellationToken);
}
