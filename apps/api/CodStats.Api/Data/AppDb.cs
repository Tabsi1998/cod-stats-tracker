using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodStats.Api.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<NameHistory> NameHistory => Set<NameHistory>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<UsedNonce> UsedNonces => Set<UsedNonce>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by their wire names, so the database reads like the API.
        configurationBuilder.Properties<Game>().HaveConversion<WireNameConverter<Game>>().HaveMaxLength(32);
        configurationBuilder.Properties<MatchResult>().HaveConversion<WireNameConverter<MatchResult>>().HaveMaxLength(16);
        configurationBuilder.Properties<ReviewStatus>().HaveConversion<WireNameConverter<ReviewStatus>>().HaveMaxLength(16);
        configurationBuilder.Properties<NameStatus>().HaveConversion<WireNameConverter<NameStatus>>().HaveMaxLength(16);
        configurationBuilder.Properties<MatchStatus>().HaveConversion<WireNameConverter<MatchStatus>>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>(profile =>
        {
            profile.Property(p => p.DisplayName).HasMaxLength(DisplayNames.MaxLength);
            profile.HasIndex(p => p.RecoveryKeyHash).IsUnique();
            profile.HasIndex(p => p.DisplayName);
            profile.HasMany(p => p.Devices).WithOne(d => d.Profile).HasForeignKey(d => d.ProfileId).OnDelete(DeleteBehavior.Cascade);
            profile.HasMany(p => p.Names).WithOne().HasForeignKey(n => n.ProfileId).OnDelete(DeleteBehavior.Cascade);
            profile.HasMany(p => p.Matches).WithOne().HasForeignKey(m => m.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Device>(device =>
        {
            device.Property(d => d.ClientVersion).HasMaxLength(32);
            device.HasIndex(d => d.PublicKey).IsUnique();
        });

        modelBuilder.Entity<NameHistory>(name =>
        {
            name.Property(n => n.Name).HasMaxLength(DisplayNames.MaxLength);
            name.HasIndex(n => new { n.ProfileId, n.Name }).IsUnique();
        });

        modelBuilder.Entity<Match>(match =>
        {
            match.Property(m => m.Id).ValueGeneratedNever();
            match.Property(m => m.Mode).HasMaxLength(MatchRules.MaxTextLength);
            match.Property(m => m.Map).HasMaxLength(MatchRules.MaxTextLength);
            match.Property(m => m.RecognizerVersion).HasMaxLength(MatchRules.MaxTextLength);
            // Rows from before the status existed are matches played to the end.
            match.Property(m => m.Status).HasDefaultValue(MatchStatus.Completed).HasSentinel((MatchStatus)(-1));
            match.Property(m => m.ModeStats).HasColumnType("jsonb").HasConversion(
                new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Dictionary<string, int>?, string?>(
                    stats => stats == null || stats.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(stats, (System.Text.Json.JsonSerializerOptions?)null),
                    json => json == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json, (System.Text.Json.JsonSerializerOptions?)null)),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, int>?>(
                    (a, b) => a == null ? b == null : b != null && a.Count == b.Count && !a.Except(b).Any(),
                    stats => stats == null ? 0 : stats.Aggregate(0, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value)),
                    stats => stats == null ? null : new Dictionary<string, int>(stats)));
            match.HasIndex(m => new { m.ProfileId, m.PlayedAt });
            match.HasIndex(m => new { m.Game, m.ProfileId });
        });

        modelBuilder.Entity<UsedNonce>(nonce =>
        {
            nonce.HasKey(n => new { n.Signer, n.Nonce });
            nonce.Property(n => n.Signer).HasMaxLength(80);
            nonce.Property(n => n.Nonce).HasMaxLength(RequestSigning.MaxNonceLength);
            nonce.HasIndex(n => n.ExpiresAt);
        });
    }
}

public sealed class WireNameConverter<T>() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string>(
    value => WireNames.Of(value),
    text => Parse(text)) where T : struct, Enum
{
    private static T Parse(string text) =>
        WireNames.TryParse<T>(text, out var value) ? value : throw new InvalidOperationException($"unknown {typeof(T).Name} '{text}'");
}

/// <summary>Lets "dotnet ef migrations add" build the model without a running database.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDb>()
            .UseNpgsql("Host=localhost;Database=codstats")
            .UseSnakeCaseNamingConvention()
            .Options);
}
