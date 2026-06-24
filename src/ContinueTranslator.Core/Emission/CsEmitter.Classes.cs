using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ContinueTranslator.Core.IR;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ContinueTranslator.Core.Emission;

internal sealed partial class CsEmitter
{
    /// <summary>
    /// Conservative line budget. Leaving headroom for namespace declaration and braces
    /// means the file always lands well under the 400-line hard limit.
    /// </summary>
    private const int LineBudget = 390;

    partial void EmitClasses(TsFile[] files, string commonRoot, List<EmittedFile> results)
    {
        foreach (TsFile file in files)
        {
            if (file.Classes.Length == 0) continue;

            string ns = DeriveNamespace(file.FilePath, commonRoot);
            string relDir = DeriveRelativePath(ns, string.Empty).TrimEnd('/');

            foreach (TsClass tsClass in file.Classes)
            {
                if (HasIgnoreCookie(tsClass.Cookies)) continue;

                string className = TryGetRenameCookie(tsClass.Cookies, out string renamed)
                    ? renamed
                    : tsClass.Name;

                EmitClassFiles(tsClass, className, ns, relDir, results);
            }
        }
    }

    private static void EmitClassFiles(
        TsClass tsClass,
        string className,
        string ns,
        string relDir,
        List<EmittedFile> results)
    {
        // Build all property members first.
        List<MemberDeclarationSyntax> properties = BuildClassProperties(tsClass.Properties);

        // Build all method stubs.
        List<MemberDeclarationSyntax> methods = BuildClassMethods(tsClass.Methods);

        // Measure the full class to decide whether splitting is needed.
        List<MemberDeclarationSyntax> allMembers = [.. properties, .. methods];
        string primaryText = RenderClass(className, tsClass, allMembers, ns);
        int primaryLines = CountLines(primaryText);

        if (primaryLines <= LineBudget)
        {
            // Fits in a single file.
            string fileName = className + ".cs";
            string relPath = relDir.Length > 0 ? $"{relDir}/{fileName}" : fileName;
            results.Add(new EmittedFile(relPath, primaryText));
            return;
        }

        // Split: keep properties + as many methods as fit in the primary file.
        SplitClassFiles(className, tsClass, ns, relDir, properties, methods, results);
    }

    private static void SplitClassFiles(
        string className,
        TsClass tsClass,
        string ns,
        string relDir,
        List<MemberDeclarationSyntax> properties,
        List<MemberDeclarationSyntax> methods,
        List<EmittedFile> results)
    {
        // Primary file: all properties + fill with methods until budget is reached.
        var primaryMembers = new List<MemberDeclarationSyntax>(properties);
        var overflow = new List<MemberDeclarationSyntax>();

        foreach (MemberDeclarationSyntax method in methods)
        {
            primaryMembers.Add(method);
            string probe = RenderClass(className, tsClass, primaryMembers, ns);
            if (CountLines(probe) > LineBudget)
            {
                primaryMembers.RemoveAt(primaryMembers.Count - 1);
                overflow.Add(method);
            }
        }

        EmitPartFile(className, tsClass, ns, relDir, primaryMembers, 1, results);

        // Overflow files: ClassName.Part2.cs, ClassName.Part3.cs, …
        int partNumber = 2;
        while (overflow.Count > 0)
        {
            var chunk = new List<MemberDeclarationSyntax>();
            var remaining = new List<MemberDeclarationSyntax>();

            foreach (MemberDeclarationSyntax method in overflow)
            {
                chunk.Add(method);
                string probe = RenderPartialClass(className, chunk, ns);
                if (CountLines(probe) > LineBudget)
                {
                    chunk.RemoveAt(chunk.Count - 1);
                    remaining.Add(method);
                }
            }

            // Guard against a single method exceeding the budget (emit it anyway).
            if (chunk.Count == 0 && remaining.Count > 0)
            {
                chunk.Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            EmitPartFile(className, tsClass, ns, relDir, chunk, partNumber, results);
            overflow = remaining;
            partNumber++;
        }
    }

    private static void EmitPartFile(
        string className,
        TsClass tsClass,
        string ns,
        string relDir,
        List<MemberDeclarationSyntax> members,
        int partNumber,
        List<EmittedFile> results)
    {
        string text = partNumber == 1
            ? RenderClass(className, tsClass, members, ns)
            : RenderPartialClass(className, members, ns);

        string fileSuffix = partNumber == 1 ? ".cs" : $".Part{partNumber}.cs";
        string fileName = className + fileSuffix;
        string relPath = relDir.Length > 0 ? $"{relDir}/{fileName}" : fileName;
        results.Add(new EmittedFile(relPath, text));
    }

    // -------------------------------------------------------------------------
    // Member builders
    // -------------------------------------------------------------------------

    private static List<MemberDeclarationSyntax> BuildClassProperties(TsProperty[] props)
    {
        var list = new List<MemberDeclarationSyntax>();

        foreach (TsProperty prop in props)
        {
            string typeText = prop.IsOptional && !prop.Type.Text.EndsWith('?')
                ? prop.Type.Text + "?"
                : prop.Type.Text;

            PropertyDeclarationSyntax propDecl = PropertyDeclaration(
                    ParseTypeSyntax(typeText),
                    Identifier(prop.Name))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithAccessorList(AccessorList(List(new[]
                {
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                })));

            if (HasTodoCookie(prop.Cookies))
                propDecl = WithLeadingLineComment(propDecl, "TODO");

            list.Add(propDecl);
        }

        return list;
    }

    private static List<MemberDeclarationSyntax> BuildClassMethods(TsMethod[] methods)
    {
        var list = new List<MemberDeclarationSyntax>();

        foreach (TsMethod method in methods)
        {
            ParameterListSyntax paramList = BuildParameterList(method.Parameters);

            StatementSyntax body = ThrowStatement(
                ObjectCreationExpression(IdentifierName("NotImplementedException"))
                    .WithArgumentList(ArgumentList()));

            MethodDeclarationSyntax methodDecl = MethodDeclaration(
                    ParseTypeSyntax(method.ReturnType.Text),
                    Identifier(method.Name))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithParameterList(paramList)
                .WithBody(Block(body));

            if (method.TypeParameters.Length > 0)
                methodDecl = methodDecl.WithTypeParameterList(BuildTypeParameterList(method.TypeParameters));

            if (HasTodoCookie(method.Cookies))
                methodDecl = WithLeadingLineComment(methodDecl, "TODO");

            list.Add(methodDecl);
        }

        return list;
    }

    // -------------------------------------------------------------------------
    // Rendering helpers
    // -------------------------------------------------------------------------

    private static string RenderClass(
        string className,
        TsClass tsClass,
        List<MemberDeclarationSyntax> members,
        string ns)
    {
        ClassDeclarationSyntax classDecl = ClassDeclaration(Identifier(className))
            .AddModifiers(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.PartialKeyword))
            .WithMembers(List(members));

        if (tsClass.BaseClass is not null)
        {
            classDecl = classDecl.WithBaseList(
                BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                    SimpleBaseType(ParseTypeName(tsClass.BaseClass)))));
        }

        return BuildCompilationUnit(ns, [classDecl]);
    }

    private static string RenderPartialClass(
        string className,
        List<MemberDeclarationSyntax> members,
        string ns)
    {
        ClassDeclarationSyntax classDecl = ClassDeclaration(Identifier(className))
            .AddModifiers(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.PartialKeyword))
            .WithMembers(List(members));

        return BuildCompilationUnit(ns, [classDecl]);
    }

    private static int CountLines(string text) =>
        text.Split('\n').Length;
}
