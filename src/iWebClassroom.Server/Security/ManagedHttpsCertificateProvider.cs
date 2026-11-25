using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace IWebClassroom.Server.Security;

public sealed class ManagedHttpsCertificateProvider : IDisposable
{
    private const X509KeyStorageFlags PrivateKeyFlags =
        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable;
    private readonly object _gate = new();
    private readonly string _certificateDirectory;
    private readonly string _rootPath;
    private readonly string _serverPath;
    private readonly string _networkIdentityPath;
    private X509Certificate2 _rootCertificate;
    private X509Certificate2 _serverCertificate;

    public ManagedHttpsCertificateProvider(string contentRoot, string configuredDataDirectory)
    {
        var dataDirectory = Path.IsPathRooted(configuredDataDirectory)
            ? configuredDataDirectory
            : Path.Combine(contentRoot, configuredDataDirectory);
        _certificateDirectory = Path.Combine(dataDirectory, "certificates");
        _rootPath = Path.Combine(_certificateDirectory, "iwebclassroom-root.pfx");
        _serverPath = Path.Combine(_certificateDirectory, "iwebclassroom-server.pfx");
        _networkIdentityPath = Path.Combine(_certificateDirectory, "network-identity.txt");
        Directory.CreateDirectory(_certificateDirectory);
        (_rootCertificate, _serverCertificate) = LoadOrCreate();
    }

    public X509Certificate2 Current
    {
        get { lock (_gate) return _serverCertificate; }
    }

    public string RootThumbprint
    {
        get { lock (_gate) return _rootCertificate.Thumbprint; }
    }

    public byte[] ExportRootCertificate()
    {
        lock (_gate) return _rootCertificate.Export(X509ContentType.Cert);
    }

    public bool RefreshIfRequired()
    {
        lock (_gate)
        {
            var identity = NetworkIdentity();
            var savedIdentity = File.Exists(_networkIdentityPath) ? File.ReadAllText(_networkIdentityPath) : string.Empty;
            if (_serverCertificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30) &&
                string.Equals(identity, savedIdentity, StringComparison.Ordinal))
            {
                return false;
            }

            using var generated = CreateServerCertificate(_rootCertificate);
            WriteCertificate(_serverPath, generated);
            var replacement = LoadPrivateCertificate(_serverPath);
            File.WriteAllText(_networkIdentityPath, identity, Encoding.UTF8);
            var previous = _serverCertificate;
            _serverCertificate = replacement;
            previous.Dispose();
            return true;
        }
    }

    private (X509Certificate2 Root, X509Certificate2 Server) LoadOrCreate()
    {
        X509Certificate2 root;
        if (File.Exists(_rootPath))
        {
            root = X509CertificateLoader.LoadPkcs12FromFile(_rootPath, null, PrivateKeyFlags);
        }
        else
        {
            using var generatedRoot = CreateRootCertificate();
            WriteCertificate(_rootPath, generatedRoot);
            root = LoadPrivateCertificate(_rootPath);
        }

        var identity = NetworkIdentity();
        X509Certificate2? server = null;
        if (File.Exists(_serverPath) && File.Exists(_networkIdentityPath) &&
            string.Equals(File.ReadAllText(_networkIdentityPath), identity, StringComparison.Ordinal))
        {
            server = X509CertificateLoader.LoadPkcs12FromFile(_serverPath, null, PrivateKeyFlags);
            if (server.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(30))
            {
                server.Dispose();
                server = null;
            }
        }

        if (server is null)
        {
            using var generatedServer = CreateServerCertificate(root);
            WriteCertificate(_serverPath, generatedServer);
            server = LoadPrivateCertificate(_serverPath);
            File.WriteAllText(_networkIdentityPath, identity, Encoding.UTF8);
        }

        return (root, server);
    }

    private static X509Certificate2 LoadPrivateCertificate(string path) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, null, PrivateKeyFlags);

    private static X509Certificate2 CreateRootCertificate()
    {
        using var key = RSA.Create(4096);
        var request = new CertificateRequest(
            "CN=iWebClassroom Local School CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 root)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={Environment.MachineName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        foreach (var dnsName in DnsNames()) san.AddDnsName(dnsName);
        foreach (var address in IpAddresses()) san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());

        var serial = RandomNumberGenerator.GetBytes(16);
        var expires = DateTimeOffset.UtcNow.AddDays(397);
        if (expires >= root.NotAfter) expires = root.NotAfter.AddDays(-1);
        using var issued = request.Create(root, DateTimeOffset.UtcNow.AddDays(-1), expires, serial);
        return issued.CopyWithPrivateKey(key);
    }

    private static IEnumerable<string> DnsNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", Environment.MachineName };
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName()).HostName;
            if (!string.IsNullOrWhiteSpace(host)) names.Add(host);
        }
        catch (SocketException) { }
        return names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<IPAddress> IpAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(item => item.OperationalStatus == OperationalStatus.Up))
        {
            foreach (var address in network.GetIPProperties().UnicastAddresses.Select(item => item.Address))
            {
                if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal)
                {
                    addresses.Add(address);
                }
            }
        }
        return addresses.OrderBy(value => value.ToString(), StringComparer.Ordinal);
    }

    private static string NetworkIdentity()
    {
        var value = string.Join('|', DnsNames().Concat(IpAddresses().Select(value => value.ToString())));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static void WriteCertificate(string path, X509Certificate2 certificate)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, certificate.Export(X509ContentType.Pfx));
        File.Move(temporary, path, true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _serverCertificate.Dispose();
            _rootCertificate.Dispose();
        }
    }
}
