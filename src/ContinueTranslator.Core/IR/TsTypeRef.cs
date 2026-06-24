namespace ContinueTranslator.Core.IR;

/// <summary>Represents a resolved TypeScript type reference (e.g. <c>Map&lt;string, number&gt;</c>).</summary>
internal sealed record TsTypeRef(
    /// <summary>Full rendered text of the type (e.g. <c>Map&lt;string, number&gt;</c>).</summary>
    string Text,
    /// <summary>Base name without type arguments (e.g. <c>Map</c>).</summary>
    string Name,
    /// <summary>Type arguments, if any.</summary>
    TsTypeRef[] TypeArgs,
    /// <summary><see langword="true"/> when the type is an array type (e.g. <c>string[]</c>).</summary>
    bool IsArray);

