using System.Text.Json;

namespace ContinueTranslator.Core.Mapping;

/// <summary>
/// Reads <c>npm-packages.json</c> at construction time and resolves npm package names
/// (e.g. <c>axios</c>) to their .NET type or namespace equivalents.
/// </summary>
internal sealed partial class NpmPackageMap
{
    private readonly Dictionary<string, string> _map;

    /// <param name="jsonPath">Absolute path to <c>npm-packages.json</c>.</param>
    public NpmPackageMap(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);

        using var stream = File.OpenRead(jsonPath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Failed to deserialize '{jsonPath}'.");

        _map = new Dictionary<string, string>(raw, StringComparer.Ordinal);
    }

    /// <summary>
    /// Looks up <paramref name="npmPackage"/> (e.g. <c>axios</c>) and returns
    /// the mapped .NET type when found.
    /// </summary>
    /// <returns><see langword="true"/> when a mapping exists.</returns>
    public bool TryResolve(string npmPackage, out string dotNetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(npmPackage);
        return _map.TryGetValue(npmPackage, out dotNetType!);
    }

    /// <summary>
    /// Returns all mapped .NET type/namespace values, used by <c>ProjectEmitter</c>
    /// when collecting NuGet references.
    /// </summary>
    public IReadOnlyList<string> GetAllDotNetTypes() =>
        [.. _map.Values];
}
