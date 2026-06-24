using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ContinueTranslator.Core.IR;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ContinueTranslator.Core.Emission;

internal sealed partial class CsEmitter
{
    // -------------------------------------------------------------------------
    // Cookie constants
    // -------------------------------------------------------------------------

    private const string CookieIgnore = "@ct:ignore";
    private const string CookieRenamePrefix = "@ct:rename=";
    private const string CookieTodo = "@ct:todo";

    // -------------------------------------------------------------------------
    // CollectResults — sorts results by RelativePath (deterministic output)
    // -------------------------------------------------------------------------

    partial void CollectResults(List<EmittedFile> results)
    {
        results.Sort(static (a, b) =>
            StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
    }

    // -------------------------------------------------------------------------
    // Namespace / path helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the longest common absolute directory prefix shared by all file paths
    /// in <paramref name="files"/>, or the directory of the first file when there is
    /// only one entry.
    /// </summary>
    internal static string ResolveCommonRoot(TsFile[] files)
    {
        if (files.Length == 0)
            return string.Empty;

        if (files.Length == 1)
            return Path.GetDirectoryName(files[0].FilePath) ?? string.Empty;

        string[] parts = files[0].FilePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int commonLength = parts.Length;

        foreach (TsFile file in files.Skip(1))
        {
            string[] cur = file.FilePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int shared = 0;
            while (shared < commonLength && shared < cur.Length &&
                   string.Equals(parts[shared], cur[shared], StringComparison.OrdinalIgnoreCase))
            {
                shared++;
            }
            commonLength = shared;
        }

        // commonLength is the number of path segments shared — rejoin without the final filename segment.
        // We want a directory, so use up to commonLength segments.
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(commonLength));
    }

    /// <summary>
    /// Derives a dotted C# namespace from <paramref name="absoluteFilePath"/> relative to
    /// <paramref name="commonRoot"/>, prefixed with <c>ContinueCore</c>.
    /// Example: <c>/repo/core/protocol/types.ts</c> relative to <c>/repo/core</c>
    /// → <c>ContinueCore.Protocol</c>.
    /// </summary>
    internal static string DeriveNamespace(string absoluteFilePath, string commonRoot)
    {
        string rel = Path.GetRelativePath(commonRoot, Path.GetDirectoryName(absoluteFilePath) ?? string.Empty)
                         .Replace('\\', '/')
                         .Trim('/');

        if (rel == "." || rel == string.Empty)
            return "ContinueCore";

        // Strip leading "core/" segment if present (Continue's source lives under core/).
        if (rel.StartsWith("core/", StringComparison.OrdinalIgnoreCase))
            rel = rel["core/".Length..];

        if (rel == string.Empty)
            return "ContinueCore";

        IEnumerable<string> parts = rel.Split('/').Select(ToPascalCase);
        return "ContinueCore." + string.Join('.', parts);
    }

    /// <summary>
    /// Returns the relative output path (using forward slashes) for a file that belongs to
    /// <paramref name="ns"/> and whose base name is <paramref name="fileName"/>.
    /// Example: ns=<c>ContinueCore.Protocol</c>, fileName=<c>MyEnum.cs</c>
    /// → <c>Protocol/MyEnum.cs</c>.
    /// </summary>
    internal static string DeriveRelativePath(string ns, string fileName)
    {
        const string prefix = "ContinueCore";
        string sub = ns.StartsWith(prefix + ".", StringComparison.Ordinal)
            ? ns[(prefix.Length + 1)..].Replace('.', '/')
            : string.Empty;

        return sub.Length > 0 ? $"{sub}/{fileName}" : fileName;
    }

    // -------------------------------------------------------------------------
    // Cookie helpers
    // -------------------------------------------------------------------------

    internal static bool HasIgnoreCookie(string[] cookies) =>
        cookies.Any(static c => c.Equals(CookieIgnore, StringComparison.OrdinalIgnoreCase));

    internal static bool TryGetRenameCookie(string[] cookies, out string newName)
    {
        foreach (string cookie in cookies)
        {
            if (cookie.StartsWith(CookieRenamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                newName = cookie[CookieRenamePrefix.Length..].Trim();
                return newName.Length > 0;
            }
        }
        newName = string.Empty;
        return false;
    }

    internal static bool HasTodoCookie(string[] cookies) =>
        cookies.Any(static c => c.StartsWith(CookieTodo, StringComparison.OrdinalIgnoreCase));

    // -------------------------------------------------------------------------
    // Trivia / comment helpers
    // -------------------------------------------------------------------------

    /// <summary>Wraps a node with a leading <c>// {text}</c> single-line comment trivia.</summary>
    internal static T WithLeadingLineComment<T>(T node, string text) where T : SyntaxNode
    {
        SyntaxTrivia comment = Comment($"// {text}");
        SyntaxTrivia eol = ElasticCarriageReturnLineFeed;
        return node.WithLeadingTrivia(node.GetLeadingTrivia().InsertRange(0, [comment, eol]));
    }

    // -------------------------------------------------------------------------
    // Type-name helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a TypeScript identifier segment to PascalCase.
    /// E.g. <c>my-segment</c> → <c>MySegment</c>, <c>mySegment</c> → <c>MySegment</c>.
    /// </summary>
    internal static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // Split on hyphens and underscores, then capitalise each word.
        string[] words = s.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>
    /// Converts a raw type-text string into a <see cref="TypeSyntax"/> the compiler accepts.
    /// Defaults to <c>object</c> when the text is empty or unparseable.
    /// </summary>
    internal static TypeSyntax ParseTypeSyntax(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return PredefinedType(Token(SyntaxKind.ObjectKeyword));

        try
        {
            return (TypeSyntax)SyntaxFactory.ParseTypeName(text);
        }
        catch
        {
            return PredefinedType(Token(SyntaxKind.ObjectKeyword));
        }
    }

    // -------------------------------------------------------------------------
    // Compilation-unit builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wraps <paramref name="members"/> in a file-scoped namespace declaration and returns
    /// the normalised source text.
    /// </summary>
    internal static string BuildCompilationUnit(string ns, IEnumerable<MemberDeclarationSyntax> members)
    {
        FileScopedNamespaceDeclarationSyntax nsDecl =
            FileScopedNamespaceDeclaration(ParseName(ns))
                .WithMembers(List(members))
                .NormalizeWhitespace();

        CompilationUnitSyntax cu = CompilationUnit()
            .WithMembers(SingletonList<MemberDeclarationSyntax>(nsDecl))
            .NormalizeWhitespace();

        return cu.ToFullString();
    }
}
