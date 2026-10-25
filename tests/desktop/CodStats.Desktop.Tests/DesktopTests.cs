using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using CodStats.Contracts;
using CodStats.Desktop.Identity;
using CodStats.Desktop.Recognition;
using CodStats.Desktop.Storage;
using CodStats.Desktop.Sync;

namespace CodStats.Desktop.Tests;

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codstats-tests-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file handle can linger briefly on Windows; the temp folder is cleaned up by the OS.
        }
    }
}

internal static class Local
{
    private static int minutes;

    public static LocalMatch Mw4(int kills, int deaths, ReviewStatus review = ReviewStatus.Auto) =>
        new(Guid.NewGuid(), Game.Mw4Multiplayer, "TDM", DateTimeOffset.UtcNow.AddMinutes(-10 * Interlocked.Increment(ref minutes)),
            kills, deaths, null, MatchResult.Win, null, null, null, 0.9, review, "test", SyncState.Waiting, null);
}

public class MatchStoreTests
{
    [Fact]
    public void Stores_and_reads_matches_with_nulls_intact()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        var match = Local.Mw4(10, 4) with { Assists = null, Map = null };
        Assert.True(store.Add(match));
        var read = Assert.Single(store.All());
        Assert.Equal(match.Id, read.Id);
        Assert.Null(read.Assists);
        Assert.Null(read.Map);
        Assert.Equal(10, read.Kills);
    }

    [Fact]
    public void The_same_result_read_twice_is_stored_once()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        var match = Local.Mw4(10, 4);
        Assert.True(store.Add(match));
        Assert.False(store.Add(match));
        Assert.False(store.Add(match with { Id = Guid.NewGuid(), PlayedAt = match.PlayedAt.AddSeconds(30) }));
        Assert.True(store.Add(match with { Id = Guid.NewGuid(), PlayedAt = match.PlayedAt.AddMinutes(20) }));
    }

    [Fact]
    public void Pending_matches_are_not_uploaded()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        store.Add(Local.Mw4(1, 1));
        store.Add(Local.Mw4(2, 2, ReviewStatus.Pending));
        Assert.Single(store.ToUpload(50));
    }

    [Fact]
    public void Correcting_a_synced_match_queues_a_correction()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        var match = Local.Mw4(1, 1);
        store.Add(match);
        store.SetSyncState(match.Id, SyncState.Synced);
        store.Correct(match with { Kills = 5 });
        var read = Assert.Single(store.CorrectionsToSend());
        Assert.Equal(5, read.Kills);
        Assert.Equal(ReviewStatus.Corrected, read.ReviewStatus);
        Assert.Empty(store.ToUpload(50));
    }
}

public class MatchStoreUpgradeTests
{
    [Fact]
    public void Status_and_details_are_stored_and_read_back()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        var details = new MatchDetails(11, 300, 0, 12410, 26, new Dictionary<string, int> { ["captures"] = 2 });
        var left = Local.Mw4(3, 5) with { Result = null, Status = MatchStatus.Left };
        store.Add(left);
        store.Add(Local.Mw4(8, 8) with { Details = details });

        var read = store.All().ToDictionary(m => m.Kills!.Value);
        Assert.Equal(MatchStatus.Left, read[3].Status);
        Assert.Null(read[3].Details);
        Assert.Equal(12410, read[8].Details!.Xp);
        Assert.Equal(2, read[8].ToUpload().Details!.ModeStats!["captures"]);
    }

    [Fact]
    public void A_database_of_an_older_client_is_upgraded()
    {
        using var temp = new TempFolder();
        var path = temp.File("old.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // The table as v0.1.x created it, with one match in it.
            command.CommandText = """
                CREATE TABLE matches (id TEXT PRIMARY KEY, game TEXT NOT NULL, mode TEXT, played_at TEXT NOT NULL, kills INTEGER,
                    deaths INTEGER, assists INTEGER, result TEXT, placement INTEGER, map TEXT, score INTEGER, confidence REAL NOT NULL,
                    review_status TEXT NOT NULL, recognizer_version TEXT, sync_state TEXT NOT NULL, sync_error TEXT, created_at TEXT NOT NULL);
                INSERT INTO matches VALUES ('8e5a6c1f-31b1-4a8b-9d1e-1c2b3a4d5e6f', 'mw4_mp', 'TDM', '2026-10-24T10:00:00.0000000+00:00',
                    25, 12, 5, 'win', NULL, NULL, 3585, 0.97, 'auto', 'ocr-0.1', 'Synced', NULL, '2026-10-24T10:05:00.0000000+00:00');
                """;
            command.ExecuteNonQuery();
        }

        var store = new MatchStore(path);
        store.Initialise();
        store.Initialise(); // a second start must not add the columns again

        var old = Assert.Single(store.All());
        Assert.Equal(MatchStatus.Completed, old.Status);
        Assert.Null(old.Details);
        Assert.True(store.Add(Local.Mw4(1, 1) with { Details = new MatchDetails(Xp: 900) }));
    }
}

