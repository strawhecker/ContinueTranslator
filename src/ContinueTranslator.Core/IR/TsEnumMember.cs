namespace ContinueTranslator.Core.IR;

/// <summary>Represents a single member of a TypeScript enum declaration.</summary>
internal sealed record TsEnumMember(
    string Name,
    /// <summary>Literal initializer text, or <see langword="null"/> when auto-assigned.</summary>
    string? Value,
    string[] Cookies);
