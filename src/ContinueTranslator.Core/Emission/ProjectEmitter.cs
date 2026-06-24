using System.Xml.Linq;
using ContinueTranslator.Core.IR;

namespace ContinueTranslator.Core.Emission;

/// <summary>
/// Emits a SDK-style <c>ContinueCore.csproj</c> file with NuGet package references
/// discovered by <see cref="NuGetPackageResolver"/>.
/// </summary>
internal sealed partial class ProjectEmitter
{
    private readonly NuGetPackageResolver _resolver;

    /// <param name="resolver">Resolves the NuGet package IDs required by the translated files.</param>
    public ProjectEmitter(NuGetPackageResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Generates <c>ContinueCore.csproj</c> for the given IR files.
    /// </summary>
    /// <param name="files">Mapped IR nodes (MappingEngine must have already run).</param>
    /// <param name="targetFramework">TFM string, e.g. <c>net10.0</c>.</param>
    /// <returns>
    /// A single <see cref="EmittedFile"/> with <c>RelativePath = "ContinueCore.csproj"</c>
    /// and <c>Content</c> set to the formatted XML.
    /// </returns>
    public EmittedFile Emit(TsFile[] files, string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);

        IReadOnlySet<string> packageIds = _resolver.Resolve(files);

        XElement propertyGroup = new("PropertyGroup",
            new XElement("OutputType", "Library"),
            new XElement("TargetFramework", targetFramework));

        XElement project = new(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            propertyGroup);

        if (packageIds.Count > 0)
        {
            XElement itemGroup = new("ItemGroup",
                packageIds.Select(id =>
                    new XElement("PackageReference",
                        new XAttribute("Include", id),
                        new XAttribute("Version", "FIXME"))));

            project.Add(itemGroup);
        }

        XDocument doc = new(project);

        // SaveOptions.OmitDuplicateNamespaces keeps the output clean; we want
        // the default indentation (2-space) from XDocument.ToString().
        string xml = doc.ToString(SaveOptions.None);

        return new EmittedFile("ContinueCore.csproj", xml);
    }
}

