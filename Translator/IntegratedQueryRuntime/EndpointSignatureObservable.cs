using System.Diagnostics;
using System.Reactive.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.Generators;
using Translator.Misc;
using Translator.Workspace;

namespace Translator.IntegratedQueryRuntime;

public static class EndpointSignatureObservable
{
    public const string LockedImplementationsFileName = EndpointGenInitializer.QueryImplementationsFile + ".locked";
    public static string ControllersPath;
    private static readonly string SignatureSeparator = "|";
    
    /// <summary>
    /// This will tree be saved on the first run, so changes can be rolled back
    /// </summary>
    private static SyntaxTree _lockedOriginalTree;
    
    // Same thing for the controller file.
    private static SyntaxTree _originalControllerTree;

    public static void InitializeObservable(FileObservable fileObservable, string csprojPath)
    {
        var backendRoot = Directory.GetParent(csprojPath)!.FullName;
        ControllersPath = Path.Combine(backendRoot, "Controllers");
        
        // When the implementation file changes, proceed with signatures check only if the user confirms it
        var implementationObservable = fileObservable.ObserveChangedFiles(e => e.FileTree.FilePath == Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile))
            .Where(e => e)
            .Select(_ =>
            {
                Console.Write("Generated query implementations file has changed.\nDo you want check signatures of used endpotins [Y/n]:");
                return Observable.FromAsync(() => Console.In.ReadLineAsync()).Select(e => e.Trim() == "" || e.Trim().ToLower() == "y");
            })
            .Switch()
            .Where(e => e);

        // Get actions from changed (deleted) files, and run the check automatically
        var actionsObservable = fileObservable
            .ObserveChangedFiles(input =>
                // ApiGenerator.GetActionsFromController(input.FileTree, input.SemanticModel)?.CheckForOutOfDateSignatures(fileObservable.Compilation) ?? false)
                ApiGenerator.GetActionsFromController(input.FileTree, input.SemanticModel).Any())
            .Where(e => e);
        
