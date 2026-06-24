namespace ContinueTranslator.Core.Emission;

/// <summary>A single C# source file produced by <see cref="CsEmitter"/>.</summary>
internal sealed record EmittedFile(
    /// <summary>Path relative to the output root (e.g. <c>Protocol/MyEnum.cs</c>).</summary>
    string RelativePath,
    /// <summary>Full C# source text of the file.</summary>
    string Content);
