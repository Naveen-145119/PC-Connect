using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PCConnect.Core;

public static class Security
{
    public static X509Certificate2 CreateIdentity()
    {
        using var key = RSA.Create(3072);
        var req = new CertificateRequest("CN=PC Connect", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var created = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        byte[] pfx = created.Export(X509ContentType.Pfx);
        try { return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    public static string Fingerprint(X509Certificate2 certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public static string Normalize(string fingerprint)
    {
        string result = fingerprint.Replace(" ", "").Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        if (result.Length != 64 || !result.All(Uri.IsHexDigit)) throw new ArgumentException("Enter the other laptop's complete 64-character fingerprint.");
        return result;
    }
    public static bool Matches(X509Certificate? cert, string pin)
    {
        if (cert is null) return false;
        using var peer = new X509Certificate2(cert);
        try
        {
            return DateTime.UtcNow >= peer.NotBefore.ToUniversalTime() && DateTime.UtcNow < peer.NotAfter.ToUniversalTime()
                && CryptographicOperations.FixedTimeEquals(peer.GetCertHash(HashAlgorithmName.SHA256), Convert.FromHexString(Normalize(pin)));
        }
        catch (ArgumentException) { return false; }
    }
    public static async Task<SslStream> Authenticate(TcpClient tcp, X509Certificate2 identity, string peerPin, bool server, CancellationToken ct)
    {
        tcp.NoDelay = true;
        var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => Matches(cert, peerPin));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            if (server)
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = identity, ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    AllowRenegotiation = false
                }, deadline.Token);
            else
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "PCConnect", ClientCertificates = new X509CertificateCollection { identity },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    AllowRenegotiation = false
                }, deadline.Token);
            if (!ssl.IsMutuallyAuthenticated || !ssl.IsEncrypted) throw new AuthenticationException("Mutual TLS authentication required.");
            return ssl;
        }
        catch { ssl.Dispose(); throw; }
    }
}
