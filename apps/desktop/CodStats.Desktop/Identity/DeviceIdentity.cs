using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodStats.Desktop.Identity;

/// <summary>
/// The device's ECDSA P-256 key and the ids the server gave it. The private key is stored encrypted with DPAPI
/// for the current Windows user, so another user or another PC cannot use the copied file.
/// </summary>
public sealed class DeviceIdentity : IDisposable
{
    private static readonly byte[] Entropy = "CodStats.DeviceKey.v1"u8.ToArray();

    private DeviceIdentity(ECDsa key, Guid? deviceId, Guid? profileId)
    {
        Key = key;
        DeviceId = deviceId;
        ProfileId = profileId;
    }

    public ECDsa Key { get; }
    public Guid? DeviceId { get; private set; }
    public Guid? ProfileId { get; private set; }
    public bool IsRegistered => DeviceId is not null;
    public string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    /// <summary>Loads the identity, or creates a new key when there is none yet.</summary>
    public static DeviceIdentity LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path))
                ?? throw new InvalidDataException("identity file is empty");
            var key = ECDsa.Create();
            var pkcs8 = ProtectedData.Unprotect(Convert.FromBase64String(stored.ProtectedKey), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                key.ImportPkcs8PrivateKey(pkcs8, out _);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }
            return new DeviceIdentity(key, stored.DeviceId, stored.ProfileId);
        }

        var identity = new DeviceIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256), null, null);
        identity.Save(path);
        return identity;
    }

    public void SetRegistration(Guid deviceId, Guid profileId, string path)
    {
        DeviceId = deviceId;
        ProfileId = profileId;
        Save(path);
    }

    /// <summary>A fresh key for this PC, e.g. before moving a profile here with a recovery key.</summary>
    public static DeviceIdentity CreateNew(string path)
    {
        File.Delete(path);
        return LoadOrCreate(path);
    }

    private void Save(string path)
    {
        var pkcs8 = Key.ExportPkcs8PrivateKey();
        try
        {
            var stored = new Stored(Convert.ToBase64String(ProtectedData.Protect(pkcs8, Entropy, DataProtectionScope.CurrentUser)), DeviceId, ProfileId);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(stored));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    public void Dispose() => Key.Dispose();

    private sealed record Stored(string ProtectedKey, Guid? DeviceId, Guid? ProfileId);
}