        actionsObservable.Merge(implementationObservable)
            .StartWith(true)
            .Select(_ => RemoveOutOfDateSignaturesAndLogTheRest(ref fileObservable.Compilation, fileObservable.LanguageVersion))
            .Where(tuple => tuple != null)
            .AfterComplete((tuple, cancellationToken) => Task.WhenAll(
               // File.WriteAllTextAsync(
                File.WriteAllTextAsync(Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile), tuple.Value!.newImplementationsSource, cancellationToken),
                File.WriteAllTextAsync(Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile), tuple.Value!.newControllerSource, cancellationToken))
            )
            .Subscribe();
    }


    /*========================== Private API ==========================*/

    /// <summary>
    /// Removes actions with out-of-date endpoint references,
    /// and return method signatures of endpoint references in remaining methods.
    /// </summary>
    private static (string signatures, string newImplementationsSource, string newControllerSource)? RemoveOutOfDateSignaturesAndLogTheRest(ref Compilation compilation, LanguageVersion languageVersion)
    {
        var areSyntaxTreesPresent = TryAddQueryFilesIfMissing(ref compilation, languageVersion);
        if (areSyntaxTreesPresent == false) return null;

        AddLockedSyntaxTree(ref compilation, languageVersion);
            
        if (_lockedOriginalTree == null) return null;
        var semanticModel = compilation.GetSemanticModel(_lockedOriginalTree);
        
        var invocationsToLock = _lockedOriginalTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(e => e is { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } }
                        && ModelExtensions.GetSymbolInfo(semanticModel, id).Symbol?.Kind == SymbolKind.Local)
            .Select(e =>
            {

                var returnNode = (SyntaxNode)e;
                while (returnNode is not ReturnStatementSyntax) returnNode = returnNode.Parent;

                var returnHasErrors = semanticModel.GetTypeInfo(((ReturnStatementSyntax) returnNode).Expression).Type is IErrorTypeSymbol;

                var actionName = returnHasErrors ? ((MethodDeclarationSyntax)returnNode.Parent.Parent).Identifier.Text : null;
                
                var symbol = (IMethodSymbol)semanticModel.GetSymbolInfo(e).Symbol;
                return (symbol == null ? null : new[] { symbol.GetSymbolFullName(), symbol.ReturnType.ToString() }.Concat(symbol.Parameters.Select(p => p.Type.ToString())).ToList(), DeleteAction: actionName);
            })
            .DistinctBy(e => e.Item1?[0])
            .ToList();

        // Extract actions for deletions
        var namesOfMethodsToDelete = invocationsToLock.Where(e => e.DeleteAction != null).Select(e => e.DeleteAction).ToList();

        // Remove actions
        var nodesToDelete = _lockedOriginalTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => namesOfMethodsToDelete.Contains(e.Identifier.Text));
        var newNode = _lockedOriginalTree.GetRoot().RemoveNodes(nodesToDelete, SyntaxRemoveOptions.KeepNoTrivia);
        var namespaceToUpdate = newNode.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().First();
        newNode = newNode.ReplaceNode(namespaceToUpdate, namespaceToUpdate.WithName(SyntaxFactory.IdentifierName("GeneratedControllers")));
        var newTree = newNode.SyntaxTree.WithFilePath(Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile));

        // Update compilation
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(e => e.FilePath == Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile)), newTree);
        _originalControllerTree ??= compilation.SyntaxTrees.First(e => e.FilePath == Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile));
        var controllerNodesToDelete = _originalControllerTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => namesOfMethodsToDelete.Contains(e.Identifier.Text));
        var newControllerTree = _originalControllerTree.GetRoot().RemoveNodes(controllerNodesToDelete, SyntaxRemoveOptions.KeepNoTrivia).SyntaxTree;
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(e => e.FilePath == Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile)), newControllerTree);
        
        // Return signatures of endpoints in remaining actions
        return (string.Join("\n", invocationsToLock.Where(e => e.Item1 != null).Select(e => string.Join(SignatureSeparator, e.Item1))), newTree.ToString(), newControllerTree.ToString());
    }

    /// <summary>
    /// Original locked tree  has all the generated methods, and its semantic model is checked for errors,
    /// rather that a already processed version whose removed method might become valid again after newest changes. 
    /// </summary>
    private static void AddLockedSyntaxTree(ref Compilation compilation, LanguageVersion languageVersion)
    {
        if (compilation.SyntaxTrees.Any(e => e == _lockedOriginalTree)) return;
        
        var fileContent = File.ReadAllText(Path.Combine(ControllersPath, LockedImplementationsFileName));
        fileContent = fileContent.Replace("namespace GeneratedControllers", "namespace GeneratedControllersLock");
        _lockedOriginalTree = CSharpSyntaxTree.ParseText(fileContent, new CSharpParseOptions(languageVersion));
        compilation = compilation.AddSyntaxTrees(_lockedOriginalTree);
    }

    private static bool TryAddQueryFilesIfMissing(ref Compilation compilation, LanguageVersion languageVersion)
    {
        var implementationsPath = Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile);
        var controllerPath = Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile);
        
        if (compilation.SyntaxTrees.Any(e => e.FilePath == implementationsPath)) return true;

        if (!File.Exists(Path.Combine(ControllersPath, LockedImplementationsFileName)) || !File.Exists(Path.Combine(ControllersPath, LockedImplementationsFileName)))
            return false;

        if (compilation.SyntaxTrees.All(e => e.FilePath != implementationsPath))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(implementationsPath), new CSharpParseOptions(languageVersion))
                .WithFilePath(implementationsPath);
            compilation = compilation.AddSyntaxTrees(tree);
        }

        if (compilation.SyntaxTrees.All(e => e.FilePath != controllerPath))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(controllerPath), new CSharpParseOptions(languageVersion))
                .WithFilePath(controllerPath);
            compilation = compilation.AddSyntaxTrees(tree);
        }

        return true;
    }
}