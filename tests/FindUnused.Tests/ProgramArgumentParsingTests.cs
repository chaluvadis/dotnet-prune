using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class ProgramArgumentParsingTests
{
    [TestMethod]
    public void ParseArguments_WhenNoArgs_ReturnsNullTargetAndDefaultConfig()
    {
        var (targetPath, config) = Program.ParseArguments([]);

        Assert.IsNull(targetPath);
        Assert.IsTrue(config.IncludePublicSymbols);
        Assert.IsTrue(config.IncludeInternalSymbols);
        Assert.IsTrue(config.ExcludeGeneratedCode);
        Assert.AreEqual("loose", config.AnalysisMode);
    }

    [TestMethod]
    public void ParseArguments_WhenTargetProvided_ReturnsTargetPath()
    {
        var (targetPath, config) = Program.ParseArguments(["--target", "/path/to/solution.sln"]);

        Assert.AreEqual("/path/to/solution.sln", targetPath);
    }

    [TestMethod]
    public void ParseArguments_WhenTargetMissingValue_IgnoresFlag()
    {
        var (targetPath, _) = Program.ParseArguments(["--target"]);

        Assert.IsNull(targetPath);
    }

    [TestMethod]
    public void ParseArguments_WhenExcludePublic_DisablesPublicSymbols()
    {
        var (_, config) = Program.ParseArguments(["--exclude-public"]);

        Assert.IsFalse(config.IncludePublicSymbols);
    }

    [TestMethod]
    public void ParseArguments_WhenExcludeInternal_DisablesInternalSymbols()
    {
        var (_, config) = Program.ParseArguments(["--exclude-internal"]);

        Assert.IsFalse(config.IncludeInternalSymbols);
    }

    [TestMethod]
    public void ParseArguments_WhenIncludeGenerated_EnablesGeneratedCode()
    {
        var (_, config) = Program.ParseArguments(["--include-generated"]);

        Assert.IsFalse(config.ExcludeGeneratedCode);
    }

    [TestMethod]
    public void ParseArguments_WhenStrict_SetsStrictMode()
    {
        var (_, config) = Program.ParseArguments(["--strict"]);

        Assert.AreEqual("strict", config.AnalysisMode);
    }

    [TestMethod]
    public void ParseArguments_WhenMaxFindingsProvided_ParsesNumber()
    {
        var (_, config) = Program.ParseArguments(["--max-findings", "50"]);

        // MaxFindings is currently ignored, but parsing should not throw
        Assert.AreEqual("loose", config.AnalysisMode);
    }

    [TestMethod]
    public void ParseArguments_WithMultipleFlags_CombinesAll()
    {
        var (targetPath, config) = Program.ParseArguments([
            "--target", "/path/to/project.csproj",
            "--exclude-public",
            "--exclude-internal",
            "--strict"
        ]);

        Assert.AreEqual("/path/to/project.csproj", targetPath);
        Assert.IsFalse(config.IncludePublicSymbols);
        Assert.IsFalse(config.IncludeInternalSymbols);
        Assert.IsTrue(config.ExcludeGeneratedCode);
        Assert.AreEqual("strict", config.AnalysisMode);
    }

    [TestMethod]
    public void ParseArguments_WithMaxFindingsMissingValue_IgnoresFlag()
    {
        var (_, config) = Program.ParseArguments(["--max-findings"]);

        Assert.AreEqual("loose", config.AnalysisMode);
    }
}
