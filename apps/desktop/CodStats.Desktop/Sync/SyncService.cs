using System.Net;
using System.Net.Http;
using CodStats.Contracts;
using CodStats.Desktop.Identity;
using CodStats.Desktop.Storage;

namespace CodStats.Desktop.Sync;

public enum SyncPhase { Idle, Syncing, Done, Offline, Failed, DeviceInvalid }

public sealed record SyncStatus(SyncPhase Phase, DateTimeOffset? LastSuccess, int Waiting, string? Detail)
{
    public string Text => Phase switch
    {
        SyncPhase.Syncing => "Synchronisiert …",
        SyncPhase.Done => Waiting == 0 ? "Synchronisierung erfolgreich" : $"Synchronisiert, {Waiting} offen",
        SyncPhase.Offline => $"Offline, {Waiting} Match(es) warten",
        SyncPhase.Failed => "Synchronisierung fehlgeschlagen",
        SyncPhase.DeviceInvalid => "Gerät vom Server nicht mehr anerkannt",
        _ => "Bereit",
    };
}

/// <summary>What the player chose locally and the server still has to learn.</summary>
public sealed record ProfileWish(string? DisplayName, bool NameConfirmed, bool IsPublic);

/// <summary>
/// Brings the server up to date with the local database: registers the device once, then sends name, privacy,
/// new matches and corrections. Every step can be repeated safely: uploads are idempotent by match id.
/// </summary>
public sealed class SyncService(MatchStore store, DeviceIdentity identity, Func<ApiClient> client, string identityPath) : IDisposable
{
    public const int BatchSize = 50;
    private readonly SemaphoreSlim running = new(1, 1);

    public SyncStatus Status { get; private set; } = new(SyncPhase.Idle, null, 0, null);
    public MeResponse? Me { get; private set; }

    public event Action<SyncStatus>? StatusChanged;

    public async Task<SyncStatus> RunAsync(ProfileWish wish, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(wish);
        if (!await running.WaitAsync(0, cancel))
        {
            return Status;
        }
        try
        {
            Report(SyncPhase.Syncing, null);
            using var api = client();

            if (!identity.IsRegistered)
            {
                var registered = await api.RegisterAsync(identity.PublicKey, cancel);
                identity.SetRegistration(registered.DeviceId, registered.ProfileId, identityPath);
            }

            var me = await api.MeAsync(cancel);
            string? notice = null;
            if (wish.DisplayName is { } name && (me.DisplayName != name || me.NameStatus != WishedStatus(wish)))
            {
                try
                {
                    me = await api.SetDisplayNameAsync(name, WishedStatus(wish), cancel);
                }
                catch (ApiException error) when (error.Status == HttpStatusCode.Conflict)
                {
                    // Matches still go up; the profile just stays without that name until the player takes the
                    // existing profile over.
                    notice = $"Der Name „{name}“ gehört schon einem anderen Profil. Ist es deins, übernimm es in den Einstellungen mit dem Wiederherstellungsschlüssel.";
                }
            }
            if (me.IsPublic != wish.IsPublic)
            {
                me = await api.SetPrivacyAsync(wish.IsPublic, cancel);
            }
            Me = me;

            await UploadAll(api, cancel);
            await SendCorrections(api, cancel);
            return Report(SyncPhase.Done, notice, success: true);
        }
        catch (ApiException error) when (error.Status == HttpStatusCode.Unauthorized && identity.IsRegistered)
        {
            return Report(SyncPhase.DeviceInvalid, "Der Server kennt dieses Gerät nicht mehr (Profil gelöscht oder auf einen anderen PC übertragen).");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return Report(SyncPhase.Offline, error.Message);
        }
        catch (ApiException error)
        {
            return Report(SyncPhase.Failed, error.Message);
        }
        finally
        {
            running.Release();
        }

        static NameStatus WishedStatus(ProfileWish wish) => wish.NameConfirmed ? NameStatus.Confirmed : NameStatus.Uncertain;
    }

    private async Task UploadAll(ApiClient api, CancellationToken cancel)
    {
        while (store.ToUpload(BatchSize) is { Count: > 0 } batch)
        {
            var response = await api.UploadAsync(batch.Select(m => m.ToUpload()).ToList(), cancel);
            foreach (var result in response.Results)
            {
                if (result.Outcome == UploadOutcome.Rejected)
                {
                    store.SetSyncState(result.Id, SyncState.Rejected, result.Reason);
                }
                else
                {
                    // Created, unchanged and duplicate all mean: the server has this result.
                    store.SetSyncState(result.Id, SyncState.Synced);
                }
            }
            if (response.Results.Count == 0)
            {
                break;
            }
        }
    }

    private async Task SendCorrections(ApiClient api, CancellationToken cancel)
    {
        foreach (var match in store.CorrectionsToSend())
        {
            try
            {
                await api.CorrectAsync(match.Id, match.ToCorrection(), cancel);
                store.SetSyncState(match.Id, SyncState.Synced);
            }
            catch (ApiException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                store.SetSyncState(match.Id, SyncState.Rejected, error.Message);
            }
        }
    }

    private SyncStatus Report(SyncPhase phase, string? detail, bool success = false)
    {
        var waiting = store.Count(SyncState.Waiting) + store.Count(SyncState.CorrectionWaiting);
        Status = new SyncStatus(phase, success ? DateTimeOffset.Now : Status.LastSuccess, waiting, detail);
        StatusChanged?.Invoke(Status);
        return Status;
    }

    public void Dispose() => running.Dispose();
}
