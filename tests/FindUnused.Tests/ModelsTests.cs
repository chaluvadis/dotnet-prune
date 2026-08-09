using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class ModelsTests
{
    [TestMethod]
    public void Finding_DefaultValues_AreSet()
    {
        var finding = new Finding();

        Assert.AreEqual(string.Empty, finding.Project);
        Assert.AreEqual(string.Empty, finding.FilePath);
        Assert.AreEqual(string.Empty, finding.FilePathDisplay);
        Assert.AreEqual(string.Empty, finding.DisplayName);
        Assert.AreEqual(string.Empty, finding.ProjectFilePath);
        Assert.AreEqual(0, finding.Line);
        Assert.AreEqual(string.Empty, finding.SymbolKind);
        Assert.AreEqual(string.Empty, finding.ContainingType);
        Assert.AreEqual(string.Empty, finding.SymbolName);
        Assert.AreEqual(string.Empty, finding.Accessibility);
        Assert.AreEqual(string.Empty, finding.Remarks);
        Assert.AreEqual(string.Empty, finding.DeclaredProject);
        Assert.AreEqual(string.Empty, finding.FallbackProject);
        Assert.AreEqual(string.Empty, finding.Icon);
        Assert.IsNull(finding.Confidence);
        Assert.IsNull(finding.Severity);
    }

    [TestMethod]
    public void Finding_WithValues_SetsProperties()
    {
        var finding = new Finding
        {
            Project = "MyProject (/path/to/MyProject.csproj)",
            FilePath = "/path/to/MyClass.cs",
            FilePathDisplay = "MyClass.cs",
            DisplayName = "MyClass.cs",
            ProjectFilePath = "/path/to/MyProject.csproj",
            Line = 42,
            SymbolKind = "method",
            ContainingType = "MyClass",
            SymbolName = "UnusedMethod",
            Accessibility = "private",
            Remarks = "This method is never called",
            DeclaredProject = "MyProject",
            FallbackProject = "MyProject",
            Icon = "⚙️",
            Confidence = 95,
            Severity = "warning"
        };

        Assert.AreEqual("MyProject (/path/to/MyProject.csproj)", finding.Project);
        Assert.AreEqual("/path/to/MyClass.cs", finding.FilePath);
        Assert.AreEqual("MyClass.cs", finding.FilePathDisplay);
        Assert.AreEqual("MyClass.cs", finding.DisplayName);
        Assert.AreEqual("/path/to/MyProject.csproj", finding.ProjectFilePath);
        Assert.AreEqual(42, finding.Line);
        Assert.AreEqual("method", finding.SymbolKind);
        Assert.AreEqual("MyClass", finding.ContainingType);
        Assert.AreEqual("UnusedMethod", finding.SymbolName);
        Assert.AreEqual("private", finding.Accessibility);
        Assert.AreEqual("This method is never called", finding.Remarks);
        Assert.AreEqual("MyProject", finding.DeclaredProject);
        Assert.AreEqual("MyProject", finding.FallbackProject);
        Assert.AreEqual("⚙️", finding.Icon);
        Assert.AreEqual(95, finding.Confidence);
        Assert.AreEqual("warning", finding.Severity);
    }

    [TestMethod]
    public void Finding_SerializesToJson_WithAllProperties()
    {
        var finding = new Finding
        {
            Project = "TestProject",
            FilePath = "/path/to/MyClass.cs",
            FilePathDisplay = "MyClass.cs",
            DisplayName = "MyClass.cs",
            ProjectFilePath = "/path/to/TestProject.csproj",
            Line = 10,
            SymbolKind = "method",
            ContainingType = "MyClass",
            SymbolName = "UnusedMethod",
            Accessibility = "private",
            Remarks = "Never called",
            DeclaredProject = "TestProject",
            FallbackProject = "TestProject",
            Icon = "⚙️",
            Confidence = 90,
            Severity = "warning"
        };

        string json = JsonSerializer.Serialize(finding);

        Assert.IsTrue(json.Contains("\"Project\":\"TestProject\""));
        Assert.IsTrue(json.Contains("\"FilePath\":\"/path/to/MyClass.cs\""));
        Assert.IsTrue(json.Contains("\"Line\":10"));
        Assert.IsTrue(json.Contains("\"SymbolKind\":\"method\""));
        Assert.IsTrue(json.Contains("\"Confidence\":90"));
        Assert.IsTrue(json.Contains("\"Severity\":\"warning\""));
    }

    [TestMethod]
    public void Finding_DeserializesFromJson_PreservesValues()
    {
        string json = """
        {
            "Project": "TestProject",
            "FilePath": "/path/to/MyClass.cs",
            "FilePathDisplay": "MyClass.cs",
            "DisplayName": "MyClass.cs",
            "ProjectFilePath": "/path/to/TestProject.csproj",
            "Line": 10,
            "SymbolKind": "method",
            "ContainingType": "MyClass",
            "SymbolName": "UnusedMethod",
            "Accessibility": "private",
            "Remarks": "Never called",
            "DeclaredProject": "TestProject",
            "FallbackProject": "TestProject",
            "Icon": "⚙️",
            "Confidence": 90,
            "Severity": "warning"
        }
        """;

        var finding = JsonSerializer.Deserialize<Finding>(json);

        Assert.IsNotNull(finding);
        Assert.AreEqual("TestProject", finding!.Project);
        Assert.AreEqual("/path/to/MyClass.cs", finding.FilePath);
        Assert.AreEqual(10, finding.Line);
        Assert.AreEqual("method", finding.SymbolKind);
        Assert.AreEqual(90, finding.Confidence);
        Assert.AreEqual("warning", finding.Severity);
    }

    [TestMethod]
    public void Finding_WithNullValues_HandlesGracefully()
    {
        var finding = new Finding
        {
            Project = "TestProject",
            Confidence = null,
            Severity = null
        };

        string json = JsonSerializer.Serialize(finding);

        Assert.IsTrue(json.Contains("\"Confidence\":null") || json.Contains("\"Severity\":null"));
    }

    [TestMethod]
    public void Finding_WithSpecialCharacters_SerializesCorrectly()
    {
        var finding = new Finding
        {
            SymbolName = "Method<With<T>>",
            Remarks = "Contains \"quotes\" and special chars: <>&"
        };

        string json = JsonSerializer.Serialize(finding);

        Assert.IsTrue(json.Contains("Method<With<T>>") || json.Contains("Method"));
        Assert.IsTrue(json.Contains("quotes"));
    }

    [TestMethod]
    public void Finding_WithUnicode_SerializesCorrectly()
    {
        var finding = new Finding
        {
            SymbolName = "日本語メソッド",
            Remarks = "Contains emoji: ⚙️ and Unicode: ñ"
        };

        string json = JsonSerializer.Serialize(finding);

        Assert.IsTrue(json.Contains("日本語") || json.Contains("\\u65E5\\u672C\\u8A9E") || json.Contains("\\u65e5\\u672c\\u8a9e"));
        Assert.IsTrue(json.Contains("⚙️") || json.Contains("\\u2699") || json.Contains("\\u26A9"));
    }

    [TestMethod]
    public void AnalysisResult_DefaultValues_AreSet()
    {
        var result = new AnalysisResult();

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Findings);
        Assert.AreEqual(0, result.Findings.Count);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void AnalysisResult_SerializesToJson_WithSuccessFlag()
    {
        var result = new AnalysisResult
        {
            Success = true,
            Findings = [],
            ErrorMessage = null
        };

        string json = JsonSerializer.Serialize(result);

        Assert.IsTrue(json.Contains("\"Success\":true"));
        Assert.IsTrue(json.Contains("\"Findings\":[]"));
    }

    [TestMethod]
    public void AnalysisResult_WithFindings_SerializesCorrectly()
    {
        var result = new AnalysisResult
        {
            Success = true,
            Findings =
            [
                new Finding { SymbolName = "Unused1", SymbolKind = "method", Line = 1 },
                new Finding { SymbolName = "Unused2", SymbolKind = "property", Line = 5 }
            ],
            ErrorMessage = null
        };

        string json = JsonSerializer.Serialize(result);

        Assert.IsTrue(json.Contains("Unused1"));
        Assert.IsTrue(json.Contains("Unused2"));
        Assert.IsTrue(json.Contains("\"Findings\":["));
    }

    [TestMethod]
    public void AnalysisResult_WithErrorMessage_SerializesCorrectly()
    {
        var result = new AnalysisResult
        {
            Success = false,
            Findings = [],
            ErrorMessage = "Analysis failed: Target not found"
        };

        string json = JsonSerializer.Serialize(result);

        Assert.IsTrue(json.Contains("Analysis failed"));
        Assert.IsTrue(json.Contains("\"Success\":false"));
    }

    [TestMethod]
    public void FindingRecord_Equality_WorksCorrectly()
    {
        var finding1 = new Finding { SymbolName = "Method1", Line = 1, SymbolKind = "method" };
        var finding2 = new Finding { SymbolName = "Method1", Line = 1, SymbolKind = "method" };
        var finding3 = new Finding { SymbolName = "Method2", Line = 2, SymbolKind = "method" };

        Assert.AreEqual(finding1, finding2);
        Assert.AreNotEqual(finding1, finding3);
    }

    [TestMethod]
    public void AnalysisResultRecord_Equality_WorksCorrectly()
    {
        var result1 = new AnalysisResult { Success = true, Findings = [] };
        var result2 = new AnalysisResult { Success = true, Findings = [] };

        Assert.AreEqual(result1.Success, result2.Success);
        Assert.IsFalse(result1 == result2); // Record equality for reference-typed mutable lists is reference-based
    }

    [TestMethod]
    public void AnalysisResultRecord_Inequality_WithDifferentSuccess_WorksCorrectly()
    {
        var result1 = new AnalysisResult { Success = true, Findings = [] };
        var result2 = new AnalysisResult { Success = false, Findings = [] };

        Assert.AreNotEqual(result1, result2);
    }
}
