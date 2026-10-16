using Temhalo.Infrastructure.Windows.Processes;
using Temhalo.Infrastructure.Windows.Accounts;

namespace Temhalo.Infrastructure.Windows.Settings;

public sealed record DestinationSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Sortie";
    public bool Enabled { get; init; } = true;
    public int Provider { get; init; } = 3;
    public string ServerUrl { get; init; } = "";
    public string StreamKey { get; init; } = "";
    public bool RememberKey { get; init; } = true;
    public ConnectedAccount? Account { get; init; }
    public void Validate(bool requireKey = false)
    {
        if (!Guid.TryParseExact(Id, "N", out _) || Name is null || Name.Trim().Length is < 1 or > 40 ||
            Name.Any(char.IsControl) || Provider is < 0 or > 6 || ServerUrl is null || StreamKey is null ||
            ServerUrl.Length > 2048 || StreamKey.Length > 4096)
            throw new ArgumentException("Destination invalide : nom, identifiant ou plateforme.");
        if (Account is not null)
        {
            Account.Validate();
            if ((int)Account.Provider != Provider) throw new ArgumentException("Le compte ne correspond pas à la plateforme.");
            if (requireKey && (Account.AccessToken.Length == 0 && Account.RefreshToken.Length == 0 ||
                Account.Provider == StreamingProvider.YouTube && Account.BroadcastId.Length == 0))
                throw new ArgumentException("Reconnectez le compte et choisissez le live YouTube si nécessaire.");
        }
        else if (Enabled || ServerUrl.Length > 0)
            _ = new RelayDestination(ServerUrl, requireKey ? StreamKey : StreamKey.Length > 0 ? StreamKey : "validation");
    }
    public override string ToString() => "Destination [secrets masqués]";
}
