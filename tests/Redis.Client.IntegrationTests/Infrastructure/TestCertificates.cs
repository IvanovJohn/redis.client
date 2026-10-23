using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Redis.Client.IntegrationTests.Infrastructure;

/// <summary>
/// Throw-away PKI for TLS tests: a CA, a server certificate for localhost and a client certificate.
/// </summary>
public sealed class TestCertificates : IDisposable
{
    private TestCertificates(X509Certificate2 ca, X509Certificate2 server, X509Certificate2 client)
    {
        Ca = ca;
        Server = server;
        Client = client;
    }

    public X509Certificate2 Ca { get; }

    public X509Certificate2 Server { get; }

    public X509Certificate2 Client { get; }

    public static TestCertificates Create()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=Redis.Client Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        var ca = caRequest.CreateSelfSigned(notBefore, notAfter);

        var server = Issue(ca, "CN=localhost", notBefore, notAfter, serverAuth: true);
        var client = Issue(ca, "CN=redis-client", notBefore, notAfter, serverAuth: false);
        return new TestCertificates(ca, server, client);
    }

    private static X509Certificate2 Issue(X509Certificate2 ca, string subject, DateTimeOffset notBefore, DateTimeOffset notAfter, bool serverAuth)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usage = serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2";
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], false));
        if (serverAuth)
        {
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
        }

        var serial = RandomNumberGenerator.GetBytes(16);
        using var issued = request.Create(ca, notBefore, notAfter, serial);
        return issued.CopyWithPrivateKey(key);
    }

    public static byte[] CertificatePem(X509Certificate2 certificate) =>
        System.Text.Encoding.ASCII.GetBytes(certificate.ExportCertificatePem());

    public static byte[] PrivateKeyPem(X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("Certificate has no RSA private key.");
        return System.Text.Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem());
    }

    public void Dispose()
    {
        Ca.Dispose();
        Server.Dispose();
        Client.Dispose();
    }
}
