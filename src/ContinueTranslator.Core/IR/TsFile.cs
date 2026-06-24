namespace ContinueTranslator.Core.IR;

/// <summary>Root IR node representing one parsed TypeScript source file.</summary>
internal sealed record TsFile(
    /// <summary>Absolute path to the <c>.ts</c> source file.</summary>
    string FilePath,
    TsImport[] Imports,
    TsClass[] Classes,
    TsInterface[] Interfaces,
    TsEnum[] Enums,
    TsFunction[] Functions,
    TsTypeAlias[] TypeAliases,
    string[] Cookies);

