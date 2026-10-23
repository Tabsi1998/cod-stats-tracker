using CodStats.Desktop.Capture;

namespace CodStats.Desktop.Recognition;

/// <summary>Reads text from a region of a frame.</summary>
public interface ITextReader
{
    Task<string> ReadAsync(Frame frame, Region region, CancellationToken cancel);
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
                // Unverified profiles are allowed to run, but what they read is never trusted fully.
                fields[name] = new FieldReading(text, profile.Verified ? 1.0 : 0.5);
            }
            FieldReading? own = null;
            if (profile.OwnName is { } ownSpec)
            {
                own = new FieldReading(await reader.ReadAsync(frame, ownSpec.Region, cancel), profile.Verified ? 1.0 : 0.5);
            }
            return new Observation(at, profile.Screen, profile.Game, profile.Id, fields, own);
        }
        return Observation.Of(at, ScreenKind.Unknown);
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
