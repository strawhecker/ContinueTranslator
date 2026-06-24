namespace ContinueTranslator.Cli;

/// <summary>
/// Validated argument bag passed from <c>Program.cs</c> to the translation pipeline.
/// </summary>
internal sealed record TranslationOptions(
    /// <summary>Absolute path to the local clone of the forked Continue repository.</summary>
    string RepoPath,
    /// <summary>Git tag or branch to check out before scanning.</summary>
    string Tag,
    /// <summary>Absolute path to the output directory for generated C# files.</summary>
    string OutDirectory);
