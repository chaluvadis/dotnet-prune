namespace FindUnused;

public static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: FindUnused <targetPath>");
                Environment.Exit(1);
                return;
            }
            string targetPath = args[0];
            var result = await EntryPoint.RunAnalysisAsync(targetPath, new AnalyzerConfiguration());
            if (!result.Success)
            {
                Console.Error.WriteLine($"Analysis failed: {result.ErrorMessage}");
                Environment.Exit(1);
            }
            else
            {
                // Output findings as JSON to stdout for the extension to parse
                Console.WriteLine(JsonSerializer.Serialize(result.Findings));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
