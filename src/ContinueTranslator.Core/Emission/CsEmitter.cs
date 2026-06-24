using ContinueTranslator.Core.IR;

namespace ContinueTranslator.Core.Emission;

/// <summary>
/// Converts an array of mapped <see cref="TsFile"/> IR nodes into emitted C# source files
/// using Roslyn SyntaxFactory. Each conceptual group is implemented in a dedicated partial file.
/// </summary>
internal sealed partial class CsEmitter
{
    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits C# source files for all IR nodes in <paramref name="files"/>.
    /// </summary>
    /// <param name="files">Mapped IR nodes (MappingEngine must have already run).</param>
    /// <param name="outputDirectory">
    /// Absolute path to the output root used to derive relative paths for each emitted file.
    /// </param>
    /// <returns>Deterministically ordered list of emitted files.</returns>
    public IReadOnlyList<EmittedFile> Emit(TsFile[] files, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string commonRoot = ResolveCommonRoot(files);
        var results = new List<EmittedFile>();

        EmitEnums(files, commonRoot, results);
        EmitInterfaces(files, commonRoot, results);
        EmitTypeAliases(files, commonRoot, results);
        EmitFunctions(files, commonRoot, results);
        EmitClasses(files, commonRoot, results);
        CollectResults(results);

        return results.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // Partial method declarations (implemented in dedicated partial files)
    // -------------------------------------------------------------------------

    partial void EmitEnums(TsFile[] files, string commonRoot, List<EmittedFile> results);
    partial void EmitInterfaces(TsFile[] files, string commonRoot, List<EmittedFile> results);
    partial void EmitTypeAliases(TsFile[] files, string commonRoot, List<EmittedFile> results);
    partial void EmitFunctions(TsFile[] files, string commonRoot, List<EmittedFile> results);
    partial void EmitClasses(TsFile[] files, string commonRoot, List<EmittedFile> results);

    /// <summary>Post-processes <paramref name="results"/> in-place (sorting, de-dup, etc.).</summary>
    partial void CollectResults(List<EmittedFile> results);
}
