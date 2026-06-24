namespace ContinueTranslator.Core.IR;

/// <summary>Represents a TypeScript interface declaration.</summary>
internal sealed record TsInterface(
    string Name,
    string[] TypeParameters,
    /// <summary>Names of extended interfaces.</summary>
    string[] Extends,
    TsProperty[] Properties,
    TsMethod[] Methods,
    bool IsExported,
    string[] Cookies);

