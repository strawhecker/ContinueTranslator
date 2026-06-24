using System.Text.Json;

namespace ContinueTranslator.Core.Mapping;

/// <summary>
/// Reads <c>types.json</c> at construction time and resolves TypeScript type names
/// (e.g. <c>string</c>, <c>Promise</c>, <c>Array</c>) to their C# equivalents.
/// </summary>
internal sealed partial class TypeMap
{
    // Both simple ("string" → "string") and generic-base ("Promise" → "Task") entries live here.
    private readonly Dictionary<string, string> _map;

    /// <param name="jsonPath">Absolute path to <c>types.json</c>.</param>
    public TypeMap(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);

        using var stream = File.OpenRead(jsonPath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Failed to deserialize '{jsonPath}'.");

        _map = new Dictionary<string, string>(raw.Count, StringComparer.Ordinal);

        foreach (var (tsKey, csValue) in raw)
        {
            // Normalise generic placeholders: "Promise<T>" → key "Promise", value "Task".
            string normKey = StripGenericPlaceholder(tsKey);
            string normValue = StripGenericPlaceholder(csValue);
            _map[normKey] = normValue;
        }
    }

    /// <summary>
    /// Returns the mapped C# type for <paramref name="tsType"/>, handling generic forms by
    /// stripping type arguments, looking up the base name, and re-attaching the arguments.
    /// Returns <paramref name="tsType"/> unchanged when no mapping is found.
    /// </summary>
    public string Resolve(string tsType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tsType);

        // Fast path: exact match (handles simple types and already-stripped names).
        if (_map.TryGetValue(tsType, out string? direct))
            return direct;

        // Generic form: split "Promise<Foo, Bar>" into base "Promise" and args "<Foo, Bar>".
        int angleBracket = tsType.IndexOf('<');
        if (angleBracket > 0)
        {
            string baseName = tsType[..angleBracket];
            string typeArgs = tsType[angleBracket..]; // includes < and >

            if (_map.TryGetValue(baseName, out string? mappedBase))
                return mappedBase + typeArgs;
        }

        return tsType;
    }

    /// <summary>
    /// Removes a generic placeholder suffix (<c>&lt;T&gt;</c>, <c>&lt;K,V&gt;</c>, etc.)
    /// from a type name, returning only the base identifier.
    /// </summary>
    private static string StripGenericPlaceholder(string value)
    {
        int idx = value.IndexOf('<');
        return idx > 0 ? value[..idx] : value;
    }
}

