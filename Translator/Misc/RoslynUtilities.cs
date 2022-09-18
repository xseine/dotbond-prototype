using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Translator.Workspace;

namespace Translator.Misc;

public static class RoslynUtilities
{
    public static (List<(string Path, SyntaxTree Tree)>, Compilation, LanguageVersion, string) InitiateCompilation(string csprojFilePath)
    {
        MSBuildLocator.RegisterDefaults();
        
        var workspace = MSBuildWorkspace.Create();
        var project = workspace.OpenProjectAsync(csprojFilePath).Result;
        
        if (!workspace.Diagnostics.IsEmpty)
            workspace.Diagnostics.ForEach(e => Console.WriteLine(e.Message));
        
        var pathsWithTrees = project.Documents.Where(doc => doc.SourceCodeKind == SourceCodeKind.Regular).Select(doc => (Path: doc.FilePath, Tree: doc.GetSyntaxTreeAsync().Result!)).ToList();
        var languageVersion = ((CSharpParseOptions)pathsWithTrees.First()!.Tree!.Options).LanguageVersion;
        var compilation = project.GetCompilationAsync().Result;

        return (pathsWithTrees, compilation, languageVersion, compilation!.Assembly.Name);
    }

    public static Compilation CreateDemoCompilation()
    {
        var runtimeAssemblyRoot = Directory.GetParent(typeof(object).Assembly.Location).FullName;

        MetadataReference mscorlib =
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

        var neededAssemblies = new[]
        {
            "System.Runtime.dll",
            "mscorlib.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Text.RegularExpressions.dll",
            "System.Console.dll"
        };


        var references = neededAssemblies.Select(e => MetadataReference.CreateFromFile(Path.Combine(runtimeAssemblyRoot, e))).Cast<MetadataReference>().ToList();
        references.Add(mscorlib);

        var compilation = CSharpCompilation.Create("qwerty.dll",
            null,
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));


        return compilation;
    }

    public static bool InheritsFromController(INamedTypeSymbol symbol)
    {
        while (true)
        {
            if (symbol.ToString() == "Microsoft.AspNetCore.Mvc.Controller" || symbol.ToString() == "Microsoft.AspNetCore.Mvc.ControllerBase")
            {
                return true;
            }

            if (symbol.BaseType != null)
            {
                symbol = symbol.BaseType;
                continue;
            }

            break;
        }

        return false;
    }

    public static bool InheritsFromActionResult(INamedTypeSymbol symbol)
    {
        while (true)
        {
            if (symbol.ToString() == "Microsoft.AspNetCore.Mvc.IActionResult")
            {
                return true;
            }

            if (symbol.BaseType != null)
            {
                symbol = symbol.BaseType;
                continue;
            }

            break;
        }

        return false;
    }

    /// <summary>
    /// Replaces the tree for a given path.
    /// </summary>
    public static List<(string Path, SyntaxTree Tree)> ReplaceTree(this List<(string Path, SyntaxTree Tree)> source, string sourcePath, SyntaxTree newTree)
    {
        var idx = source.FindIndex(e => e.Path == sourcePath);
        source[idx] = (sourcePath, newTree);
        return source;
    }


    public static List<string> GetConstructorParametersNamespaces(ClassDeclarationSyntax classSyntax, SemanticModel semanticModel)
    {
        return classSyntax.DescendantNodes().OfType<ConstructorDeclarationSyntax>().FirstOrDefault()
            ?.DescendantNodes().OfType<ParameterSyntax>()
            .Select(parameter => (semanticModel.GetSymbolInfo(parameter.Type).Symbol as ITypeSymbol).ContainingNamespace.ToString()).ToList();
    }

    public static string GetSourceFile(this ITypeSymbol symbol) => symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath
                                                                   ?? throw new Exception("Symbol is not defined in the project's source code.");

    public static string GetSymbolFullName(this ISymbol symbol) => $"{symbol.ContainingNamespace}.{symbol.Name}";


    public static TypeSymbolLocation GetLocation(this ITypeSymbol typeSymbol) => new(typeSymbol.GetSourceFile(), typeSymbol.GetSymbolFullName());

    public static IEnumerable<ITypeSymbol> GetTypesInFile(FileAnalysisCallbackInput input, IEnumerable<string> typeNames)
    {
        var (syntaxTree, semanticModel, assemblyName) = input;
        return syntaxTree.GetRoot().DescendantNodes().Where(node => node is ClassDeclarationSyntax or RecordDeclarationSyntax or EnumDeclarationSyntax)
            .Select(definitionSyntax => (ITypeSymbol)semanticModel.GetDeclaredSymbol(definitionSyntax)!)
            .Where(symbol => typeNames.Contains($"{symbol.ContainingNamespace}.{symbol.Name}"))
            .Where(e => e!.ContainingAssembly.Name == assemblyName)
            .ToList();
    }

    public static List<string> GetSubclasses(Compilation compilation, string baseClass)
    {
        var types = compilation.GlobalNamespace.GetTypeMembers().Where(symbol =>
        {
            // Skip the baseClass itself, only subtypes
            if (symbol.Name == baseClass) return false;

            while (true)
            {
                if (symbol.Name == baseClass)
                {
                    return true;
                }

                if (symbol.BaseType != null)
                {
                    symbol = symbol.BaseType;
                    continue;
                }

                break;
            }

            return false;
        }).Select(symbol => symbol.Name).Distinct().ToList();

        return types;
    }
}

public record struct TypeSymbolLocation(string FilePath, string FullName);

public record struct UsedTypeSymbolLocation(TypeSymbolLocation Location, TypeSymbolLocation? UsingTypeLocation)
{
    public static implicit operator TypeSymbolLocation(UsedTypeSymbolLocation e) => e.Location;
}