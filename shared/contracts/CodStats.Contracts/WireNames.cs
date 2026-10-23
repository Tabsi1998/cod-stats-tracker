using System.Reflection;
using System.Text.Json.Serialization;

namespace CodStats.Contracts;

/// <summary>The JSON names of the enums, for query strings and logs.</summary>
public static class WireNames
{
    public static string Of<T>(T value) where T : struct, Enum =>
        typeof(T).GetField(value.ToString())?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
        ?? value.ToString();

    public static bool TryParse<T>(string? text, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(Of(candidate), text, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }
}
