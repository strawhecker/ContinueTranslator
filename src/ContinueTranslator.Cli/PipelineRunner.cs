using System.Reflection;
using ContinueTranslator.Core.Emission;
using ContinueTranslator.Core.IR;
using ContinueTranslator.Core.Mapping;
using ContinueTranslator.Core.Parsing;

namespace ContinueTranslator.Cli;

/// <summary>
/// Constructs all pipeline dependencies and runs the full TypeScript-to-C# translation
/// for a given <see cref="TranslationOptions"/>.
/// </summary>
internal sealed class PipelineRunner
{
    private readonly MappingEngine _mappingEngine;
    private readonly NuGetPackageResolver _nugetResolver;
    private readonly TsParser _tsParser;
    private readonly CsEmitter _csEmitter;
    private readonly ProjectEmitter _projectEmitter;

    /// <summary>
    /// Initialises all pipeline components. The mappings folder is resolved as one directory
    /// above the directory containing the running assembly.
    /// </summary>
    public PipelineRunner()
    {
        string assemblyDir = Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();

        string mappingsDir = Path.Combine(assemblyDir, "..", "mappings");

        string nodeApiPath = Path.GetFullPath(Path.Combine(mappingsDir, "node-api.json"));
        string npmPackagesPath = Path.GetFullPath(Path.Combine(mappingsDir, "npm-packages.json"));
        string typesPath = Path.GetFullPath(Path.Combine(mappingsDir, "types.json"));

        var nodeApiMap = new NodeApiMap(nodeApiPath);
        var npmPackageMap = new NpmPackageMap(npmPackagesPath);
        var typeMap = new TypeMap(typesPath);

        _mappingEngine = new MappingEngine(nodeApiMap, npmPackageMap, typeMap);
        _nugetResolver = new NuGetPackageResolver(npmPackageMap);
        _tsParser = new TsParser();
        _csEmitter = new CsEmitter();
        _projectEmitter = new ProjectEmitter(_nugetResolver);
    }

    /// <summary>
    /// Runs the full pipeline: checkout → scan → parse → map → emit → write.
    /// </summary>
    /// <param name="options">Validated CLI arguments.</param>
    public void Run(TranslationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 1. Checkout and scan.
        var scanner = new RepoScanner();
        IReadOnlyList<string> tsPaths = scanner.CheckoutAndScan(options.RepoPath, options.Tag);

        // 2. Parse.
        TsFile[] parsed = _tsParser.Parse(tsPaths);

        // 3. Apply mappings.
        TsFile[] mapped = _mappingEngine.Apply(parsed);

        // 4. Emit C# source files.
        IReadOnlyList<EmittedFile> emitted = _csEmitter.Emit(mapped, options.OutDirectory);
        var files = new List<EmittedFile>(emitted);

        // 5. Emit project file and append.
        EmittedFile csproj = _projectEmitter.Emit(mapped, "net10.0");
        files.Add(csproj);

        // 6. Write all files to disk.
        foreach (EmittedFile file in files)
        {
            string fullPath = Path.Combine(options.OutDirectory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        Console.WriteLine($"Wrote {files.Count} file(s) to '{options.OutDirectory}'.");
    }
}