public class IdentityTests
{
    [Fact]
    public void Key_survives_a_restart_and_is_not_stored_in_plain_text()
    {
        using var temp = new TempFolder();
        var path = temp.File("identity.json");
        string publicKey;
        using (var first = DeviceIdentity.LoadOrCreate(path))
        {
            first.SetRegistration(Guid.NewGuid(), Guid.NewGuid(), path);
            publicKey = first.PublicKey;
        }
        using var again = DeviceIdentity.LoadOrCreate(path);
        Assert.Equal(publicKey, again.PublicKey);
        Assert.True(again.IsRegistered);
        Assert.DoesNotContain("PRIVATE", File.ReadAllText(path), StringComparison.Ordinal);
    }
}

public class GameWatcherTests
{
    [Fact]
    public void Notices_the_game_start_and_stop()
    {
        var running = new List<string> { "explorer", "Battle.net", "codex" };
        using var watcher = new GameWatcher(() => running);
        watcher.Poll();
        Assert.Equal(TrackerState.WaitingForGame, watcher.State);
        running.Add("cod25-cod");
        watcher.Poll();
        Assert.Equal(TrackerState.GameRunning, watcher.State);
        watcher.SetEnabled(false);
        Assert.Equal(TrackerState.TrackingOff, watcher.State);
    }

    [Theory]
    [InlineData(1, 2, 1, true)] // the splash window was replaced by the game window: as seen live on 25.10.
    [InlineData(1, 1, 1, false)] // a moment without a frame on the same window
    [InlineData(1, 0, 1, false)] // the game is closing: nothing to switch to
    [InlineData(1, 1, RecognitionLoop.MaxMissedFrames, true)]
    public void Capture_starts_over_on_a_new_window_or_a_long_silence(int captured, int current, int missed, bool expected) =>
        Assert.Equal(expected, RecognitionLoop.NeedsNewCapture(captured, current, missed));
}

public class ProcessNameTests
{
    [Theory]
    [InlineData("cod25-cod", true)]   // Warzone / Black Ops 7, seen on a real PC in October 2026
    [InlineData("cod26-cod", true)]   // expected for MW4
    [InlineData("COD27-COD", true)]
    [InlineData("cod", true)]
    [InlineData("Battle.net", false)]
    [InlineData("codex", false)]
    [InlineData("cod25-cod-helper", false)]
    public void Recognises_cod_launcher_builds(string name, bool expected) => Assert.Equal(expected, GameWatcher.IsCodProcess(name));
}

public class ClanTagTests
{
    [Theory]
    [InlineData("[TLS] Tabsi98", "Tabsi98")]   // as the Warzone party list shows it
    [InlineData("[TLS]Tabsi98", "Tabsi98")]
    [InlineData("Tabsi98", "Tabsi98")]
    [InlineData("[Clan] ", "")]
    [InlineData("[VeryLongTag] Name", "[VeryLongTag] Name")]
    public void Clan_tags_are_removed(string shown, string expected) =>
        Assert.Equal(expected, CodStats.Contracts.DisplayNames.WithoutClanTag(shown));

