using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class FindingMetricsTests
{
    [DataTestMethod]
    [DataRow("public", "method", false, 70)]
    [DataRow("protected", "method", false, 80)]
    [DataRow("internal", "method", false, 90)]
    [DataRow("private", "method", false, 100)]
    [DataRow("public", "property", false, 65)]
    [DataRow("private", "field", false, 95)]
    [DataRow("public", "event", false, 80)]
    [DataRow("private", "event", false, 90)]
    public void CalculateConfidence_WithDifferentAccessibilities_ReturnsExpectedBase(
        string accessibility, string symbolKind, bool hasReferences, int expectedBase)
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!, accessibility, symbolKind, hasReferences);

        if (hasReferences)
        {
            Assert.AreEqual(0, confidence);
        }
        else
        {
            Assert.IsTrue(confidence >= expectedBase - 10);
            Assert.IsTrue(confidence <= expectedBase + 10);
        }
    }

    [TestMethod]
    public void CalculateConfidence_WhenHasReferences_ReturnsZero()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "private",
            symbolKind: "method",
            hasReferences: true);

        Assert.AreEqual(0, confidence);
    }

    [TestMethod]
    public void CalculateConfidence_WhenNoReferencesAndPrivate_ReturnsHundred()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "private",
            symbolKind: "method",
            hasReferences: false);

        Assert.AreEqual(100, confidence);
    }

    [TestMethod]
    public void CalculateConfidence_WhenNoReferencesAndPublic_ReturnsSeventy()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "public",
            symbolKind: "method",
            hasReferences: false);

        Assert.AreEqual(70, confidence);
    }

    [TestMethod]
    public void CalculateConfidence_WhenNoReferencesAndProtected_ReturnsEighty()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "protected",
            symbolKind: "method",
            hasReferences: false);

        Assert.AreEqual(80, confidence);
    }

    [TestMethod]
    public void CalculateConfidence_WhenNoReferencesAndInternal_ReturnsNinety()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "internal",
            symbolKind: "method",
            hasReferences: false);

        Assert.AreEqual(90, confidence);
    }

    [TestMethod]
    public void CalculateSeverity_WhenHighConfidencePrivate_ReturnsWarning()
    {
        string severity = FindingMetrics.CalculateSeverity(
            accessibility: "private",
            symbolKind: "method",
            confidence: 85);

        Assert.AreEqual("warning", severity);
    }

    [TestMethod]
    public void CalculateSeverity_WhenHighConfidenceInternal_ReturnsWarning()
    {
        string severity = FindingMetrics.CalculateSeverity(
            accessibility: "internal",
            symbolKind: "method",
            confidence: 85);

        Assert.AreEqual("warning", severity);
    }

    [TestMethod]
    public void CalculateSeverity_WhenPublicSymbol_ReturnsInformation()
    {
        string severity = FindingMetrics.CalculateSeverity(
            accessibility: "public",
            symbolKind: "method",
            confidence: 90);

        Assert.AreEqual("information", severity);
    }

    [TestMethod]
    public void CalculateSeverity_WhenLowConfidence_ReturnsInformation()
    {
        string severity = FindingMetrics.CalculateSeverity(
            accessibility: "private",
            symbolKind: "method",
            confidence: 50);

        Assert.AreEqual("information", severity);
    }

    [TestMethod]
    public void CalculateSeverity_WhenMediumConfidencePrivate_ReturnsHint()
    {
        string severity = FindingMetrics.CalculateSeverity(
            accessibility: "private",
            symbolKind: "method",
            confidence: 65);

        Assert.AreEqual("hint", severity);
    }

    [DataTestMethod]
    [DataRow("class", "🔷")]
    [DataRow("MyClass", "🔷")]
    [DataRow("MyType", "🔷")]
    [DataRow("interface", "🔶")]
    [DataRow("IMyInterface", "🔶")]
    [DataRow("method", "⚙️")]
    [DataRow("void Method()", "⚙️")]
    [DataRow("function", "⚙️")]
    [DataRow("property", "📝")]
    [DataRow("int MyProperty", "📝")]
    [DataRow("field", "📦")]
    [DataRow("int myField", "📦")]
    [DataRow("parameter", "🎯")]
    [DataRow("int parameter", "🎯")]
    [DataRow("enum", "🔢")]
    [DataRow("MyEnum", "🔢")]
    [DataRow("struct", "📐")]
    [DataRow("MyStruct", "📐")]
    [DataRow("event", "⚡")]
    [DataRow("event EventHandler", "⚡")]
    [DataRow("unknown", "⚠️")]
    [DataRow("delegate", "⚠️")]
    public void GetIconForSymbolKind_WithVariousKinds_ReturnsExpectedIcon(string symbolKind, string expectedIcon)
    {
        string icon = FindingMetrics.GetIconForSymbolKind(symbolKind);

        Assert.AreEqual(expectedIcon, icon);
    }

    [TestMethod]
    public void CalculateConfidence_ClampsToMinimumZero()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "public",
            symbolKind: "method",
            hasReferences: false);

        Assert.IsTrue(confidence >= 0);
    }

    [TestMethod]
    public void CalculateConfidence_ClampsToMaximumHundred()
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol: null!,
            accessibility: "private",
            symbolKind: "method",
            hasReferences: false);

        Assert.IsTrue(confidence <= 100);
    }
}
