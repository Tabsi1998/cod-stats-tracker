using CodStats.Contracts;

namespace CodStats.Api.Data;

public sealed class Profile
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public NameStatus NameStatus { get; set; }
    public bool IsPublic { get; set; }

    /// <summary>SHA-256 of the normalised recovery key; the key itself is never stored.</summary>
    public byte[]? RecoveryKeyHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Device> Devices { get; } = [];
    public List<NameHistory> Names { get; } = [];
    public List<Match> Matches { get; } = [];
}

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    /// <summary>SubjectPublicKeyInfo of the device's ECDSA P-256 key.</summary>
    public byte[] PublicKey { get; set; } = [];

    public string? ClientVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class NameHistory
{
    public long Id { get; set; }
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class Match
{
    /// <summary>The UUID the client gave the match; the idempotency key of uploads.</summary>
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }
    public Game Game { get; set; }
    public string? Mode { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
    public MatchResult? Result { get; set; }
    public int? Placement { get; set; }
    public string? Map { get; set; }
    public int? Score { get; set; }
    public MatchStatus Status { get; set; }

    /// <summary>
    /// Shown on the website. Set from the profile's choice when the match arrives; switching the profile to public
    /// shows all its matches, switching it off keeps the ones already shown and hides only what comes after.
    /// </summary>
    public bool IsPublic { get; set; }
    public int? Eliminations { get; set; }
    public int? TeamScore { get; set; }
    public int? EnemyScore { get; set; }
    public int? Xp { get; set; }
    public int? Level { get; set; }

    /// <summary>Mode values such as captures; stored as jsonb, since every mode has others.</summary>
    public Dictionary<string, int>? ModeStats { get; set; }

    /// <summary>Players on the final scoreboard and the player's place by score; no other player's name or values.</summary>
    public int? LobbySize { get; set; }
    public int? LobbyRank { get; set; }

    /// <summary>The report's top weapon and its class ("HAN 86", "STURMGEWEHR").</summary>
    public string? Weapon { get; set; }
    public string? WeaponClass { get; set; }

    public double Confidence { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public string? RecognizerVersion { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A nonce already used by a signer, kept until its timestamp could no longer be accepted.</summary>
public sealed class UsedNonce
{
    public string Signer { get; set; } = "";
    public string Nonce { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
