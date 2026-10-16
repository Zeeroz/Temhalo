namespace Temhalo.Core.Abstractions;

/// <summary>Transfers encoded media independently of the native processes and UI.</summary>
public interface IMediaTransfer
{
    Task TransferAsync(Stream input, Stream output, CancellationToken cancellationToken);
    long BytesTransferred { get; }
}
