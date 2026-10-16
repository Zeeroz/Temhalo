using Microsoft.Extensions.Logging;
using Temhalo.Core.Abstractions;
using Temhalo.Core.Outputs;

namespace Temhalo.Infrastructure.Windows.Processes;

internal sealed class RelayOutputGroup : IAsyncDisposable
{
    private readonly RelayOutput[] _specifications;
    private readonly SupervisedFlvOutput[] _connections;
    private readonly FanoutFlvOutput? _fanout;
    public IFlvOutputConnection Connection { get; }
    public long ReservedBytes => _fanout is null ? 0 : FanoutFlvOutput.ReservedBytes(_connections.Length);
    public string? FirstFile => _connections[0].State.File;
    public void CatchUp(bool onlyBehind) => _fanout?.CatchUp(onlyBehind);
    public IReadOnlyList<RelayOutputSnapshot> Snapshots(long commonBytes)
    {
        var branches = _fanout?.Snapshots;
        return _connections.Select((connection, index) =>
        {
            var state = connection.State;
            var branch = branches?[index]; var specification = _specifications[index];
            return new RelayOutputSnapshot(specification.Id, specification.Name, state.Pid, state.Attempt,
                state.Ready && (branch is null || branch.State == "Relaying"), state.Reconnecting,
                branch?.BytesWritten ?? commonBytes, branch?.QueuedBytes ?? 0, branch?.DroppedTags ?? 0,
                branch?.ExtraDelayMilliseconds ?? 0, branch?.State ?? (state.Ready ? "Relaying" : state.Reconnecting ? "RetryingOutput" : "WaitingForMedia"));
        }).ToArray();
    }
    public RelayOutputGroup(LocalRelayOptions options, WindowsProcessJob job, ILogger logger)
    {
        _specifications = options.Outputs?.ToArray() ?? [new("primary", options.Destination is null ? "Fichier local" : "Sortie principale", options.Destination)];
        if (_specifications.Length is < 1 or > 4 || _specifications.Select(output => output.Id).Distinct().Count() != _specifications.Length ||
            _specifications.Any(output => output.Id.Length is < 1 or > 40 || output.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                output.Name.Length is < 1 or > 40 || output.Name.Any(char.IsControl)))
            throw new ArgumentException("Une à quatre sorties distinctes sont requises.");
        _connections = _specifications.Select(specification =>
        {
            string directory = _specifications.Length == 1 ? options.OutputDirectory : Path.Combine(options.OutputDirectory, "output-" + specification.Id);
            Directory.CreateDirectory(directory);
            return new SupervisedFlvOutput(options with { Destination = specification.Destination, Outputs = null, OutputDirectory = directory }, job, logger);
        }).ToArray();
        if (_connections.Length == 1) Connection = _connections[0];
        else
        {
            _fanout = new FanoutFlvOutput(_connections.Select((connection, index) =>
                new FanoutTarget(_specifications[index].Id, connection, () => connection.State.Ready)).ToArray(),
                diagnostic: message => logger.LogWarning("{Event}", message));
            Connection = _fanout;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_fanout is not null) await _fanout.DisposeAsync().ConfigureAwait(false);
        foreach (var connection in _connections) await connection.DisposeAsync().ConfigureAwait(false);
    }
}
