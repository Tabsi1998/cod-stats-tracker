using CodStats.Desktop.Capture;

namespace CodStats.Desktop.Recognition;

/// <summary>One line of text and where it sits, relative to the whole frame (0..1).</summary>
public sealed record TextLine(string Text, double Left, double Top, double Width, double Height)
{
    public double Middle => Top + Height / 2;
    public double Center => Left + Width / 2;
}

/// <summary>Reads text from a region of a frame.</summary>
public interface ITextReader
{
    Task<string> ReadAsync(Frame frame, Region region, CancellationToken cancel);

    /// <summary>The lines of a region with their vertical position, for finding the player's own table row.</summary>
    Task<IReadOnlyList<TextLine>> ReadLinesAsync(Frame frame, Region region, CancellationToken cancel);
}

/// <summary>
/// Classifies a frame with the profiles and reads its fields. Only anchors are read for frames that match
/// nothing, so idle frames cost one OCR pass per anchor.
/// </summary>
/// <param name="knownName">
/// The player's name as recognised so far, for screens without a name banner (the free-for-all result): the own
/// table row is then found by this name.
/// </param>
public sealed class ScreenReader(IReadOnlyList<RecognitionProfile> profiles, ITextReader reader, Func<string?>? knownName = null)
{
    public IReadOnlyList<RecognitionProfile> Profiles { get; } = profiles;

    public async Task<Observation> ReadAsync(Frame frame, DateTimeOffset at, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(frame);
        foreach (var profile in Profiles.Where(p => p.FitsAspect(frame.Aspect)))
        {
            if (!await MatchesAsync(profile, frame, cancel))
            {
                continue;
            }
            // Unverified profiles are allowed to run, but what they read is never trusted fully.
            var quality = profile.Verified ? 1.0 : 0.5;
            var fields = new Dictionary<string, FieldReading>();
            foreach (var (name, spec) in profile.Fields)
            {
                if (await ReadFieldAsync(frame, profile, name, spec, cancel) is { } text)
                {
                    fields[name] = new FieldReading(text, quality);
                }
            }
            FieldReading? own = null;
            if (profile.OwnName is { } ownSpec)
            {
                own = new FieldReading(await reader.ReadAsync(frame, ownSpec.Region, cancel), quality);
            }
            var known = knownName?.Invoke() is { Length: > 0 } knownText ? new FieldReading(knownText, quality) : null;
            var bannerOfOther = own is not null && known is not null && !SameName(own.Text, known.Text);
            if (bannerOfOther)
            {
                // The in-match scoreboard's banner shows whichever player is selected, not always the player:
                // neither the name nor the values under it are the player's then.
                own = null;
            }
            var rowName = own ?? known;
            if (profile.OwnRow is { } row && rowName is not null
                && await ReadOwnRowAsync(frame, row, rowName, fields, quality, cancel) is { } tableName && own is not null)
            {
                // The table spells the name more reliably than the banner (where "]" can come out as "I").
                own = own with { Text = tableName };
            }
            if (profile.Labelled is { } labelled && !bannerOfOther)
            {
                await ReadLabelledAsync(frame, labelled, fields, quality, cancel);
            }
            if (profile.Tiles is { } tiles)
            {
                await ReadTilesAsync(frame, tiles, fields, quality, cancel);
            }
            return new Observation(at, profile.Screen, profile.Game, profile.Id, fields, own);
        }
        return Observation.Of(at, ScreenKind.Unknown);
    }

