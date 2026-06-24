using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ContinueTranslator.Core.IR;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ContinueTranslator.Core.Emission;

internal sealed partial class CsEmitter
{
    partial void EmitTypeAliases(TsFile[] files, string commonRoot, List<EmittedFile> results)
    {
        foreach (TsFile file in files)
        {
            if (file.TypeAliases.Length == 0) continue;

            string ns = DeriveNamespace(file.FilePath, commonRoot);
            var members = new List<MemberDeclarationSyntax>();

            foreach (TsTypeAlias alias in file.TypeAliases)
            {
                if (HasIgnoreCookie(alias.Cookies)) continue;

                // Emit a placeholder empty static class: valid C# that survives the build.
                // The TODO comment carries the full TS alias definition for human review.
                ClassDeclarationSyntax placeholder = ClassDeclaration(
                        Identifier(alias.Name + "Alias"))
                    .AddModifiers(
                        Token(SyntaxKind.PublicKeyword),
                        Token(SyntaxKind.StaticKeyword));

                string comment = $"TODO type alias: {alias.Name} = {alias.TypeText}";
                placeholder = WithLeadingLineComment(placeholder, comment);

                members.Add(placeholder);
            }

            if (members.Count == 0) continue;

            string relDir = DeriveRelativePath(ns, string.Empty).TrimEnd('/');
            string fileName = Path.GetFileNameWithoutExtension(file.FilePath) + ".TypeAliases.cs";
            string relativePath = relDir.Length > 0 ? $"{relDir}/{fileName}" : fileName;

            string content = BuildCompilationUnit(ns, members);
            results.Add(new EmittedFile(relativePath, content));
        }
    }

    partial void EmitFunctions(TsFile[] files, string commonRoot, List<EmittedFile> results)
    {
        foreach (TsFile file in files)
        {
            if (file.Functions.Length == 0) continue;

            string ns = DeriveNamespace(file.FilePath, commonRoot);

            // Class name: PascalCase of the filename (without extension) + "Functions".
            string baseName = ToPascalCase(
                Path.GetFileNameWithoutExtension(file.FilePath));
            string className = baseName + "Functions";

            var methodMembers = new List<MemberDeclarationSyntax>();

            foreach (TsFunction func in file.Functions)
            {
                if (HasIgnoreCookie(func.Cookies)) continue;

                string methodName = TryGetRenameCookie(func.Cookies, out string renamed)
                    ? renamed
                    : func.Name;

                MethodDeclarationSyntax methodDecl = BuildFunctionStub(func, methodName);
                methodMembers.Add(methodDecl);
            }

            if (methodMembers.Count == 0) continue;

            ClassDeclarationSyntax classDecl = ClassDeclaration(Identifier(className))
                .AddModifiers(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.StaticKeyword),
                    Token(SyntaxKind.PartialKeyword))
                .WithMembers(List(methodMembers));

            string relDir = DeriveRelativePath(ns, string.Empty).TrimEnd('/');
            string fileName = className + ".cs";
            string relativePath = relDir.Length > 0 ? $"{relDir}/{fileName}" : fileName;

            string content = BuildCompilationUnit(ns, [classDecl]);
            results.Add(new EmittedFile(relativePath, content));
        }
    }

    private static MethodDeclarationSyntax BuildFunctionStub(TsFunction func, string methodName)
    {
        ParameterListSyntax paramList = BuildParameterList(func.Parameters);

        // Stub body: throw new NotImplementedException();
        StatementSyntax body = ThrowStatement(
            ObjectCreationExpression(IdentifierName("NotImplementedException"))
                .WithArgumentList(ArgumentList()));

        MethodDeclarationSyntax methodDecl = MethodDeclaration(
                ParseTypeSyntax(func.ReturnType.Text),
                Identifier(methodName))
            .AddModifiers(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.StaticKeyword))
            .WithParameterList(paramList)
            .WithBody(Block(body));

        if (func.TypeParameters.Length > 0)
            methodDecl = methodDecl.WithTypeParameterList(BuildTypeParameterList(func.TypeParameters));

        return methodDecl;
    }
}
