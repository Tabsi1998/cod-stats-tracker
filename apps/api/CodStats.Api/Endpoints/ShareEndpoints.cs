using System.Globalization;
using System.Text;
using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api.Endpoints;

/// <summary>
/// Link previews for Discord, WhatsApp, X and co. Caddy sends only their crawlers here, for profile and match
/// addresses; people get the website. Each answer is a small page with Open Graph tags and a link to the real page.
/// Hidden or unknown profiles get the site's general preview, so a preview tells nothing a visitor could not see.
/// </summary>
public static class ShareEndpoints
{
    private const string SiteName = "COD Stats";
    private const string SiteDescription = "Community-erfasste Statistiken für Call of Duty: MW4 Multiplayer und Warzone.";

    /// <summary>German numbers ("1.234", "1,32") without depending on the server's culture data.</summary>
    private static readonly NumberFormatInfo German = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", PercentDecimalSeparator = "," };

    public static void MapShareEndpoints(this RouteGroupBuilder api)
    {
        var share = api.MapGroup("/public/share").WithTags("Link previews").RequireRateLimiting(RateLimits.Public).ExcludeFromDescription();
        share.MapGet("/player/{id:guid}", Player);
        share.MapGet("/player/{id:guid}/match/{matchId:guid}", Match);
    }

    private static async Task<IResult> Player(Guid id, AppDb db, TimeProvider clock, HttpRequest request, CancellationToken cancel)
    {
        var path = $"/player/{id}";
        var name = await db.Profiles.AsNoTracking().Visible().Where(p => p.Id == id).Select(p => p.DisplayName).SingleOrDefaultAsync(cancel);
        if (name is null)
        {
            return Page(request, path, SiteName, SiteDescription);
        }
        var stats = await Statistics.ForProfile(db, id, new StatsFilter(GameGroup.All), clock.GetUtcNow(), shownOnly: true, cancel);
        return Page(request, path, $"{name} · {SiteName}", PlayerDescription(stats));
    }

    private static async Task<IResult> Match(Guid id, Guid matchId, AppDb db, HttpRequest request, CancellationToken cancel)
    {
        var path = $"/player/{id}/match/{matchId}";
        var name = await db.Profiles.AsNoTracking().Visible().Where(p => p.Id == id).Select(p => p.DisplayName).SingleOrDefaultAsync(cancel);
        var match = name is null ? null
            : await db.Matches.Shown().AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchId && m.ProfileId == id, cancel);
        if (match is null)
        {
            return Page(request, path, SiteName, SiteDescription);
        }
        return Page(request, path, $"{MatchTitle(match)} · {name}", MatchDescription(match));
    }

    /// <summary>"K/D 1,32 · 54 % Siege · 120 Matches · 1.234 Kills"; unknown values are left out, never shown as 0.</summary>
    private static string PlayerDescription(StatsResponse stats)
    {
        if (stats.Matches == 0)
        {
            return "Noch keine Matches. " + SiteDescription;
        }
        var parts = new List<string>();
        if (stats.KdRatio is { } kd)
        {
            parts.Add("K/D " + kd.ToString("0.00", German));
        }
        if (stats.WinRate is { } rate)
        {
            parts.Add((rate * 100).ToString("0.#", German) + " % Siege");
        }
        parts.Add(Count(stats.Matches) + (stats.Matches == 1 ? " Match" : " Matches"));
        parts.Add(Count(stats.Kills) + " Kills");
        return string.Join(" · ", parts) + ". Community-erfasst, nicht von Activision bestätigt.";
    }

    /// <summary>"Sieg · TEAM-DEATHMATCH" as the match page heads it.</summary>
    private static string MatchTitle(Data.Match match)
    {
        var outcome = match.Status == MatchStatus.Left ? "Verlassen" : match.Result switch
        {
            MatchResult.Win => "Sieg",
            MatchResult.Loss => "Niederlage",
            MatchResult.Draw => "Unentschieden",
            _ => "Match",
        };
        var what = match.Mode ?? Game(match.Game);
        return $"{outcome} · {what}";
    }

    private static string MatchDescription(Data.Match match)
    {
        var parts = new List<string>();
        if (match.Placement is { } placement)
        {
            parts.Add($"Platz {Count(placement)}");
        }
        if (match.Kills is { } kills)
        {
            parts.Add($"{Count(kills)} Kills");
        }
        if (match.Deaths is { } deaths)
        {
            parts.Add($"{Count(deaths)} Tode");
        }
        if (match.Kills is { } k && match.Deaths is { } d)
        {
            parts.Add("K/D " + (StatsCalculator.KdRatio(k, d) ?? k).ToString("0.00", German));
        }
        if (match.Score is { } score)
        {
            parts.Add($"{Count(score)} Punkte");
        }
        var where = match.Map is { Length: > 0 } map ? $"{Game(match.Game)}, {map}" : Game(match.Game);
        return (parts.Count > 0 ? string.Join(" · ", parts) + ". " : "") + where + ". Community-erfasst.";
    }

    private static string Game(Game game) => game switch
    {
        Contracts.Game.Mw4Multiplayer => "MW4 Multiplayer",
        Contracts.Game.WarzoneBattleRoyale => "Warzone Battle Royale",
        Contracts.Game.WarzoneResurgence => "Warzone Resurgence",
        _ => "Call of Duty",
    };

    private static string Count(long value) => value.ToString("#,0", German);

    /// <summary>The five characters that matter in HTML text and quoted attributes; umlauts and "·" stay readable.</summary>
    private static string E(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&#39;", StringComparison.Ordinal);

    /// <summary>The preview page. Every value is HTML-encoded: names and modes come from players' screens.</summary>
    private static IResult Page(HttpRequest request, string path, string title, string description)
    {
        var origin = $"{request.Scheme}://{request.Host}";
        var url = origin + path;
        var html = new StringBuilder()
            .Append("<!doctype html>\n<html lang=\"de\">\n<head>\n<meta charset=\"utf-8\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<title>{E(title)}</title>\n")
            .Append(CultureInfo.InvariantCulture, $"<meta name=\"description\" content=\"{E(description)}\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<link rel=\"canonical\" href=\"{E(url)}\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<meta property=\"og:site_name\" content=\"{SiteName}\" />\n")
            .Append("<meta property=\"og:type\" content=\"website\" />\n<meta property=\"og:locale\" content=\"de_AT\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<meta property=\"og:title\" content=\"{E(title)}\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<meta property=\"og:description\" content=\"{E(description)}\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<meta property=\"og:url\" content=\"{E(url)}\" />\n")
            .Append(CultureInfo.InvariantCulture, $"<meta property=\"og:image\" content=\"{E(origin)}/og.png\" />\n")
            .Append("<meta property=\"og:image:width\" content=\"1200\" />\n<meta property=\"og:image:height\" content=\"630\" />\n")
            .Append("<meta name=\"twitter:card\" content=\"summary_large_image\" />\n")
            .Append("<meta name=\"theme-color\" content=\"#070d14\" />\n</head>\n<body>\n")
            .Append(CultureInfo.InvariantCulture, $"<p><a href=\"{E(url)}\">{E(title)}</a></p>\n<p>{E(description)}</p>\n")
            .Append("</body>\n</html>\n");
        return Results.Content(html.ToString(), "text/html; charset=utf-8");
    }
}
