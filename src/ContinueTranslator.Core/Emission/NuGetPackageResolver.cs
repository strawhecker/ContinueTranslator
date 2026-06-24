using ContinueTranslator.Core.IR;
using ContinueTranslator.Core.Mapping;

namespace ContinueTranslator.Core.Emission;

/// <summary>
/// Resolves the set of NuGet package IDs needed by a translated C# project.
/// Sources are (1) <c>@ct:nuget=PackageId</c> cookies on any IR node and
/// (2) the .NET type strings returned by <see cref="NpmPackageMap.GetAllDotNetTypes"/>
/// matched against a hardcoded prefix table.
/// </summary>
internal sealed class NuGetPackageResolver
{
    private const string NugetCookiePrefix = "@ct:nuget=";

    // Maps a .NET namespace/type prefix to the NuGet package that delivers it.
    // Types that are part of the base runtime (e.g. System.IO, System.Collections)
    // are intentionally absent — they need no NuGet reference.
    private static readonly (string Prefix, string PackageId)[] s_prefixTable =
    [
        ("System.Net.Http",                           "System.Net.Http"),
        ("System.Net.WebSockets",                     "System.Net.WebSockets"),
        ("System.Net.Security",                       "System.Net.Security"),
        ("System.Net.Sockets",                        "System.Net.Sockets"),
        ("System.Text.Json",                          "System.Text.Json"),
        ("System.Text.Encodings.Web",                 "System.Text.Encodings.Web"),
        ("System.Security.Cryptography",              "System.Security.Cryptography.Algorithms"),
        ("Microsoft.Extensions.DependencyInjection",  "Microsoft.Extensions.DependencyInjection"),
        ("Microsoft.Extensions.Logging",              "Microsoft.Extensions.Logging"),
        ("Microsoft.Extensions.Configuration",        "Microsoft.Extensions.Configuration"),
        ("Microsoft.Extensions.Hosting",              "Microsoft.Extensions.Hosting"),
        ("Microsoft.Extensions.Options",              "Microsoft.Extensions.Options"),
        ("Microsoft.Extensions.Http",                 "Microsoft.Extensions.Http"),
        ("Microsoft.Extensions",                      "Microsoft.Extensions.Primitives"),
    ];

    private readonly NpmPackageMap _npmPackageMap;

    /// <param name="npmPackageMap">
    /// The map used to derive .NET types from npm package names; its
    /// <see cref="NpmPackageMap.GetAllDotNetTypes"/> result is inspected for NuGet needs.
    /// </param>
    public NuGetPackageResolver(NpmPackageMap npmPackageMap)
    {
        ArgumentNullException.ThrowIfNull(npmPackageMap);
        _npmPackageMap = npmPackageMap;
    }

    /// <summary>
    /// Returns the deduplicated, sorted set of NuGet package IDs required by
    /// <paramref name="files"/>.
    /// </summary>
    public IReadOnlySet<string> Resolve(TsFile[] files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectCookiePackages(files, ids);
        CollectMappedTypePackages(ids);

        return ids;
    }

    // ── Cookie scanning ───────────────────────────────────────────────────────

    private static void CollectCookiePackages(TsFile[] files, SortedSet<string> ids)
    {
        foreach (TsFile file in files)
        {
            AddFromCookies(file.Cookies, ids);

            foreach (TsImport import in file.Imports)
                AddFromCookies(import.Cookies, ids);

            foreach (TsClass cls in file.Classes)
            {
                AddFromCookies(cls.Cookies, ids);
                foreach (TsProperty prop in cls.Properties)
                    AddFromCookies(prop.Cookies, ids);
                foreach (TsMethod method in cls.Methods)
                {
                    AddFromCookies(method.Cookies, ids);
                    foreach (TsParameter param in method.Parameters)
                        AddFromCookies(param.Cookies, ids);
                }
            }

            foreach (TsInterface iface in file.Interfaces)
            {
                AddFromCookies(iface.Cookies, ids);
                foreach (TsProperty prop in iface.Properties)
                    AddFromCookies(prop.Cookies, ids);
                foreach (TsMethod method in iface.Methods)
                {
                    AddFromCookies(method.Cookies, ids);
                    foreach (TsParameter param in method.Parameters)
                        AddFromCookies(param.Cookies, ids);
                }
            }

            foreach (TsEnum en in file.Enums)
            {
                AddFromCookies(en.Cookies, ids);
                foreach (TsEnumMember member in en.Members)
                    AddFromCookies(member.Cookies, ids);
            }

            foreach (TsFunction func in file.Functions)
            {
                AddFromCookies(func.Cookies, ids);
                foreach (TsParameter param in func.Parameters)
                    AddFromCookies(param.Cookies, ids);
            }

            foreach (TsTypeAlias alias in file.TypeAliases)
                AddFromCookies(alias.Cookies, ids);
        }
    }

    private static void AddFromCookies(string[] cookies, SortedSet<string> ids)
    {
        foreach (string cookie in cookies)
        {
            if (cookie.StartsWith(NugetCookiePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string packageId = cookie[NugetCookiePrefix.Length..].Trim();
                if (packageId.Length > 0)
                    ids.Add(packageId);
            }
        }
    }

    // ── Prefix-table lookup ───────────────────────────────────────────────────

    private void CollectMappedTypePackages(SortedSet<string> ids)
    {
        foreach (string dotNetType in _npmPackageMap.GetAllDotNetTypes())
        {
            string? packageId = MapTypeToPackage(dotNetType);
            if (packageId is not null)
                ids.Add(packageId);
        }
    }

    private static string? MapTypeToPackage(string dotNetType)
    {
        // Walk the prefix table in declaration order; first match wins.
        foreach ((string prefix, string packageId) in s_prefixTable)
        {
            if (dotNetType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return packageId;
        }

        // No entry → part of the base runtime; no NuGet reference needed.
        return null;
    }
}
