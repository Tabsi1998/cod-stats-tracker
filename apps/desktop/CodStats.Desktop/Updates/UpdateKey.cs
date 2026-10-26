namespace CodStats.Desktop.Updates;

/// <summary>
/// The public half of the release key (SubjectPublicKeyInfo, Base64). The private half is the GitHub secret
/// UPDATE_SIGNING_KEY and signs CodStats-update.json in the release workflow. Empty: this build offers no updates.
/// How to create the pair: docs/updates.md.
/// </summary>
public static class UpdateKey
{
    public const string PublicKey = "";
}
