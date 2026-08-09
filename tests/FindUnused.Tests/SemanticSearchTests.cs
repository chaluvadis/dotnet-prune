using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using FindUnused;

namespace FindUnused.Tests;

[TestClass]
public class SemanticSearchTests
{
    private const string DefaultMetadata = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text;
        using System.Threading.Tasks;
        """;

    [TestMethod]
    public async Task IsSymbolUsedInNode_WhenNodeIsNotReference_ReturnsFalse()
    {
        var (symbol, node, model) = await CreateSymbolAndNodeAsync("""
            public class MyClass { }
            public class OtherClass { }
            """, "MyClass", "ClassDeclarationSyntax");

        bool result = await SemanticSearch.IsSymbolUsedInNode(symbol, node, model);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task IsSymbolUsedInNode_TypeGetType_WithMatchingString_ReturnsTrue()
    {
        var (typeSymbol, literalNode, model) = await CreateTypeAndLiteralNodeAsync("""
            using System;
            public class MyClass { }
            public class Consumer {
                public void Method() {
                    var t = Type.GetType("MyClass");
                }
            }
            """, "MyClass");

        bool result = await SemanticSearch.IsSymbolUsedInNode(typeSymbol, literalNode, model);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task IsSymbolUsedInNode_AssemblyGetType_WithMatchingString_ReturnsTrue()
    {
        var (typeSymbol, literalNode, model) = await CreateTypeAndLiteralNodeAsync("""
            using System.Reflection;
            public class MyClass { }
            public class Consumer {
                public void Method() {
                    var t = typeof(Consumer).Assembly.GetType("MyClass");
                }
            }
            """, "MyClass");

        bool result = await SemanticSearch.IsSymbolUsedInNode(typeSymbol, literalNode, model);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task ManualSemanticSearchAsync_WhenSymbolIsNotUsed_ReturnsFalse()
    {
        var (symbol, solution, projectIds) = await CreateSymbolAndSolutionAsync("""
            public class MyClass { }
            public class Consumer { }
            """, "MyClass");

        bool result = await SemanticSearch.ManualSemanticSearchAsync(symbol, solution, projectIds);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task IsSymbolUsedInNode_WhenNodeIsDifferentClass_ReturnsFalse()
    {
        var (symbol, node, model) = await CreateSymbolAndNodeAsync("""
            public class MyClass { }
            public class Consumer {
                public void Method() {
                    var x = new Consumer();
                }
            }
            """, "MyClass", "ObjectCreationExpressionSyntax");

        bool result = await SemanticSearch.IsSymbolUsedInNode(symbol, node, model);

        Assert.IsFalse(result);
    }

    private static async Task<(INamedTypeSymbol symbol, SyntaxNode node, SemanticModel model)> CreateSymbolAndNodeAsync(
        string code, string typeName, string nodeTypeName)
    {
        var compilation = CreateCompilation(code);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var typeSymbol = model.GetDeclaredSymbol(root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == typeName)) as INamedTypeSymbol;

        var node = root.DescendantNodes().First(n => n.GetType().Name == nodeTypeName);

        return (typeSymbol!, node, model);
    }

    private static async Task<(INamedTypeSymbol symbol, SyntaxNode literalNode, SemanticModel model)> CreateTypeAndLiteralNodeAsync(
        string code, string typeName)
    {
        var compilation = CreateCompilation(code);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var typeSymbol = model.GetDeclaredSymbol(root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == typeName)) as INamedTypeSymbol;

        var literalNode = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()
            .First(l => l.Token.ValueText == typeName);

        return (typeSymbol!, literalNode, model);
    }

    private static async Task<(INamedTypeSymbol symbol, Solution solution, HashSet<ProjectId> projectIds)> CreateSymbolAndSolutionAsync(
        string code, string typeName)
    {
        var compilation = CreateCompilation(code);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var typeSymbol = model.GetDeclaredSymbol(root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == typeName)) as INamedTypeSymbol;

        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var projectId = Microsoft.CodeAnalysis.ProjectId.CreateNewId();
        var projectInfo = Microsoft.CodeAnalysis.ProjectInfo.Create(
            projectId,
            Microsoft.CodeAnalysis.VersionStamp.Default,
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp);
        var project = workspace.AddProject(projectInfo);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            "netstandard.dll")));
        var document = project.AddDocument("Test.cs", SourceText.From(code));
        var solution = document.Project.Solution;
        var projectIds = new HashSet<Microsoft.CodeAnalysis.ProjectId> { projectId };

        return (typeSymbol!, solution, projectIds);
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation CreateCompilation(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(DefaultMetadata + code);
        var references = new List<Microsoft.CodeAnalysis.MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                "netstandard.dll"))
        };

        return Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "TestCompilation",
            [tree],
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