    [Fact]
    public void A_clan_change_does_not_split_the_name()
    {
        var names = new NameTracker();
        foreach (var shown in new[] { "[TLS] Tabsi98", "[ABC] Tabsi98", "Tabsi98" })
        {
            names.NextMatch();
            names.Observe(new FieldReading(shown, 1));
        }
        Assert.Equal("Tabsi98", names.Confirmed);
    }
}

public class SyncTests
{
    [Fact]
    public async Task Registers_once_uploads_and_marks_results()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        var accepted = Local.Mw4(10, 2);
        var refused = Local.Mw4(11, 2);
        store.Add(accepted);
        store.Add(refused);
        using var identity = DeviceIdentity.LoadOrCreate(temp.File("id.json"));
        var server = new FakeServer(rejected: refused.Id);
        using var sync = new SyncService(store, identity, () => new ApiClient(new Uri("https://stats.test/"), identity, server), temp.File("id.json"));

        var status = await sync.RunAsync(new ProfileWish("Tester", true, true), CancellationToken.None);

        Assert.Equal(SyncPhase.Done, status.Phase);
        Assert.True(identity.IsRegistered);
        Assert.Equal(1, server.Registrations);
        Assert.Equal(SyncState.Synced, store.All().Single(m => m.Id == accepted.Id).SyncState);
        Assert.Equal(SyncState.Rejected, store.All().Single(m => m.Id == refused.Id).SyncState);
        Assert.All(server.Requests, r => Assert.True(r.Signed));
    }

    [Fact]
    public async Task Offline_keeps_matches_waiting()
    {
        using var temp = new TempFolder();
        var store = new MatchStore(temp.File("m.db"));
        store.Initialise();
        store.Add(Local.Mw4(10, 2));
        using var identity = DeviceIdentity.LoadOrCreate(temp.File("id.json"));
        using var sync = new SyncService(store, identity, () => new ApiClient(new Uri("https://stats.test/"), identity, new OfflineHandler()), temp.File("id.json"));

        var status = await sync.RunAsync(new ProfileWish(null, false, false), CancellationToken.None);

        Assert.Equal(SyncPhase.Offline, status.Phase);
        Assert.Equal(1, status.Waiting);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no connection");
    }

    /// <summary>Answers like the API would, enough to drive the sync logic.</summary>
    private sealed class FakeServer(Guid rejected) : HttpMessageHandler
    {
        private readonly Guid deviceId = Guid.NewGuid();
        private readonly Guid profileId = Guid.NewGuid();
        private string? name;
        private bool isPublic;

        public int Registrations { get; private set; }
        public List<(string Path, bool Signed)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((path, request.Headers.Contains(RequestSigning.SignatureHeader)));
            object body = path switch
            {
                "/api/v1/devices/register" => Register(),
                "/api/v1/me" => Me(),
                "/api/v1/me/display-name" => SetName((await request.Content!.ReadFromJsonAsync<SetDisplayNameRequest>(cancellationToken))!),
                "/api/v1/me/privacy" => SetPrivacy((await request.Content!.ReadFromJsonAsync<PrivacyRequest>(cancellationToken))!),
                "/api/v1/me/matches/batch" => Batch((await request.Content!.ReadFromJsonAsync<MatchBatchRequest>(cancellationToken))!),
                _ => throw new InvalidOperationException("unexpected " + path),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        }

        private RegisterDeviceResponse Register()
        {
            Registrations++;
            return new RegisterDeviceResponse(deviceId, profileId);
        }

        private MeResponse Me() => new(profileId, deviceId, name, name is null ? NameStatus.Unknown : NameStatus.Confirmed, isPublic, isPublic && name is not null, false, DateTimeOffset.UtcNow);

        private MeResponse SetName(SetDisplayNameRequest request)
        {
            name = request.Name;
            return Me();
        }

        private MeResponse SetPrivacy(PrivacyRequest request)
        {
            isPublic = request.IsPublic;
            return Me();
        }

        private MatchBatchResponse Batch(MatchBatchRequest request) => new(request.Matches.Select(m => m.Id == rejected
            ? new MatchBatchItemResult(m.Id, UploadOutcome.Rejected, "test rejection")
            : new MatchBatchItemResult(m.Id, UploadOutcome.Created, null)).ToList());
    }
}
