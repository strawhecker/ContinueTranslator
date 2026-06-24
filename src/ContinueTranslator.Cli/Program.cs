string? repo = null, tag = null, outDir = null;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--repo") repo = args[++i];
    else if (args[i] == "--tag") tag = args[++i];
    else if (args[i] == "--out") outDir = args[++i];
}

if (string.IsNullOrWhiteSpace(repo) ||
    string.IsNullOrWhiteSpace(tag) ||
    string.IsNullOrWhiteSpace(outDir))
{
    Console.Error.WriteLine("Usage: ContinueTranslator.Cli --repo <path> --tag <tag> --out <directory>");
    Console.Error.WriteLine("  --repo   Path to local clone of forked Continue repository (required)");
    Console.Error.WriteLine("  --tag    Git tag or branch to check out before scanning (required)");
    Console.Error.WriteLine("  --out    Output directory for generated C# files (required)");
    return 1;
}

var options = new ContinueTranslator.Cli.TranslationOptions(repo, tag, outDir);

try
{
    new ContinueTranslator.Cli.PipelineRunner().Run(options);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

return 0;
