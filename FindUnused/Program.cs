namespace FindUnused;

public static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var config = new AnalyzerConfiguration();
            string? targetPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
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
                            // We don't have a MaxFindings property in AnalyzerConfiguration,
                            // but the extension passes this. We'll ignore it for now since
                            // the trimming happens on the extension side.
                        }
                        i++;
                        break;
                    default:
                        if (!args[i].StartsWith("--"))
                        {
                            targetPath = args[i];
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                Console.Error.WriteLine("Usage: FindUnused <targetPath>");
                Console.Error.WriteLine("Options:");
                Console.Error.WriteLine("  --exclude-public       Exclude public symbols from analysis");
                Console.Error.WriteLine("  --exclude-internal     Exclude internal symbols from analysis");
                Console.Error.WriteLine("  --include-generated    Include generated code in analysis");
                Console.Error.WriteLine("  --strict               Use strict analysis mode");
                Console.Error.WriteLine("  --max-findings <num>   Maximum number of findings to return");
                Environment.Exit(1);
                return;
            }

            var result = await EntryPoint.RunAnalysisAsync(targetPath, config);
            if (!result.Success)
            {
                Console.Error.WriteLine($"Analysis failed: {result.ErrorMessage}");
                Environment.Exit(1);
            }
            else
            {
                // Stream findings as JSON to stdout using Utf8JsonWriter
                // This avoids allocating a large intermediate string
                await using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
                writer.WriteStartArray();
                foreach (var finding in result.Findings)
                {
                    JsonSerializer.Serialize(writer, finding);
                }
                writer.WriteEndArray();
                await writer.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
