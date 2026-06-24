namespace ContinueTranslator.Core.IR;

/// <summary>Represents a TypeScript import declaration.</summary>
internal sealed record TsImport(
    /// <summary>Module specifier string (e.g. <c>"./foo"</c> or <c>"fs"</c>).</summary>
    string ModuleSpecifier,
    /// <summary>Named bindings imported (e.g. <c>["readFile", "writeFile"]</c>).</summary>
    string[] NamedImports,
    /// <summary>Default import name, or <see langword="null"/> when absent.</summary>
    string? DefaultImport,
    /// <summary>Namespace import alias (e.g. <c>* as fs</c>), or <see langword="null"/> when absent.</summary>
    string? NamespaceImport,
    string[] Cookies);
