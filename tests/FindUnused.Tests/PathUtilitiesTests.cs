using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class PathUtilitiesTests
{
    private static readonly IReadOnlySet<string> s_exclusionPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/.nuget/packages/", "/.nuget/", "/packages/", "/bin/", "/obj/", "/debug/", "/release/"
    };

    [TestMethod]
    public void IsPathExcluded_WhenNull_ReturnsFalse()
    {
        bool result = PathUtilities.IsPathExcluded(null, s_exclusionPatterns);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenEmpty_ReturnsFalse()
    {
        bool result = PathUtilities.IsPathExcluded(string.Empty, s_exclusionPatterns);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenWhitespace_ReturnsFalse()
    {
        bool result = PathUtilities.IsPathExcluded("   ", s_exclusionPatterns);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenBackslashBinPath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded(@"C:\project\bin\Debug\net6.0\MyClass.cs", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenForwardSlashBinPath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded("/project/bin/Debug/net6.0/MyClass.cs", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenObjPath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded("/project/obj/Debug/net6.0/MyClass.cs", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNuGetPath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded("/home/.nuget/packages/newtonsoft.json/13.0.1/lib/net6.0/Newtonsoft.Json.dll", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenDebugPath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded("/project/src/debug/MyClass.cs", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenReleasePath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded("/project/src/release/MyClass.cs", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenPackagesPath_ReturnsTrue()
    {
        bool result = PathUtilities.IsPathExcluded("/project/packages/xunit/2.4.1/xunit.dll", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNormalSourcePath_ReturnsFalse()
    {
        bool result = PathUtilities.IsPathExcluded("/project/src/MyClass.cs", s_exclusionPatterns);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNuGetInFilenameButNotPath_ReturnsFalse()
    {
        bool result = PathUtilities.IsPathExcluded("/project/src/MyNuGetClass.cs", s_exclusionPatterns);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_IsCaseInsensitive()
    {
        bool result = PathUtilities.IsPathExcluded("/project/BIN/Debug/MyClass.cs", s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void SourceTreeIsExcluded_WhenTreePathIsExcluded_ReturnsTrue()
    {
        bool result = PathUtilities.SourceTreeIsExcluded(
            CreateSyntaxTree("/project/bin/Debug/MyClass.cs"),
            s_exclusionPatterns);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void SourceTreeIsExcluded_WhenTreePathIsNotExcluded_ReturnsFalse()
    {
        bool result = PathUtilities.SourceTreeIsExcluded(
            CreateSyntaxTree("/project/src/MyClass.cs"),
            s_exclusionPatterns);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetDisplayPathForDocument_WithNullDocAndTree_ReturnsGenerated()
    {
        string result = PathUtilities.GetDisplayPathForDocument(null, null, null, null);

        Assert.AreEqual("(generated)", result);
    }

    [TestMethod]
    public void GetDisplayPathForDocument_WithTreeFilePath_ReturnsFileName()
    {
        string result = PathUtilities.GetDisplayPathForDocument(
            null,
            CreateSyntaxTree("/project/src/MyClass.cs"),
            null, null);

        Assert.AreEqual("MyClass.cs", result);
    }

    [TestMethod]
    public void GetDisplayNameForDocument_WithNulls_ReturnsGenerated()
    {
        string result = PathUtilities.GetDisplayNameForDocument(null, null);

        Assert.AreEqual("(generated)", result);
    }

    [TestMethod]
    public void GetDisplayNameForDocument_WithTreeFilePath_ReturnsFileName()
    {
        string result = PathUtilities.GetDisplayNameForDocument(
            null,
            CreateSyntaxTree("/project/src/MyClass.cs"));

        Assert.AreEqual("MyClass.cs", result);
    }

    [TestMethod]
    public void GetFullPathForDocument_WithTreeFilePath_ReturnsFullPath()
    {
        string? result = PathUtilities.GetFullPathForDocument(
            null,
            CreateSyntaxTree("/project/src/MyClass.cs"));

        Assert.IsNotNull(result);
        Assert.IsTrue(Path.IsPathRooted(result!));
    }

    [TestMethod]
    public void GetFullPathForDocument_WithNulls_ReturnsNull()
    {
        string? result = PathUtilities.GetFullPathForDocument(null, null);

        Assert.IsNull(result);
    }

    private static Microsoft.CodeAnalysis.SyntaxTree CreateSyntaxTree(string filePath)
    {
        var sourceCode = "class MyClass { }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode, new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions());
        return tree.WithFilePath(filePath);
    }

    private static Microsoft.CodeAnalysis.Document CreateDocument(string filePath)
    {
        var project = CreateProject();
        var sourceCode = "class MyClass { }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode);
        var treeWithPath = tree.WithFilePath(filePath);
        return project.AddDocument("MyClass.cs", treeWithPath.GetRoot());
    }

    private static Microsoft.CodeAnalysis.Project CreateProject()
    {
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var projectInfo = Microsoft.CodeAnalysis.ProjectInfo.Create(
            Microsoft.CodeAnalysis.ProjectId.CreateNewId(),
            Microsoft.CodeAnalysis.VersionStamp.Default,
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp);
        return workspace.AddProject(projectInfo);
    }
}
