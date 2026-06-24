namespace ContinueTranslator.Core.IR;

/// <summary>Represents a property declaration on a TypeScript class or interface.</summary>
internal sealed record TsProperty(
    string Name,
    TsTypeRef Type,
    bool IsOptional,
    bool IsReadonly,
    bool IsStatic,
    /// <summary>Visibility modifier: <c>"public"</c>, <c>"protected"</c>, or <c>"private"</c>.</summary>
    string Accessibility,
    string[] Cookies);
