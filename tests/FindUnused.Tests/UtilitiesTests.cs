using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class UtilitiesTests
{
    [TestMethod]
    public void IsPathExcluded_WhenNull_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded(null);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenEmpty_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded(string.Empty);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenWhitespace_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded("   ");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNoSeparators_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded("MyClass.cs");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNormalSourcePath_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded("/project/src/MyClass.cs");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenBinPath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/bin/Debug/net6.0/MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenObjPath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/obj/Debug/net6.0/MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNuGetPackagesPath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/home/.nuget/packages/newtonsoft.json/13.0.1/lib/net6.0/Newtonsoft.Json.dll");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNuGetPath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/.nuget/packages/MyPackage/1.0.0/lib/net6.0/MyPackage.dll");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenPackagesPath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/packages/xunit/2.4.1/xunit.dll");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenDebugPath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/src/debug/MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenReleasePath_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/src/release/MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenBackslashSeparators_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded(@"C:\project\bin\Debug\MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenMixedSeparators_ReturnsTrue()
    {
        bool result = Utilities.IsPathExcluded("/project/bin\\Debug/MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_IsCaseInsensitive()
    {
        bool result = Utilities.IsPathExcluded("/project/BIN/Debug/MyClass.cs");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenNuGetInFilenameButNotPath_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded("/project/src/MyNuGetClass.cs");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPathExcluded_WhenBinInFilenameButNotPath_ReturnsFalse()
    {
        bool result = Utilities.IsPathExcluded("/project/src/BinaryClass.cs");

        Assert.IsFalse(result);
    }
}
