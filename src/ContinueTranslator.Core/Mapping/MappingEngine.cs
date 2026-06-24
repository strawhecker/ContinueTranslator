using ContinueTranslator.Core.IR;

namespace ContinueTranslator.Core.Mapping;

/// <summary>
/// Orchestrates <see cref="NodeApiMap"/>, <see cref="NpmPackageMap"/>, and
/// <see cref="TypeMap"/> over a <see cref="TsFile"/> array, producing a new array
/// with all resolvable type references replaced with their C# equivalents.
/// Unresolvable type references are tagged with an <c>@ct:todo=unresolved</c> cookie.
/// </summary>
internal sealed partial class MappingEngine
{
    private const string MapCookiePrefix = "@ct:map=";
    private const string TodoUnresolved = "@ct:todo=unresolved";

    private readonly NodeApiMap _nodeApi;
    private readonly NpmPackageMap _npmPackage;
    private readonly TypeMap _typeMap;

    public MappingEngine(NodeApiMap nodeApi, NpmPackageMap npmPackage, TypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(nodeApi);
        ArgumentNullException.ThrowIfNull(npmPackage);
        ArgumentNullException.ThrowIfNull(typeMap);

        _nodeApi = nodeApi;
        _npmPackage = npmPackage;
        _typeMap = typeMap;
    }

    /// <summary>
    /// Applies all mapping tables to every type reference in <paramref name="files"/> and
    /// returns a new array of <see cref="TsFile"/> records with resolved types.
    /// Input records are never mutated.
    /// </summary>
    public TsFile[] Apply(TsFile[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return [.. files.Select(ApplyFile)];
    }

    private TsFile ApplyFile(TsFile file) => file with
    {
        Imports = [.. file.Imports.Select(ApplyImport)],
        Classes = [.. file.Classes.Select(ApplyClass)],
        Interfaces = [.. file.Interfaces.Select(ApplyInterface)],
        Functions = [.. file.Functions.Select(ApplyFunction)],
    };

    private TsImport ApplyImport(TsImport import)
    {
        // Check @ct:map= cookie on the import node.
        if (TryGetMapCookie(import.Cookies, out string mapped))
            return import with { ModuleSpecifier = mapped };

        // Try npm-package map on the module specifier.
        if (!string.IsNullOrWhiteSpace(import.ModuleSpecifier) &&
            _npmPackage.TryResolve(import.ModuleSpecifier, out string dotNetType))
            return import with { ModuleSpecifier = dotNetType };

        // Try stripping leading '@' scope prefix (e.g. "@scope/pkg" → "pkg").
        string bare = import.ModuleSpecifier.TrimStart('@').Split('/')[^1];
        if (!string.IsNullOrWhiteSpace(bare) &&
            _npmPackage.TryResolve(bare, out string dotNetType2))
            return import with { ModuleSpecifier = dotNetType2 };

        return import;
    }

    private TsClass ApplyClass(TsClass cls) => cls with
    {
        Properties = [.. cls.Properties.Select(ApplyProperty)],
        Methods = [.. cls.Methods.Select(ApplyMethod)],
    };

    private TsInterface ApplyInterface(TsInterface iface) => iface with
    {
        Properties = [.. iface.Properties.Select(ApplyProperty)],
        Methods = [.. iface.Methods.Select(ApplyMethod)],
    };

    private TsProperty ApplyProperty(TsProperty prop)
    {
        if (TryGetMapCookie(prop.Cookies, out string overrideType))
        {
            var overrideRef = new TsTypeRef(overrideType, overrideType, [], false);
            return prop with { Type = overrideRef };
        }

        var (resolved, wasResolved) = ResolveTypeRef(prop.Type);
        if (!wasResolved)
            return prop with { Type = resolved, Cookies = AppendTodoUnresolved(prop.Cookies) };

        return prop with { Type = resolved };
    }

    private TsMethod ApplyMethod(TsMethod method)
    {
        TsTypeRef returnType;
        string[] cookies = method.Cookies;

        if (TryGetMapCookie(method.Cookies, out string overrideType))
        {
            returnType = new TsTypeRef(overrideType, overrideType, [], false);
        }
        else
        {
            var (resolved, wasResolved) = ResolveTypeRef(method.ReturnType);
            returnType = resolved;
            if (!wasResolved)
                cookies = AppendTodoUnresolved(cookies);
        }

        return method with
        {
            ReturnType = returnType,
            Parameters = [.. method.Parameters.Select(ApplyParameter)],
            Cookies = cookies,
        };
    }

    private TsParameter ApplyParameter(TsParameter param)
    {
        if (TryGetMapCookie(param.Cookies, out string overrideType))
        {
            var overrideRef = new TsTypeRef(overrideType, overrideType, [], false);
            return param with { Type = overrideRef };
        }

        var (resolved, wasResolved) = ResolveTypeRef(param.Type);
        if (!wasResolved)
            return param with { Type = resolved, Cookies = AppendTodoUnresolved(param.Cookies) };

        return param with { Type = resolved };
    }

    private TsFunction ApplyFunction(TsFunction func)
    {
        TsTypeRef returnType;
        string[] cookies = func.Cookies;

        if (TryGetMapCookie(func.Cookies, out string overrideType))
        {
            returnType = new TsTypeRef(overrideType, overrideType, [], false);
        }
        else
        {
            var (resolved, wasResolved) = ResolveTypeRef(func.ReturnType);
            returnType = resolved;
            if (!wasResolved)
                cookies = AppendTodoUnresolved(cookies);
        }

        return func with
        {
            ReturnType = returnType,
            Parameters = [.. func.Parameters.Select(ApplyParameter)],
            Cookies = cookies,
        };
    }

    /// <summary>
    /// Recursively resolves a <see cref="TsTypeRef"/>, mapping type args first, then the
    /// base name.  Returns the (possibly updated) ref and whether every node resolved.
    /// </summary>
    private (TsTypeRef Ref, bool WasResolved) ResolveTypeRef(TsTypeRef typeRef)
    {
        // Recursively resolve type arguments first.
        bool allArgsResolved = true;
        TsTypeRef[] resolvedArgs = typeRef.TypeArgs.Length == 0
            ? typeRef.TypeArgs
            : [.. typeRef.TypeArgs.Select(arg =>
            {
                var (r, ok) = ResolveTypeRef(arg);
                if (!ok) allArgsResolved = false;
                return r;
            })];

        // Attempt to resolve the base name.
        string resolvedName = ResolveName(typeRef.Name, out bool nameResolved);

        // Rebuild the Text representation with resolved parts.
        string resolvedText = resolvedArgs.Length > 0
            ? $"{resolvedName}<{string.Join(", ", resolvedArgs.Select(a => a.Text))}>"
            : typeRef.IsArray
                ? $"{resolvedName}[]"
                : resolvedName;

        var result = typeRef with
        {
            Name = resolvedName,
            Text = resolvedText,
            TypeArgs = resolvedArgs,
        };

        return (result, nameResolved && allArgsResolved);
    }

    /// <summary>
    /// Resolves a single type base name against TypeMap, NodeApiMap, and NpmPackageMap.
    /// </summary>
    private string ResolveName(string name, out bool resolved)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            resolved = false;
            return name;
        }

