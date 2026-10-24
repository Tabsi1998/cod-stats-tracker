using CodStats.Api.Auth;
using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api.Endpoints;

/// <summary>Everything a client may do with its own profile. The profile comes only from the signing device.</summary>
public static class MeEndpoints
{
    public const int MaxBatch = 50;
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(3);

    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        // Authentication happens in the DeviceAuthentication middleware for everything under /api/v1/me.
        var me = api.MapGroup("/me").WithTags("Own profile (signed)").RequireRateLimiting(RateLimits.Device);

        me.MapGet("", GetMe);
        me.MapPut("/display-name", SetDisplayName);
        me.MapPatch("/privacy", SetPrivacy);
        me.MapGet("/stats", GetStats);
        me.MapGet("/matches", GetMatches);
        me.MapPost("/matches/batch", UploadBatch);
        me.MapPatch("/matches/{id:guid}", CorrectMatch);
        me.MapGet("/sync-status", GetSyncStatus);
        me.MapPost("/recovery-key", CreateRecoveryKey);
        me.MapGet("/export", Export);
        me.MapDelete("/data", DeleteData);
    }

    private static async Task<IResult> GetMe(CurrentDevice device, AppDb db, CancellationToken cancel) =>
        Results.Ok(await LoadMe(device, db, cancel));

    private static async Task<MeResponse> LoadMe(CurrentDevice device, AppDb db, CancellationToken cancel)
    {
        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == device.ProfileId, cancel);
        var visible = await db.Profiles.Visible().AnyAsync(p => p.Id == device.ProfileId, cancel);
        return new MeResponse(profile.Id, device.DeviceId, profile.DisplayName, profile.NameStatus, profile.IsPublic,
            visible, profile.RecoveryKeyHash is not null, profile.CreatedAt);
    }

    private static async Task<IResult> SetDisplayName(SetDisplayNameRequest body, CurrentDevice device, AppDb db,
        TimeProvider clock, CancellationToken cancel)
    {
        var name = DisplayNames.Normalise(body.Name);
        if (name is null)
        {
            return Results.Problem(detail: $"name must be {DisplayNames.MinLength}–{DisplayNames.MaxLength} characters without control characters", statusCode: 400);
        }
        if (body.Status is not (NameStatus.Uncertain or NameStatus.Confirmed))
        {
            return Results.Problem(detail: "status must be uncertain or confirmed", statusCode: 400);
        }

        var now = clock.GetUtcNow();
        var profile = await db.Profiles.SingleAsync(p => p.Id == device.ProfileId, cancel);
        profile.DisplayName = name;
        profile.NameStatus = body.Status;
        profile.UpdatedAt = now;

        var history = await db.NameHistory.SingleOrDefaultAsync(n => n.ProfileId == profile.Id && n.Name == name, cancel);
        if (history is null)
        {
            db.NameHistory.Add(new NameHistory { ProfileId = profile.Id, Name = name, FirstSeenAt = now, LastSeenAt = now });
        }
        else
        {
            history.LastSeenAt = now;
        }
        await db.SaveChangesAsync(cancel);
        return Results.Ok(await LoadMe(device, db, cancel));
    }

    private static async Task<IResult> SetPrivacy(PrivacyRequest body, CurrentDevice device, AppDb db,
        TimeProvider clock, CancellationToken cancel)
    {
        await db.Profiles.Where(p => p.Id == device.ProfileId).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.IsPublic, body.IsPublic)
            .SetProperty(p => p.UpdatedAt, clock.GetUtcNow()), cancel);
        if (body.IsPublic)
        {
            // Going public shows the whole history, also what was played while private. Going private hides
            // nothing already shown; it only keeps new matches off the website.
            await db.Matches.Where(m => m.ProfileId == device.ProfileId && !m.IsPublic)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsPublic, true), cancel);
        }
        return Results.Ok(await LoadMe(device, db, cancel));
    }

    private static async Task<IResult> GetStats(CurrentDevice device, AppDb db, TimeProvider clock, HttpRequest request, CancellationToken cancel)
    {
        if (!QueryFilters.TryStats(request.Query, out var filter, out var problem))
        {
            return Results.Problem(detail: problem, statusCode: 400);
        }
        return Results.Ok(await Statistics.ForProfile(db, device.ProfileId, filter, clock.GetUtcNow(), shownOnly: false, cancel));
    }

    private static async Task<IResult> GetMatches(CurrentDevice device, AppDb db, int? offset, int? limit, CancellationToken cancel)
    {
        var (skip, take) = QueryFilters.Paging(offset, limit);
        var query = db.Matches.AsNoTracking().Where(m => m.ProfileId == device.ProfileId);
        var total = await query.CountAsync(cancel);
        var items = await query.OrderByDescending(m => m.PlayedAt).ThenByDescending(m => m.Id).Skip(skip).Take(take).ToListAsync(cancel);
        return Results.Ok(new Page<OwnMatch>(items.Select(Mapping.ToOwn).ToList(), total, skip, take));
    }

    private static async Task<IResult> UploadBatch(MatchBatchRequest body, CurrentDevice device, AppDb db,
        TimeProvider clock, CancellationToken cancel)
    {
        if (body.Matches is null || body.Matches.Count is 0 or > MaxBatch)
        {
            return Results.Problem(detail: $"send 1–{MaxBatch} matches per batch", statusCode: 400);
        }

        var now = clock.GetUtcNow();
        var isPublic = await db.Profiles.Where(p => p.Id == device.ProfileId).Select(p => p.IsPublic).SingleAsync(cancel);
        var ids = body.Matches.Select(m => m.Id).ToList();
        var existing = await db.Matches.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancel);
        var results = new List<MatchBatchItemResult>(body.Matches.Count);
        var accepted = new List<Match>();

        foreach (var upload in body.Matches)
        {
            if (results.Any(r => r.Id == upload.Id))
            {
                results.Add(new(upload.Id, UploadOutcome.Rejected, "id appears twice in this batch"));
                continue;
            }
            var problems = MatchRules.Validate(upload, now);
            if (problems.Count > 0)
            {
                results.Add(new(upload.Id, UploadOutcome.Rejected, string.Join("; ", problems)));
                continue;
            }
            var match = Mapping.ToEntity(upload, device.ProfileId, isPublic, now);
            if (existing.TryGetValue(upload.Id, out var stored))
            {
                // Another profile's id says nothing about that profile: the answer is the same as for any conflict.
                results.Add(stored.ProfileId == device.ProfileId && Mapping.SameValues(stored, match)
                    ? new(upload.Id, UploadOutcome.Unchanged, null)
                    : new(upload.Id, UploadOutcome.Rejected, "a different match with this id exists; change it with PATCH"));
                continue;
            }

            var from = match.PlayedAt - DuplicateWindow;
            var to = match.PlayedAt + DuplicateWindow;
            var duplicate = accepted.Any(other => Mapping.IsDuplicate(other, match))
                || await db.Matches.AnyAsync(m => m.ProfileId == device.ProfileId && m.Game == match.Game
                    && m.PlayedAt >= from && m.PlayedAt <= to
                    && m.Kills == match.Kills && m.Deaths == match.Deaths && m.Placement == match.Placement
                    && m.Result == match.Result && m.Score == match.Score, cancel);
            if (duplicate)
            {
                results.Add(new(upload.Id, UploadOutcome.Duplicate, "the same result was already recorded"));
                continue;
            }

            accepted.Add(match);
            results.Add(new(upload.Id, UploadOutcome.Created, null));
        }

        db.Matches.AddRange(accepted);
        await db.SaveChangesAsync(cancel);
        return Results.Ok(new MatchBatchResponse(results));
    }

    private static async Task<IResult> CorrectMatch(Guid id, MatchCorrectionRequest body, CurrentDevice device, AppDb db,
        TimeProvider clock, CancellationToken cancel)
    {
        var match = await db.Matches.SingleOrDefaultAsync(m => m.Id == id && m.ProfileId == device.ProfileId, cancel);
        if (match is null)
        {
            return Results.NotFound();
        }
        var now = clock.GetUtcNow();
        var problems = MatchRules.Validate(match.Game, body, now);
        if (problems.Count > 0)
        {
            return Results.Problem(detail: string.Join("; ", problems), statusCode: 400);
        }

        match.Mode = body.Mode;
        match.PlayedAt = Mapping.Seconds(body.PlayedAt);
        match.Kills = body.Kills;
        match.Deaths = body.Deaths;
        match.Assists = body.Assists;
        match.Placement = body.Placement;
        match.Result = MatchRules.NormalisedResult(match.Game, body.Result, body.Placement);
        match.Map = body.Map;
        match.Score = body.Score;
        // A result given by the player means the match was played to the end after all.
        if (match.Result is not null)
        {
            match.Status = MatchStatus.Completed;
        }
        match.ReviewStatus = ReviewStatus.Corrected;
        match.UpdatedAt = now;
        await db.SaveChangesAsync(cancel);
        return Results.Ok(Mapping.ToOwn(match));
    }

    private static async Task<IResult> GetSyncStatus(CurrentDevice device, AppDb db, CancellationToken cancel)
    {
        var query = db.Matches.Where(m => m.ProfileId == device.ProfileId);
        var count = await query.CountAsync(cancel);
        var lastUpload = await query.MaxAsync(m => (DateTimeOffset?)m.UploadedAt, cancel);
        var lastMatch = await query.MaxAsync(m => (DateTimeOffset?)m.PlayedAt, cancel);
        return Results.Ok(new SyncStatusResponse(count, lastUpload, lastMatch));
    }

    private static async Task<IResult> CreateRecoveryKey(CurrentDevice device, AppDb db, CancellationToken cancel)
    {
        var key = RecoveryKeys.Create();
        var hash = RecoveryKeys.Hash(key);
        await db.Profiles.Where(p => p.Id == device.ProfileId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecoveryKeyHash, hash), cancel);
        return Results.Ok(new RecoveryKeyResponse(key));
    }

    private static async Task<IResult> Export(CurrentDevice device, AppDb db, TimeProvider clock, CancellationToken cancel)
    {
        var me = await LoadMe(device, db, cancel);
        var names = await db.NameHistory.AsNoTracking().Where(n => n.ProfileId == device.ProfileId).OrderBy(n => n.FirstSeenAt)
            .Select(n => new NameHistoryEntry(n.Name, n.FirstSeenAt, n.LastSeenAt)).ToListAsync(cancel);
        var matches = await db.Matches.AsNoTracking().Where(m => m.ProfileId == device.ProfileId).OrderBy(m => m.PlayedAt).ToListAsync(cancel);
        var devices = await db.Devices.AsNoTracking().Where(d => d.ProfileId == device.ProfileId).OrderBy(d => d.CreatedAt)
            .Select(d => new DeviceInfo(d.Id, d.CreatedAt, d.LastSeenAt, d.RevokedAt)).ToListAsync(cancel);
        return Results.Ok(new ExportResponse(me, names, matches.Select(Mapping.ToOwn).ToList(), devices, clock.GetUtcNow()));
    }

    /// <summary>Deletes the profile with its matches, names and devices. This device can do nothing afterwards.</summary>
    private static async Task<IResult> DeleteData(CurrentDevice device, AppDb db, CancellationToken cancel)
    {
        await db.Profiles.Where(p => p.Id == device.ProfileId).ExecuteDeleteAsync(cancel);
        return Results.NoContent();
    }
}
