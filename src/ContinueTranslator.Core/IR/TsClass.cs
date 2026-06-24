namespace ContinueTranslator.Core.IR;

/// <summary>Represents a TypeScript class declaration.</summary>
internal sealed record TsClass(
    string Name,
    string[] TypeParameters,
    /// <summary>Base class name, or <see langword="null"/> when there is no <c>extends</c> clause.</summary>
    string? BaseClass,
    /// <summary>Names of implemented interfaces.</summary>
    string[] Implements,
    TsProperty[] Properties,
    TsMethod[] Methods,
    bool IsAbstract,
    bool IsExported,
    string[] Cookies);

