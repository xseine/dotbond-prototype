using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using DotBond.Generators;
using DotBond.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBond.Misc;

namespace DotBond.IntegratedQueryRuntime;

public static class EndpointSignatureObservable
{
    private const string SignatureSeparator = "|";
    
    /// <summary>
    /// Latest version of query implementations file, in its entirety (without API removal)
    /// </summary>
    private static SyntaxTree _lockedOriginalImplementationsTree;
    
    /// <summary>
    /// Latest version of query controller files, in its entirety (without API removal)
    /// </summary>
    private static SyntaxTree _lockedOriginalControllerTree;

    public const string LockedImplementationsFileName = EndpointGenInitializer.QueryImplementationsFile + ".locked";
    public const string LockedControllerFileName = EndpointGenInitializer.QueryControllerFile + ".locked";
    public static string ControllersPath;
    private static FileSystemWatcher _fileSystemWatcher;
    private static bool _skipInternalEvents = true;

    // const int STD_INPUT_HANDLE = -10;
    //
    // [DllImport("kernel32.dll", SetLastError = true)]
    // internal static extern IntPtr GetStdHandle(int nStdHandle);
    //
    // [DllImport("kernel32.dll", SetLastError = true)]
    // static extern bool CancelIoEx(IntPtr handle, IntPtr lpOverlapped);
    
    public static async Task InitializeObservable(FileObservable fileObservable, string csprojPath)
    {
        var backendRoot = Directory.GetParent(csprojPath)!.FullName;
        ControllersPath = Path.Combine(backendRoot, "Controllers");

        TryAddQueryFilesIfMissing(ref fileObservable.Compilation, fileObservable.LanguageVersion);

        var implementationsPath = Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile);
        var lockedImplementationsPath = Path.Combine(ControllersPath, LockedImplementationsFileName);
        