        // TypeMap covers TS primitives and well-known generics.
        string mapped = _typeMap.Resolve(name);
        if (!string.Equals(mapped, name, StringComparison.Ordinal))
        {
            resolved = true;
            return mapped;
        }

        // NodeApiMap (e.g. "fs.readFile").
        if (_nodeApi.TryResolve(name, out string nodeResult))
        {
            resolved = true;
            return nodeResult;
        }

        // NpmPackageMap (e.g. "axios").
        if (_npmPackage.TryResolve(name, out string npmResult))
        {
            resolved = true;
            return npmResult;
        }

        // Unresolved — return the original name, caller decides whether to tag.
        resolved = false;
        return name;
    }

    // ── Cookie helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> and the override value when any cookie starts with
    /// <c>@ct:map=</c>.
    /// </summary>
    private static bool TryGetMapCookie(string[] cookies, out string value)
    {
        foreach (string cookie in cookies)
        {
            if (cookie.StartsWith(MapCookiePrefix, StringComparison.Ordinal))
            {
                value = cookie[MapCookiePrefix.Length..];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string[] AppendTodoUnresolved(string[] cookies)
    {
        // Only append once.
        if (Array.IndexOf(cookies, TodoUnresolved) >= 0)
            return cookies;

        return [.. cookies, TodoUnresolved];
    }
}

