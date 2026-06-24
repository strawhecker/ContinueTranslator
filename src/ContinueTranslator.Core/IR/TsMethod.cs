namespace ContinueTranslator.Core.IR;

/// <summary>Represents a method declaration on a TypeScript class or interface.</summary>
internal sealed record TsMethod(
    string Name,
    TsTypeRef ReturnType,
    TsParameter[] Parameters,
    string[] TypeParameters,
    bool IsAsync,
    bool IsStatic,
    bool IsOptional,
    bool IsAbstract,
    /// <summary>Visibility modifier: <c>"public"</c>, <c>"protected"</c>, or <c>"private"</c>.</summary>
    string Accessibility,
    string[] Cookies);
