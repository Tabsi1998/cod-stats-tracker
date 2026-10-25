using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CodStats.Desktop.Updates;

namespace CodStats.Desktop.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly TempFolder temp = new();
    private readonly Dictionary<string, byte[]> served = [];

    private string PublicKey => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>Signed as the release workflow does it: openssl dgst -sha256 -sign, DER, then Base64.</summary>
    private string Sign(byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    private static byte[] Manifest(string version, string installer, byte[] content) =>
        Encoding.UTF8.GetBytes($$"""{ "version": "{{version}}", "installer": "{{installer}}", "sha256": "{{Convert.ToHexStringLower(SHA256.HashData(content))}}" }""");

    private void Publish(string version, byte[] installer, string name = "CodStats-Setup-win-x64.exe")
    {
        var manifest = Manifest(version, name, installer);
        served[UpdateService.ManifestFile] = manifest;
        served[UpdateService.ManifestFile + ".sig"] = Encoding.ASCII.GetBytes(Sign(manifest));
        served[name] = installer;
    }

    private UpdateService Service(string? publicKey = null, string current = "0.2.2") =>
        new(new Uri("https://codstats.test/"), publicKey ?? PublicKey, Version.Parse(current), temp.File("updates"), new Files(served));

    [Fact]
    public void Valid_signature_gives_the_manifest()
    {
        var manifest = Manifest("0.3.0", "CodStats-Setup-win-x64.exe", [1, 2, 3]);

        var verified = UpdateVerifier.Verify(manifest, Sign(manifest), PublicKey);

        Assert.Equal("0.3.0", verified?.Version);
    }

    [Fact]
    public void Changed_manifest_or_other_key_is_rejected()
    {
        var manifest = Manifest("0.3.0", "CodStats-Setup-win-x64.exe", [1, 2, 3]);
        var signature = Sign(manifest);
        var changed = Manifest("9.9.9", "CodStats-Setup-win-x64.exe", [1, 2, 3]);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Null(UpdateVerifier.Verify(changed, signature, PublicKey));
        Assert.Null(UpdateVerifier.Verify(manifest, signature, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())));
        Assert.Null(UpdateVerifier.Verify(manifest, "not base64!", PublicKey));
        Assert.Null(UpdateVerifier.Verify(manifest, signature, ""));
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("C:\\evil.exe")]
    [InlineData("setup.msi")]
    public void Signed_but_unsafe_installer_names_are_rejected(string name)
    {
        var manifest = Manifest("0.3.0", name.Replace("\\", "\\\\", StringComparison.Ordinal), [1]);

        Assert.Null(UpdateVerifier.Verify(manifest, Sign(manifest), PublicKey));
    }

    [Theory]
    [InlineData("0.3.0", "0.2.2.0", true)]
    [InlineData("0.2.10", "0.2.9.0", true)]
    [InlineData("0.2.2", "0.2.2.0", false)]
    [InlineData("0.2.1", "0.2.2.0", false)]
    public void Only_a_later_version_is_an_update(string offered, string running, bool expected)
    {
        var manifest = new UpdateManifest(offered, "CodStats-Setup-win-x64.exe", new string('a', 64));

        Assert.Equal(expected, UpdateVerifier.IsNewer(manifest, Version.Parse(running)));
    }

    [Fact]
    public async Task Check_finds_a_newer_signed_release()
    {
        Publish("0.3.0", [1, 2, 3]);
        using var service = Service();

        var found = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("0.3.0", found?.Version);
        Assert.Same(found, service.Available);
    }

    [Fact]
    public async Task Check_offers_nothing_without_key_signature_or_newer_version()
    {
        Publish("0.2.2", [1]);
        using (var same = Service())
        {
            Assert.Null(await same.CheckAsync(CancellationToken.None));
        }
        Publish("0.3.0", [1]);
        using (var noKey = Service(publicKey: ""))
        {
            Assert.False(noKey.Enabled);
            Assert.Null(await noKey.CheckAsync(CancellationToken.None));
        }
        served.Remove(UpdateService.ManifestFile + ".sig");
        using var unsigned = Service();
        Assert.Null(await unsigned.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Download_keeps_only_an_installer_with_the_signed_hash()
    {
        Publish("0.3.0", [1, 2, 3]);
        using var service = Service();
        var update = await service.CheckAsync(CancellationToken.None);

        var path = await service.DownloadAsync(update!, CancellationToken.None);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path));

        served["CodStats-Setup-win-x64.exe"] = [6, 6, 6];
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(update!, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(temp.File("updates")));
    }

    public void Dispose()
    {
        key.Dispose();
        temp.Dispose();
    }

    /// <summary>The server's /downloads folder: a file by name, or 404.</summary>
    private sealed class Files(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.AbsolutePath["/downloads/".Length..];
            return Task.FromResult(files.TryGetValue(name, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