    /// <summary>
    /// The scoreboard lists every player; only the row with the player's own name counts. Each table is read in
    /// one OCR pass (single short numbers alone are read unreliably). The row is found by the name from the
    /// player's own banner, the columns by their headings ("ABSCHÜSSE", "PUNKTE"), because modes add and move
    /// columns. A value is only taken where both meet; if the name is not found exactly once or a heading is
    /// missing, the field stays empty: a neighbour's or another column's value is never taken.
    /// </summary>
    private async Task<string?> ReadOwnRowAsync(Frame frame, OwnRowSpec row, FieldReading own, Dictionary<string, FieldReading> fields,
        double quality, CancellationToken cancel)
    {
        if (TextParsing.Normalise(CodStats.Contracts.DisplayNames.WithoutClanTag(own.Text)).Length < 2)
        {
            return null;
        }

        var found = new List<(TableSpec Table, IReadOnlyList<TextLine> Lines, TextLine Name)>();
        var tables = new List<(TableSpec Table, IReadOnlyList<TextLine> Lines)>();
        foreach (var table in row.Tables)
        {
            var lines = await reader.ReadLinesAsync(frame, table.Region, cancel);
            tables.Add((table, lines));
            found.AddRange(lines.Where(line => SameName(own.Text, line.Text)).Select(line => (table, lines, line)));
        }
        if (found.Count != 1)
        {
            return null;
        }

        var (ownTable, cells, name) = found[0];
        var headings = cells.Where(c => c.Top < ownTable.Region.Y + ownTable.HeaderHeight).ToList();
        var rowCells = cells.Where(c => Math.Abs(c.Middle - name.Middle) < row.RowHeight / 2 && c != name).ToList();
        foreach (var (field, words) in row.Columns)
        {
            var heading = headings.FirstOrDefault(h => TextParsing.ContainsAny(h.Text, words));
            if (heading is null)
            {
                continue;
            }
            // Cells start under the start of their heading (left-aligned), within a little tolerance.
            var cell = rowCells
                .Where(c => c.Left >= heading.Left - 0.012 && c.Left <= heading.Left + heading.Width)
                .OrderBy(c => Math.Abs(c.Left - heading.Left))
                .FirstOrDefault();
            if (cell is not null)
            {
                fields[field] = new FieldReading(cell.Text, quality);
            }
        }
        if (row.RankField is { } rankField && !fields.ContainsKey(rankField))
        {
            var above = cells.Count(c => Math.Abs(c.Left - name.Left) < 0.01 && c.Middle < name.Middle - row.RowHeight / 2
                && c.Top >= ownTable.Region.Y + ownTable.HeaderHeight);
            if (above <= 2)
            {
                fields[rankField] = new FieldReading((above + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), quality);
            }
        }
        if (row.Lobby && Lobby(tables, row, ownTable, name) is { } lobby)
        {
            fields[Recognition.Fields.LobbySize] = new FieldReading(lobby.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), quality);
            fields[Recognition.Fields.LobbyRank] = new FieldReading(lobby.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), quality);
        }
        return name.Text;
    }

    /// <summary>
    /// The lobby from the tables already read: every row's score (under the score heading, beside a name in the
    /// name column) and how many are higher than the player's. Only these two numbers leave here, never a name or
    /// another player's value. Any doubt (a table without score heading, a name without a score or the other way
    /// round, a score that is no number) and the lobby stays unknown rather than wrong.
    /// </summary>
    internal static (int Size, int Rank)? Lobby(IReadOnlyList<(TableSpec Table, IReadOnlyList<TextLine> Lines)> tables, OwnRowSpec row,
        TableSpec ownTable, TextLine ownName)
    {
        if (!row.Columns.TryGetValue(Recognition.Fields.Score, out var scoreWords))
        {
            return null;
        }
        // Both tables have the same layout: the name column sits at the same offset from each table's left edge.
        var nameOffset = ownName.Left - ownTable.Region.X;
        var scores = new List<int>();
        int? own = null;
        foreach (var (table, lines) in tables)
        {
            var bodyTop = table.Region.Y + table.HeaderHeight;
            var heading = lines.FirstOrDefault(l => l.Top < bodyTop && TextParsing.ContainsAny(l.Text, scoreWords));
            if (heading is null)
            {
                return null;
            }
            var body = lines.Where(l => l.Top >= bodyTop).ToList();
            var names = body.Where(l => Math.Abs(l.Left - (table.Region.X + nameOffset)) < 0.01).ToList();
            var cells = body.Where(l => l.Left >= heading.Left - 0.012 && l.Left <= heading.Left + heading.Width).ToList();
            if (names.Count != cells.Count)
            {
                return null;
            }
            foreach (var name in names)
            {
                var cell = cells.Where(c => Math.Abs(c.Middle - name.Middle) < row.RowHeight / 2).ToList();
                if (cell.Count != 1 || TextParsing.Count(cell[0].Text).Value is not { } value)
                {
                    return null;
                }
                scores.Add(value);
                if (ReferenceEquals(name, ownName))
                {
                    own = value;
                }
            }
        }
        if (own is not { } mine || scores.Count < 2)
        {
            return null;
        }
        return (scores.Count, 1 + scores.Count(s => s > mine));
    }

    /// <summary>
    /// Values under labels, such as the player's banner ("ELIMINIERUNGEN 18 · TODE 17"). The whole area is read in
    /// one pass, because a lone digit is often not read at all; each value is the text right below its label.
    /// </summary>
    private async Task ReadLabelledAsync(Frame frame, LabelledValuesSpec spec, Dictionary<string, FieldReading> fields,
        double quality, CancellationToken cancel)
    {
        var lines = await reader.ReadLinesAsync(frame, spec.Region, cancel);
        foreach (var (field, words) in spec.Values)
        {
            var label = lines.FirstOrDefault(l => TextParsing.ContainsAny(l.Text, words));
            if (label is null)
            {
                continue;
            }
            var value = lines
                .Where(l => l.Top >= label.Top + label.Height * 0.8 && l.Top <= label.Top + label.Height * 4
                    && l.Center >= label.Left - label.Width && l.Center <= label.Left + label.Width * 2)
                .OrderBy(l => Math.Abs(l.Center - label.Center))
                .FirstOrDefault();
            if (value is not null)
            {
                fields[field] = new FieldReading(value.Text, quality);
            }
        }
    }

    private async Task<string?> ReadFieldAsync(Frame frame, RecognitionProfile profile, string name, FieldSpec spec, CancellationToken cancel)
    {
        var regions = spec.More.Prepend(spec.Region).ToList();
        if (spec.Numbers > 0)
        {
            var readings = new List<string>();
            foreach (var region in regions)
            {
                if (TextParsing.Numbers(await reader.ReadAsync(frame, region, cancel), spec.Numbers) is { } numbers)
                {
                    readings.Add(string.Join(' ', numbers));
                }
            }
            return MostCommon(readings);
        }
        if (spec.WithoutModes)
        {
            var lines = new List<TextLine>();
            foreach (var region in regions)
            {
                lines.AddRange(await reader.ReadLinesAsync(frame, region, cancel));
            }
            return string.Join(' ', lines.Where(l => !TextParsing.ContainsAny(l.Text, profile.Modes)).OrderBy(l => l.Left).Select(l => l.Text.Trim()));
        }
        var text = await ReadAllAsync(frame, regions, cancel);
        // A mode is stored by its name from the profile's list, not with whatever else the region held.
        return name == Recognition.Fields.Mode && profile.Modes.Count > 0 ? TextParsing.FirstOf(text, profile.Modes) ?? "" : text;
    }

    /// <summary>The value most passes agree on; on a tie the earlier pass, which the profile lists as the better one.</summary>
    private static T? MostCommon<T>(List<T> values) where T : notnull =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => values.IndexOf(g.Key)).Select(g => g.Key).FirstOrDefault();

    /// <summary>
    /// The tiles of a report: each label with the number centred above it. Labels and numbers are gathered from all
    /// passes first, since one pass may read a label and miss its number and another the other way round; a tile's
    /// value is the number most passes read above its label.
    /// </summary>
    private async Task ReadTilesAsync(Frame frame, TilesSpec spec, Dictionary<string, FieldReading> fields, double quality, CancellationToken cancel)
    {
        const double SameTile = 0.03;
        var labels = new List<TextLine>();
        var numbers = new List<(TextLine Line, int Value)>();
        foreach (var region in spec.More.Prepend(spec.Region))
        {
            foreach (var line in await reader.ReadLinesAsync(frame, region, cancel))
            {
                if (TextParsing.Count(line.Text).Value is { } value)
                {
                    numbers.Add((line, value));
                }
                else if (TextParsing.Normalise(line.Text).Length >= 3)
                {
                    labels.Add(line);
                }
            }
        }

        foreach (var group in Cluster(labels, l => l.Center, SameTile))
        {
            var key = spec.Labels.FirstOrDefault(l => group.Any(t => TextParsing.ContainsAny(t.Text, l.Value))).Key;
            if (key is null)
            {
                // A label no field knows: kept only when two passes read it alike, so OCR noise never becomes a value.
                var name = group.GroupBy(t => TextParsing.Normalise(t.Text)).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                key = name is null ? null : Recognition.Fields.StatPrefix + name.ToLowerInvariant();
            }
            var center = group.Average(l => l.Center);
            var top = group.Min(l => l.Top);
            var height = group.Max(l => l.Height);
            var values = numbers
                .Where(n => Math.Abs(n.Line.Center - center) < SameTile && n.Line.Top < top && n.Line.Top > top - height * 6)
                .Select(n => n.Value)
                .ToList();
            if (key is null || values.Count == 0 || fields.ContainsKey(key))
            {
                continue;
            }
            fields[key] = new FieldReading(MostCommon(values).ToString(System.Globalization.CultureInfo.InvariantCulture), quality);
        }
    }

    /// <summary>Groups items whose position lies within <paramref name="distance"/> of the group's first item.</summary>
    private static List<List<T>> Cluster<T>(IEnumerable<T> items, Func<T, double> position, double distance)
    {
        var groups = new List<List<T>>();
        foreach (var item in items.OrderBy(position))
        {
            if (groups.Count > 0 && position(item) - position(groups[^1][0]) < distance)
            {
                groups[^1].Add(item);
            }
            else
            {
                groups.Add([item]);
            }
        }
        return groups;
    }

    private async Task<string> ReadAllAsync(Frame frame, IEnumerable<Region> regions, CancellationToken cancel)
    {
        var texts = new List<string>();
        foreach (var region in regions)
        {
            texts.Add(await reader.ReadAsync(frame, region, cancel));
        }
        return string.Join(' ', texts);
    }

    /// <summary>
    /// The banner name and a table name are the same player. At 1080p the OCR sometimes reads the clan tag's "]" as
    /// "I" ("[TLSITabsi98"), so the tag cannot always be cut off: one name ending in the other's tagless form counts,
    /// for names of at least four characters.
    /// </summary>
    internal static bool SameName(string banner, string table)
    {
        var bannerCore = TextParsing.Normalise(CodStats.Contracts.DisplayNames.WithoutClanTag(banner));
        var tableCore = TextParsing.Normalise(CodStats.Contracts.DisplayNames.WithoutClanTag(table));
        if (bannerCore.Length < 2 || tableCore.Length < 2)
        {
            return false;
        }
        if (bannerCore == tableCore)
        {
            return true;
        }
        var bannerFull = TextParsing.Normalise(banner);
        var tableFull = TextParsing.Normalise(table);
        return (tableCore.Length >= 4 && bannerFull.EndsWith(tableCore, StringComparison.Ordinal) && bannerFull.Length - tableCore.Length <= 6)
            || (bannerCore.Length >= 4 && tableFull.EndsWith(bannerCore, StringComparison.Ordinal) && tableFull.Length - bannerCore.Length <= 6);
    }

    private async Task<bool> MatchesAsync(RecognitionProfile profile, Frame frame, CancellationToken cancel)
    {
        foreach (var anchor in profile.Anchors)
        {
            var text = await ReadAllAsync(frame, anchor.More.Prepend(anchor.Region), cancel);
            if (!TextParsing.ContainsAny(text, anchor.AnyOf) || TextParsing.ContainsAny(text, anchor.NoneOf))
            {
                return false;
            }
        }
        return true;
    }
}
