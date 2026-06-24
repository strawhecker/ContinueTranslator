using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ContinueTranslator.Core.IR;

namespace ContinueTranslator.Core.Parsing;

/// <summary>
/// Extracts <c>parse.mjs</c> from the embedded resources, installs its npm dependency,
/// spawns <c>node parse.mjs</c> with the supplied TypeScript file paths, and deserializes
/// the JSON output to <see cref="TsFile"/> IR records.
/// </summary>
internal sealed partial class TsParser
{
    private const string ResourceName = "ContinueTranslator.Core.Parsing.parse.mjs";
    private const string ScriptName = "parse.mjs";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parses the given TypeScript files and returns the IR representation for each.
    /// </summary>
    /// <param name="tsFilePaths">Absolute paths to the <c>.ts</c> files to parse.</param>
    /// <returns>One <see cref="TsFile"/> per input path.</returns>
    public TsFile[] Parse(IReadOnlyList<string> tsFilePaths)
    {
        ArgumentNullException.ThrowIfNull(tsFilePaths);

        string tempDir = Path.Combine(Path.GetTempPath(), $"TsParser_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ExtractScript(tempDir);
            RunNpmInstall(tempDir);
            string json = RunNodeParse(tempDir, tsFilePaths);
            return DeserializeResult(json);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void ExtractScript(string tempDir)
    {
        using Stream? resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found in the assembly.");

        string destination = Path.Combine(tempDir, ScriptName);
        using FileStream fs = File.Create(destination);
        resource.CopyTo(fs);
    }

    private static void RunNpmInstall(string tempDir)
    {
        // npm is a batch script on Windows; route it through cmd.exe.
        var psi = new ProcessStartInfo("cmd.exe", "/c npm install ts-morph --no-fund --no-audit")
        {
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the npm process.");

        // Drain both pipes concurrently to prevent buffer deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"npm install failed (exit code {process.ExitCode}): {stderr.Trim()}");
        }
    }

    private static string RunNodeParse(string tempDir, IReadOnlyList<string> tsFilePaths)
    {
        string scriptPath = Path.Combine(tempDir, ScriptName);

        // Write paths to a JSON file so the command line stays short.
        // On Windows, CreateProcess has a 32,767-character limit that is easily
        // exceeded when many long absolute paths are passed as CLI arguments.
        string pathsFile = Path.Combine(tempDir, "paths.json");
        File.WriteAllText(pathsFile, JsonSerializer.Serialize(tsFilePaths));

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add($"--paths-file={pathsFile}");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the node process.");

        // Drain stdout and stderr concurrently to prevent buffer deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"node parse.mjs failed (exit code {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout;
    }

    private static TsFile[] DeserializeResult(string json)
    {
        return JsonSerializer.Deserialize<TsFile[]>(json, JsonOptions)
            ?? throw new InvalidOperationException("Deserialization of parse.mjs output returned null.");
    }
}

