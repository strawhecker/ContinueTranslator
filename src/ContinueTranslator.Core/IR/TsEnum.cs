namespace ContinueTranslator.Core.IR;

/// <summary>Represents a TypeScript enum declaration.</summary>
internal sealed record TsEnum(
    string Name,
    bool IsConst,
    bool IsExported,
    TsEnumMember[] Members,
    string[] Cookies);

