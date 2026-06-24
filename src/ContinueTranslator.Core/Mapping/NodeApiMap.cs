using System.Text.Json;

namespace ContinueTranslator.Core.Mapping;

/// <summary>
/// Reads <c>node-api.json</c> at construction time and resolves Node.js API call patterns
/// (e.g. <c>fs.readFile</c>) to their .NET fully-qualified member equivalents.
/// </summary>
internal sealed partial class NodeApiMap
{
    private readonly Dictionary<string, string> _map;

    /// <param name="jsonPath">Absolute path to <c>node-api.json</c>.</param>
    public NodeApiMap(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);

        using var stream = File.OpenRead(jsonPath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Failed to deserialize '{jsonPath}'.");

        _map = new Dictionary<string, string>(raw, StringComparer.Ordinal);
    }

    /// <summary>
    /// Looks up <paramref name="nodeApiCall"/> (e.g. <c>fs.readFile</c>) and returns
    /// the mapped .NET member when found.
    /// </summary>
    /// <returns><see langword="true"/> when a mapping exists.</returns>
    public bool TryResolve(string nodeApiCall, out string dotNetMember)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeApiCall);
        return _map.TryGetValue(nodeApiCall, out dotNetMember!);
    }
}
