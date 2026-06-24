namespace ContinueTranslator.Core.IR;

/// <summary>Represents a TypeScript type alias declaration (<c>type Foo = ...</c>).</summary>
internal sealed record TsTypeAlias(
    string Name,
    string[] TypeParameters,
    /// <summary>Full text of the right-hand side type expression.</summary>
    string TypeText,
    bool IsExported,
    string[] Cookies);