        var controllerPath = Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile);
        var lockedControllerPath = Path.Combine(ControllersPath, LockedControllerFileName);

        var implementationsContent = File.Exists(lockedImplementationsPath) ? await File.ReadAllTextAsync(lockedImplementationsPath) : await File.ReadAllTextAsync(implementationsPath);
        var controllerContent = File.Exists(lockedControllerPath) ? await File.ReadAllTextAsync(lockedControllerPath) : await File.ReadAllTextAsync(controllerPath);
        
        if (!File.Exists(lockedImplementationsPath)) await File.WriteAllTextAsync(lockedImplementationsPath, implementationsContent);
        if (!File.Exists(lockedControllerPath)) await File.WriteAllTextAsync(lockedControllerPath, controllerContent);

        CreateLockedTrees(fileObservable, implementationsContent, controllerContent);

        var watcher = InitializeFileWatcher(implementationsPath);
        
        // Get newest version of implementations by observing fs for external changes
        // When the implementation file changes, proceed with signatures check only if the user confirms it
        var implementationObservable = Observable.FromEventPattern<FileSystemEventArgs>(watcher, nameof(watcher.Changed))
            .Throttle(TimeSpan.FromMilliseconds(50))
            .Where(_ => !_skipInternalEvents)
            .Do(async _ => await UpdateLockedContent(fileObservable, implementationsPath, lockedImplementationsPath, controllerPath, lockedControllerPath))
            .Select(_ =>
            {
                Console.Write("Generated query implementations file has changed.\nDo you want check signatures of used endpotins [Y/n]:");
                return FromConsole.Select(e => e.Trim() == "" || e.Trim().ToLower() == "y");
                // return Observable.FromAsync(() => Console.In.ReadLineAsync()).Select(e => e.Trim() == "" || e.Trim().ToLower() == "y");
            })
            // .ObserveOn(new NewThreadScheduler())
            .Switch()
            .Where(e => e);

        // Get actions from changed (deleted) files, and run the check automatically
        var actionsObservable = fileObservable
            .ObserveChangedFiles(input =>
                // ApiGenerator.GetActionsFromController(input.FileTree, input.SemanticModel)?.CheckForOutOfDateSignatures(fileObservable.Compilation) ?? false)
                !input.FileTree.FilePath.EndsWith(EndpointGenInitializer.QueryImplementationsFile) && ApiGenerator.GetActionsFromController(input.FileTree, input.SemanticModel).Any())
            .Where(e => e);
        
        actionsObservable.Merge(implementationObservable)
            .StartWith(true)
            .Select(_ => RemoveOutOfDateSignaturesAndLogTheRest(ref fileObservable.Compilation, fileObservable.LanguageVersion))
            .Where(tuple => tuple != null)
            .Do(_ => _skipInternalEvents = true)
            .AfterComplete((tuple, cancellationToken) => Task.WhenAll(
                File.WriteAllTextAsync(Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile), tuple.Value!.newImplementationsSource, cancellationToken),
                File.WriteAllTextAsync(Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile), tuple.Value!.newControllerSource, cancellationToken))
            )
            .Do(_ => Observable.Timer(TimeSpan.FromMilliseconds(100)).Take(1).Subscribe(_ => _skipInternalEvents = false))
            .Subscribe();


        // Save from disposing
        _fileSystemWatcher = watcher;
    }

    private static void CreateLockedTrees(FileObservable fileObservable, string implementationsContent, string controllerContent)
    {
        implementationsContent = implementationsContent.Replace("namespace GeneratedControllers", "namespace GeneratedControllersLock");
        _lockedOriginalImplementationsTree = CSharpSyntaxTree.ParseText(implementationsContent, new CSharpParseOptions(fileObservable.LanguageVersion));
        fileObservable.Compilation = fileObservable.Compilation.AddSyntaxTrees(_lockedOriginalImplementationsTree);

        controllerContent = controllerContent.Replace("namespace GeneratedControllers", "namespace GeneratedControllersLock");
        _lockedOriginalControllerTree = CSharpSyntaxTree.ParseText(controllerContent, new CSharpParseOptions(fileObservable.LanguageVersion));
        fileObservable.Compilation = fileObservable.Compilation.AddSyntaxTrees(_lockedOriginalControllerTree);
    }

    private static async Task UpdateLockedContent(FileObservable fileObservable, string implementationsPath, string lockedImplementationsPath, string controllerPath, string lockedControllerPath)
    {
        var newImplementationsContent = (await File.ReadAllTextAsync(implementationsPath)).Replace("namespace GeneratedControllers", "namespace GeneratedControllersLock");
        await File.WriteAllTextAsync(lockedImplementationsPath, newImplementationsContent);
        var oldImplementationsTree = _lockedOriginalImplementationsTree;
        _lockedOriginalImplementationsTree = CSharpSyntaxTree.ParseText(newImplementationsContent, new CSharpParseOptions(fileObservable.LanguageVersion));
        fileObservable.Compilation = fileObservable.Compilation.ReplaceSyntaxTree(oldImplementationsTree, _lockedOriginalImplementationsTree);

        var newControllerContent = (await File.ReadAllTextAsync(controllerPath)).Replace("namespace GeneratedControllers", "namespace GeneratedControllersLock");
        await File.WriteAllTextAsync(lockedControllerPath, newControllerContent);
        var oldControllerTree = _lockedOriginalControllerTree;
        _lockedOriginalControllerTree = CSharpSyntaxTree.ParseText(newControllerContent, new CSharpParseOptions(fileObservable.LanguageVersion));
        fileObservable.Compilation = fileObservable.Compilation.ReplaceSyntaxTree(oldControllerTree, _lockedOriginalControllerTree);
    }


    /*========================== Private API ==========================*/

    /// <summary>
    /// Removes actions with out-of-date endpoint references,
    /// and return method signatures of endpoint references in remaining methods.
    /// </summary>
    private static (string newImplementationsSource, string newControllerSource)? RemoveOutOfDateSignaturesAndLogTheRest(ref Compilation compilation, LanguageVersion languageVersion)
    {
            
        if (_lockedOriginalImplementationsTree == null) return null;
        var semanticModel = compilation.GetSemanticModel(_lockedOriginalImplementationsTree);
        
        var namesOfMethodsToDelete = _lockedOriginalImplementationsTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(e => e is { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } }
                        && ModelExtensions.GetSymbolInfo(semanticModel, id).Symbol?.Kind == SymbolKind.Local)
            .Select(e =>
            {

                var returnNode = (SyntaxNode)e;
                while (returnNode is not ReturnStatementSyntax) returnNode = returnNode.Parent;

                var returnHasErrors = semanticModel.GetTypeInfo(((ReturnStatementSyntax) returnNode).Expression).Type is IErrorTypeSymbol;

                var actionName = returnHasErrors ? ((MethodDeclarationSyntax)returnNode.Parent.Parent).Identifier.Text : null;
                return actionName;
            })
            .Distinct()
            .ToList();

        // Remove actions
        var nodesToDelete = _lockedOriginalImplementationsTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => namesOfMethodsToDelete.Contains(e.Identifier.Text));
        var newNode = _lockedOriginalImplementationsTree.GetRoot().RemoveNodes(nodesToDelete, SyntaxRemoveOptions.KeepNoTrivia);
        var namespaceToUpdate = newNode.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().First();
        newNode = newNode.ReplaceNode(namespaceToUpdate, namespaceToUpdate.WithName(SyntaxFactory.IdentifierName("GeneratedControllers")));
        var newTree = newNode.SyntaxTree.WithFilePath(Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile));
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(e => e.FilePath == Path.Combine(ControllersPath, EndpointGenInitializer.QueryImplementationsFile)), newTree);

        // Update controller compilation
        var controllerNodesToDelete = _lockedOriginalControllerTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => namesOfMethodsToDelete.Contains(e.Identifier.Text));
        var newControllerNode = _lockedOriginalControllerTree.GetRoot().RemoveNodes(controllerNodesToDelete, SyntaxRemoveOptions.KeepNoTrivia);
        namespaceToUpdate = newControllerNode.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().First();
        newControllerNode = newControllerNode.ReplaceNode(namespaceToUpdate, namespaceToUpdate.WithName(SyntaxFactory.IdentifierName("GeneratedControllers")));
        var newControllerTree = newControllerNode.SyntaxTree.WithFilePath(Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile));
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(e => e.FilePath == Path.Combine(ControllersPath, EndpointGenInitializer.QueryControllerFile)), newControllerTree);
        
        // Return signatures of endpoints in remaining actions
        return (newTree.ToString(), newControllerTree.ToString());
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

    private static FileSystemWatcher InitializeFileWatcher(string path)
    {
        var watcher = new FileSystemWatcher(Directory.GetParent(path)!.FullName);
        watcher.Filter = Path.GetFileName(path)!;
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static IObservable<string> FromConsole =
        Observable
            .Defer(() =>
                Observable
                    .Start(() => Console.ReadLine()));
    // .Repeat()
    // .Publish()
    // .RefCount()
    // .Do(e => Console.WriteLine("Printed: " + e));
}