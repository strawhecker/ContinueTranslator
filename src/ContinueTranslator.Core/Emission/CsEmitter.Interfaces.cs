using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ContinueTranslator.Core.IR;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ContinueTranslator.Core.Emission;

internal sealed partial class CsEmitter
{
    partial void EmitInterfaces(TsFile[] files, string commonRoot, List<EmittedFile> results)
    {
        foreach (TsFile file in files)
        {
            if (file.Interfaces.Length == 0) continue;

            string ns = DeriveNamespace(file.FilePath, commonRoot);
            var members = new List<MemberDeclarationSyntax>();

            foreach (TsInterface tsIface in file.Interfaces)
            {
                if (HasIgnoreCookie(tsIface.Cookies)) continue;

                string ifaceName = TryGetRenameCookie(tsIface.Cookies, out string renamed)
                    ? renamed
                    : tsIface.Name;

                InterfaceDeclarationSyntax ifaceDecl = BuildInterfaceDeclaration(tsIface, ifaceName);
                members.Add(ifaceDecl);
            }

            if (members.Count == 0) continue;

            string relDir = DeriveRelativePath(ns, string.Empty).TrimEnd('/');
            string fileName = Path.GetFileNameWithoutExtension(file.FilePath) + ".Interfaces.cs";
            string relativePath = relDir.Length > 0 ? $"{relDir}/{fileName}" : fileName;

            string content = BuildCompilationUnit(ns, members);
            results.Add(new EmittedFile(relativePath, content));
        }
    }

    private static InterfaceDeclarationSyntax BuildInterfaceDeclaration(
        TsInterface tsIface, string ifaceName)
    {
        var memberSyntaxList = new List<MemberDeclarationSyntax>();

        // Properties
        foreach (TsProperty prop in tsIface.Properties)
        {
            PropertyDeclarationSyntax propDecl = BuildInterfaceProperty(prop);
            memberSyntaxList.Add(propDecl);
        }

        // Methods
        foreach (TsMethod method in tsIface.Methods)
        {
            MethodDeclarationSyntax methodDecl = BuildInterfaceMethod(method);
            memberSyntaxList.Add(methodDecl);
        }

        InterfaceDeclarationSyntax ifaceDecl = InterfaceDeclaration(Identifier(ifaceName))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .WithMembers(List(memberSyntaxList));

        // Base interfaces (extends)
        if (tsIface.Extends.Length > 0)
        {
            BaseTypeSyntax[] baseTypes = tsIface.Extends
                .Select(e => (BaseTypeSyntax)SimpleBaseType(ParseTypeName(e)))
                .ToArray();
            ifaceDecl = ifaceDecl.WithBaseList(BaseList(SeparatedList(baseTypes)));
        }

        return ifaceDecl;
    }

    private static PropertyDeclarationSyntax BuildInterfaceProperty(TsProperty prop)
    {
        string typeText = prop.IsOptional && !prop.Type.Text.EndsWith('?')
            ? prop.Type.Text + "?"
            : prop.Type.Text;

        PropertyDeclarationSyntax propDecl = PropertyDeclaration(
                ParseTypeSyntax(typeText),
                Identifier(prop.Name))
            .WithAccessorList(AccessorList(List(new[]
            {
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
            })));

        if (HasTodoCookie(prop.Cookies))
            propDecl = WithLeadingLineComment(propDecl, "TODO");

        return propDecl;
    }

    private static MethodDeclarationSyntax BuildInterfaceMethod(TsMethod method)
    {
        ParameterListSyntax paramList = BuildParameterList(method.Parameters);

        MethodDeclarationSyntax methodDecl = MethodDeclaration(
                ParseTypeSyntax(method.ReturnType.Text),
                Identifier(method.Name))
            .WithParameterList(paramList)
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        if (method.TypeParameters.Length > 0)
            methodDecl = methodDecl.WithTypeParameterList(BuildTypeParameterList(method.TypeParameters));

        if (HasTodoCookie(method.Cookies))
            methodDecl = WithLeadingLineComment(methodDecl, "TODO");

        return methodDecl;
    }

    // -------------------------------------------------------------------------
    // Shared parameter / type-parameter helpers (used by Interfaces + Classes)
    // -------------------------------------------------------------------------

    internal static ParameterListSyntax BuildParameterList(TsParameter[] parameters)
    {
        var paramSyntaxes = new List<ParameterSyntax>();

        foreach (TsParameter p in parameters)
        {
            string typeText = p.IsOptional && !p.Type.Text.EndsWith('?')
                ? p.Type.Text + "?"
                : p.Type.Text;

            ParameterSyntax paramSyntax = Parameter(Identifier(p.Name))
                .WithType(ParseTypeSyntax(typeText));

            if (p.IsRest)
                paramSyntax = paramSyntax.AddModifiers(Token(SyntaxKind.ParamsKeyword));

            paramSyntaxes.Add(paramSyntax);
        }

        return ParameterList(SeparatedList(paramSyntaxes));
    }

    internal static TypeParameterListSyntax BuildTypeParameterList(string[] typeParams)
    {
        TypeParameterSyntax[] tpSyntaxes = typeParams
            .Select(tp => TypeParameter(Identifier(tp)))
            .ToArray();
        return TypeParameterList(SeparatedList(tpSyntaxes));
    }
}
