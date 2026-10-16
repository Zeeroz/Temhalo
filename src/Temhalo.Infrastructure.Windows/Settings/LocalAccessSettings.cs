using System.Security.Cryptography;

namespace Temhalo.Infrastructure.Windows.Settings;

public sealed record LocalAccessSettings
{
    public int Port { get; init; } = 43120;
    public string DockToken { get; init; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public void Validate()
    {
        if (Port is < 1024 or > 65535 || !ValidToken(DockToken))
            throw new ArgumentException("Accès local invalide : port ou liens privés.");
    }
    private static bool ValidToken(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public override string ToString() => "Accès local [secrets masqués]";
}
