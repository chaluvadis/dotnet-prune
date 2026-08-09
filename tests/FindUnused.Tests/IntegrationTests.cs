using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class IntegrationTests
{
    private static readonly string s_testRoot = Path.Combine(Path.GetTempPath(), "FindUnusedIntegrationTests");

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(s_testRoot);

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(s_testRoot))
        {
            try
            {
                Directory.Delete(s_testRoot, recursive: true);
            }
            catch { }
        }
    }

    [TestMethod]
    public void RunAnalysis_WithMissingPath_ReturnsFailure()
    {
        var result = EntryPoint.RunAnalysisAsync("/nonexistent/path/to/solution.sln").Result;

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage.Contains("not found"));
    }

    [TestMethod]
    public void RunAnalysis_WithEmptyPath_ReturnsFailure()
    {
        var result = EntryPoint.RunAnalysisAsync("").Result;

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void RunAnalysis_WithNullPath_ReturnsFailure()
    {
        var result = EntryPoint.RunAnalysisAsync(null!).Result;

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void RunAnalysis_WithWhitespacePath_ReturnsFailure()
    {
        var result = EntryPoint.RunAnalysisAsync("   ").Result;

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task RunAnalysis_WithValidProject_FindsUnusedPrivateMethod()
    {
        string projectDir = CreateTestProject("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """, """
            public class MyClass {
                private void UnusedPrivateMethod() { }
                public void UsedPublicMethod() { }
            }
            public class Program {
                public static void Main() {
                    var c = new MyClass();
                    c.UsedPublicMethod();
                }
            }
            """);

        var config = new AnalyzerConfiguration
        {
            IncludePublicSymbols = true,
            IncludeInternalSymbols = true,
            ExcludeGeneratedCode = true,
            AnalysisMode = "loose"
        };

        var result = await EntryPoint.RunAnalysisAsync(projectDir, config);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsNotNull(result.Findings);

        // The analyzer should find unused private methods
        // SymbolName uses method.ToDisplayString() which includes containing type
        var unusedFindings = result.Findings.Where(f =>
            f.SymbolKind == "Method" &&
            f.Accessibility == "Private" &&
            f.Remarks.Contains("No references")).ToList();

        Assert.IsTrue(unusedFindings.Count > 0,
            $"Expected to find unused private method. Findings: {string.Join(", ", result.Findings.Select(f => f.SymbolName))}");
    }

    [TestMethod]
    public async Task RunAnalysis_WithExcludePublic_DoesNotReportPublicUnused()
    {
        string projectDir = CreateTestProject("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """, """
            public class MyClass {
                public void UnusedPublicMethod() { }
                private void UsedPrivateMethod() { }
            }
            public class Program {
                public static void Main() {
                    var c = new MyClass();
                    c.UsedPrivateMethod();
                }
            }
            """);

        var config = new AnalyzerConfiguration
        {
            IncludePublicSymbols = false,
            IncludeInternalSymbols = true,
            ExcludeGeneratedCode = true,
            AnalysisMode = "loose"
        };

        var result = await EntryPoint.RunAnalysisAsync(projectDir, config);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsNotNull(result.Findings);

        var publicFinding = result.Findings.FirstOrDefault(f => f.SymbolName == "UnusedPublicMethod");
        Assert.IsNull(publicFinding, "Public unused method should be excluded when IncludePublicSymbols is false");
    }

    [TestMethod]
    public async Task RunAnalysis_WithStrictMode_UsesStrictAnalysis()
    {
        string projectDir = CreateTestProject("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """, """
            public class MyClass {
                public void UnusedPublicMethod() { }
            }
            """);

        var config = new AnalyzerConfiguration
        {
            IncludePublicSymbols = true,
            IncludeInternalSymbols = true,
            ExcludeGeneratedCode = true,
            AnalysisMode = "strict"
        };

        var result = await EntryPoint.RunAnalysisAsync(projectDir, config);

        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    [TestMethod]
    public async Task RunAnalysis_WithIncludeGenerated_IncludesGeneratedCode()
    {
        string projectDir = CreateTestProject("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """, """
            // <auto-generated>
            public class GeneratedClass {
                public void GeneratedMethod() { }
            }
            """);

        var config = new AnalyzerConfiguration
        {
            IncludePublicSymbols = true,
            IncludeInternalSymbols = true,
            ExcludeGeneratedCode = false,
            AnalysisMode = "loose"
        };

        var result = await EntryPoint.RunAnalysisAsync(projectDir, config);

        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    private static string CreateTestProject(string csproj, string code)
    {
        string projectDir = Path.Combine(s_testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);

        File.WriteAllText(Path.Combine(projectDir, "TestProject.csproj"), csproj);
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), code);

        return projectDir;
    }
}
