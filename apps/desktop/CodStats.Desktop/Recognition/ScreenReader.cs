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
public sealed class ScreenReader(IReadOnlyList<RecognitionProfile> profiles, ITextReader reader)
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
            var fields = new Dictionary<string, FieldReading>();
            foreach (var (name, spec) in profile.Fields)
            {
                var text = await reader.ReadAsync(frame, spec.Region, cancel);
                // A mode is stored by its name from the profile's list, not with whatever else the region held.
                if (name == Recognition.Fields.Mode && profile.Modes.Count > 0)
                {
                    text = TextParsing.FirstOf(text, profile.Modes) ?? "";
                }
                // Unverified profiles are allowed to run, but what they read is never trusted fully.
                fields[name] = new FieldReading(text, profile.Verified ? 1.0 : 0.5);
            }
            FieldReading? own = null;
            if (profile.OwnName is { } ownSpec)
            {
                own = new FieldReading(await reader.ReadAsync(frame, ownSpec.Region, cancel), profile.Verified ? 1.0 : 0.5);
            }
            if (profile.OwnRow is { } row && own is not null)
            {
                await ReadOwnRowAsync(frame, row, own, fields, profile.Verified ? 1.0 : 0.5, cancel);
            }
            if (profile.Labelled is { } labelled)
            {
                await ReadLabelledAsync(frame, labelled, fields, profile.Verified ? 1.0 : 0.5, cancel);
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
    private async Task ReadOwnRowAsync(Frame frame, OwnRowSpec row, FieldReading own, Dictionary<string, FieldReading> fields,
        double quality, CancellationToken cancel)
    {
        var wanted = TextParsing.Normalise(CodStats.Contracts.DisplayNames.WithoutClanTag(own.Text));
        if (wanted.Length < 2)
        {
            return;
        }

        var found = new List<(TableSpec Table, IReadOnlyList<TextLine> Lines, TextLine Name)>();
        foreach (var table in row.Tables)
        {
            var lines = await reader.ReadLinesAsync(frame, table.Region, cancel);
            found.AddRange(lines
                .Where(line => TextParsing.Normalise(CodStats.Contracts.DisplayNames.WithoutClanTag(line.Text)) == wanted)
                .Select(line => (table, lines, line)));
        }
        if (found.Count != 1)
        {
            return;
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

    private async Task<bool> MatchesAsync(RecognitionProfile profile, Frame frame, CancellationToken cancel)
    {
        foreach (var anchor in profile.Anchors)
        {
            var text = await reader.ReadAsync(frame, anchor.Region, cancel);
            if (!TextParsing.ContainsAny(text, anchor.AnyOf))
            {
                return false;
            }
        }
        return true;
    }
}
