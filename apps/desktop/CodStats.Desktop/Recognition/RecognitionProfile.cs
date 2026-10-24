using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodStats.Contracts;

namespace CodStats.Desktop.Recognition;

/// <summary>A rectangle relative to the game's client area: 0..1 in both directions, so it works at any resolution.</summary>
public sealed record Region(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// HUD text is white over the game world. With this set, only near-white pixels are kept, as black text on
    /// white, so a bright sky or a dark wall behind the text no longer matters to the OCR.
    /// </summary>
    public bool WhiteText { get; init; }

    public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.0001 && Y + Height <= 1.0001;
}

/// <summary>Text that must appear in a region for a frame to count as this screen.</summary>
public sealed record Anchor(Region Region, IReadOnlyList<string> AnyOf);

public sealed record FieldSpec(Region Region);

/// <summary>An area with labels and their values below them (field name → label words).</summary>
public sealed record LabelledValuesSpec(Region Region, IReadOnlyDictionary<string, IReadOnlyList<string>> Values);

/// <summary>A scoreboard table: its area including the heading row, and how tall that heading row is.</summary>
public sealed record TableSpec(Region Region, double HeaderHeight);

/// <summary>
/// The player's own scoreboard row: searched by name in the tables, values read from the columns whose heading
/// contains one of the words (field name → heading words).
/// </summary>
public sealed record OwnRowSpec(IReadOnlyList<TableSpec> Tables, double RowHeight, IReadOnlyDictionary<string, IReadOnlyList<string>> Columns);

/// <summary>
/// How one screen of one game looks at one aspect ratio. Profiles are data, not code, so a season update that
/// moves the scoreboard needs a new profile file, not a new client.
/// </summary>
public sealed record RecognitionProfile
{
    public required string Id { get; init; }
    public Game? Game { get; init; }
    public required ScreenKind Screen { get; init; }

    /// <summary>Width / height the regions were measured at, e.g. 1.7778 for 16:9.</summary>
    public required double Aspect { get; init; }

    /// <summary>False until the profile was checked against real screenshots. Unverified profiles never yield safe matches.</summary>
    public bool Verified { get; init; }

    public required IReadOnlyList<Anchor> Anchors { get; init; }
    public IReadOnlyDictionary<string, FieldSpec> Fields { get; init; } = new Dictionary<string, FieldSpec>();
    public FieldSpec? OwnName { get; init; }

    /// <summary>Fields read from the scoreboard row that carries <see cref="OwnName"/>.</summary>
    public OwnRowSpec? OwnRow { get; init; }

    /// <summary>Values printed under a label ("TODE" over "17"), read as one area.</summary>
    public LabelledValuesSpec? Labelled { get; init; }
    public IReadOnlyList<string> WinWords { get; init; } = [];
    public IReadOnlyList<string> LossWords { get; init; } = [];
    public IReadOnlyList<string> DrawWords { get; init; } = [];

    /// <summary>Known mode names; the mode field is stored as the first of these it contains.</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];

    public bool FitsAspect(double aspect) => Math.Abs(aspect - Aspect) / Aspect <= 0.02;

    public ProfileWords Words => new(WinWords, LossWords, DrawWords);

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "id fehlt";
        if (Aspect is < 1 or > 4) yield return "aspect unplausibel";
        if (Anchors.Count == 0) yield return "keine Anker";
        foreach (var region in Anchors.Select(a => a.Region).Concat(Fields.Values.Select(f => f.Region)))
        {
            if (!region.IsValid) yield return $"Bereich außerhalb 0..1: {region}";
        }
        if (Screen == ScreenKind.Result && Game is null) yield return "Ergebnisprofil ohne Spiel";
        if (OwnRow is not null && OwnName is null) yield return "ownRow braucht ownName";
        if (OwnRow is { } row && (row.Tables.Count == 0 || row.Tables.Any(t => !t.Region.IsValid) || row.RowHeight is <= 0 or > 0.2)) yield return "ownRow unplausibel";
    }
}

/// <summary>Loads profiles from the client's own folder and from %LOCALAPPDATA%\CodStats\profiles.</summary>
public static class ProfileStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>All valid profiles from the folders; a later folder replaces a profile with the same id.</summary>
    public static IReadOnlyList<RecognitionProfile> Load(IEnumerable<string> folders, Action<string>? warn = null)
    {
        var byId = new Dictionary<string, RecognitionProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<RecognitionProfile>(File.ReadAllText(file), Json);
                    var problems = profile?.Problems().ToList() ?? ["leer"];
                    if (profile is null || problems.Count > 0)
                    {
                        warn?.Invoke($"{Path.GetFileName(file)}: {string.Join(", ", problems)}");
                        continue;
                    }
                    byId[profile.Id] = profile;
                }
                catch (JsonException error)
                {
                    warn?.Invoke($"{Path.GetFileName(file)}: {error.Message}");
                }
            }
        }
        return byId.Values.ToList();
    }
}
