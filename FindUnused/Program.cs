namespace FindUnused;

public static class Program
{
    public static async Task Main(string[] args)
    {
        int exitCode = await RunAsync(args);
        Environment.Exit(exitCode);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var (targetPath, config) = ParseArguments(args);

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                Console.Error.WriteLine("Usage: FindUnused --target <path>");
                Console.Error.WriteLine("Options:");
                Console.Error.WriteLine("  --target <path>         Solution/project path to analyze");
                Console.Error.WriteLine("  --exclude-public       Exclude public symbols from analysis");
                Console.Error.WriteLine("  --exclude-internal     Exclude internal symbols from analysis");
                Console.Error.WriteLine("  --include-generated    Include generated code in analysis");
                Console.Error.WriteLine("  --strict               Use strict analysis mode");
                Console.Error.WriteLine("  --max-findings <num>   Maximum number of findings to return");
                return 1;
            }

            var result = await EntryPoint.RunAnalysisAsync(targetPath, config);
            if (!result.Success)
            {
                Console.Error.WriteLine($"Analysis failed: {result.ErrorMessage}");
                return 1;
            }
            else
            {
                await using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
                writer.WriteStartArray();
                foreach (var finding in result.Findings)
                {
                    JsonSerializer.Serialize(writer, finding);
                }
                writer.WriteEndArray();
                await writer.FlushAsync();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Parse command-line arguments into target path and configuration.
    /// </summary>
    public static (string? targetPath, AnalyzerConfiguration config) ParseArguments(string[] args)
    {
        var config = new AnalyzerConfiguration();
        string? targetPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target":
                    if (i + 1 < args.Length)
                    {
                        targetPath = args[i + 1];
                        i++;
                    }
                    break;
                case "--exclude-public":
                    config.IncludePublicSymbols = false;
                    break;
                case "--exclude-internal":
                    config.IncludeInternalSymbols = false;
                    break;
                case "--include-generated":
                    config.ExcludeGeneratedCode = false;
                    break;
                case "--strict":
                    config.AnalysisMode = "strict";
                    break;
                case "--max-findings":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var max))
                    {
                        // Max findings trimming handled on extension side.
                    }
                    i++;
                    break;
            }
        }

        return (targetPath, config);
    }
}
