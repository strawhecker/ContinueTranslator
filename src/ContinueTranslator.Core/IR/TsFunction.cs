namespace ContinueTranslator.Core.IR;

/// <summary>Represents a top-level TypeScript function declaration.</summary>
internal sealed record TsFunction(
    string Name,
    TsTypeRef ReturnType,
    TsParameter[] Parameters,
    string[] TypeParameters,
    bool IsAsync,
    bool IsExported,
    string[] Cookies);

