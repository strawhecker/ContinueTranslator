namespace ContinueTranslator.Core.IR;

/// <summary>Represents a single parameter in a TypeScript function or method signature.</summary>
internal sealed record TsParameter(
    string Name,
    TsTypeRef Type,
    bool IsOptional,
    bool IsRest,
    /// <summary><see langword="true"/> when the parameter carries a default value expression.</summary>
    bool HasInitializer,
    string[] Cookies);
