using System.Diagnostics;

namespace ContinueTranslator.Core.Acquisition;

internal sealed partial class SourceFetcher
{
    /// <summary>
    /// Checks out <paramref name="tagOrBranch"/> in the local clone, then returns all
    /// non-test TypeScript source paths after applying the standard exclusion filters.
    /// </summary>
    /// <param name="repoPath">Absolute path to the local fork clone.</param>
    /// <param name="tagOrBranch">Git tag or branch name to check out before scanning.</param>
    /// <returns>Absolute paths of the surviving <c>.ts</c> files.</returns>
    public IReadOnlyList<string> FetchSourceFiles(string repoPath, string tagOrBranch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagOrBranch);

        RunGitCheckout(repoPath, tagOrBranch);
        return CollectSourceFiles(repoPath);
    }

    private static void RunGitCheckout(string repoPath, string tagOrBranch)
    {
        var psi = new ProcessStartInfo("git", $"checkout {tagOrBranch}")
        {
            WorkingDirectory = repoPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the git process.");

        // Read stderr before WaitForExit to avoid buffer deadlock.
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git checkout '{tagOrBranch}' failed in '{repoPath}' " +
                $"(exit code {process.ExitCode}): {stderr.Trim()}");
        }
    }

    private static IReadOnlyList<string> CollectSourceFiles(string repoPath)
    {
        var results = new List<string>();

        foreach (string file in Directory.EnumerateFiles(repoPath, "*.ts", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, repoPath))
                continue;

            results.Add(file);
        }

        return results.AsReadOnly();
    }

    private static bool IsExcluded(string absolutePath, string repoPath)
    {
        // Filter: *.test.ts
        if (Path.GetFileName(absolutePath).EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase))
            return true;

        // Normalise to forward-slash for consistent prefix matching on all OSes.
        string relative = Path.GetRelativePath(repoPath, absolutePath).Replace('\\', '/');

        // Filter: extensions/vscode/** and gui/**
        return relative.StartsWith("extensions/vscode/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("gui/", StringComparison.OrdinalIgnoreCase);
    }
}

