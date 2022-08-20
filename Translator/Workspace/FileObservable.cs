using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Translator.IntegratedQueryRuntime;
using Translator.Misc;
using static Translator.Misc.Utilities;

namespace Translator.Workspace;

public record FileAnalysisCallbackInput(SyntaxTree FileTree, SemanticModel SemanticModel, string AssemblyName);

public class FileObservable : IDisposable
{
    private readonly string _csProjPath;
    private string ProjectDir => Directory.GetParent(_csProjPath)!.FullName;

    private IEnumerable<string> ExcludedPaths => Regex.Matches(File.ReadAllText(_csProjPath), @$"Compile Remove=(?<path>{TsRegexRepository.MatchBalancedTokens("\"")})")
        .Select(e => Path.GetFullPath(e.Groups["path"].Value.Replace("*", "")));

    public static string ASSEMBLY_NAME = "BondCompilation";


    private readonly List<(string Path, SyntaxTree Tree)> _pathsWithTrees;
    public Compilation Compilation;
    public readonly LanguageVersion LanguageVersion;
    private readonly FileSystemWatcher _watcher;
    private readonly EventLoopScheduler _eventLoopScheduler = new();

    /// <summary>
    /// Result emitted by the file watcher.
    /// </summary>
    /// <param name="FilePath">Path of the file that was added, changed or deleted.</param>
    /// <param name="ExtractedSymbols">Symbols extracted using a caller specified function.</param>
    /// <param name="IsDeleted"></param>
    public readonly record struct SymbolsInFile(string FilePath, IEnumerable<ISymbol> ExtractedSymbols, bool IsDeleted);


    private IObservable<SymbolsInFile> _filesGetExcludedOrIncluded;
    private IObservable<(string filePath, FileAnalysisCallbackInput analysisInput)> _sharedChangeObservable;
    private IObservable<(string filePath, FileAnalysisCallbackInput analysisInput)> _sharedAdditionObservable;
    private IObservable<string> _sharedDeletionObservable;

    /// <summary>
    /// Provides observables for added, changed or deleted files
    /// along with Roslyn symbols used in those files.
    /// </summary>
    /// <param name="csprojPath"></param>
    public FileObservable(string csprojPath)
    {
        _csProjPath = csprojPath;
        (_pathsWithTrees, Compilation, LanguageVersion, ASSEMBLY_NAME) = RoslynUtilities.InitiateCompilation(csprojPath);

        _watcher = CreateSourceWatcher(csprojPath);
    }

    /*========================== Public API ==========================*/

    public IObservable<TResult> ObserveAddedFiles<TResult>(Func<FileAnalysisCallbackInput, TResult> roslynCallback, string specificFile = null)
    {
        _sharedAdditionObservable ??= HotAdditionObserver();

        return (specificFile == null ? _sharedAdditionObservable : _sharedAdditionObservable.Where(e => e.filePath == specificFile))
            .Select(tuple => roslynCallback(tuple.analysisInput));

        // Local function to create observable from file changes
        IObservable<(string filePath, FileAnalysisCallbackInput analysisInput)> HotAdditionObserver()
        {
            return FromSourceFileChange(nameof(_watcher.Created)).ObserveOn(_eventLoopScheduler)
                .LeadingSample(TimeSpan.FromMilliseconds(1))
                .Where(path => ExcludedPaths.All(excluded => !path.StartsWith(excluded))) // Skip excluded files
                .AfterRead(File.ReadAllTextAsync).ObserveOn(_eventLoopScheduler)
                .Where(result => result.FileContent != null)
                .Select(result =>
                {
                    var (filePath, fileContent) = result;
                    var newTree = CSharpSyntaxTree.ParseText(fileContent, new CSharpParseOptions(LanguageVersion)).WithFilePath(filePath);

                    // Add newTree
                    _pathsWithTrees.Add((filePath, newTree));
                    Compilation = Compilation.AddSyntaxTrees(newTree);

                    var semanticModel = Compilation.GetSemanticModel(newTree);

                    // Return models from the newTree
                    return (filePath, new FileAnalysisCallbackInput(newTree, semanticModel, ASSEMBLY_NAME));
                });
        }
    }

    public IObservable<TResult> ObserveChangedFiles<TResult>(Func<FileAnalysisCallbackInput, TResult> roslynCallback, string specificFile = null)
    {
        _sharedChangeObservable ??= HotChangeObserver();

        return (specificFile == null ? _sharedChangeObservable : _sharedChangeObservable.Where(e => e.filePath == specificFile))
            .Where(tuple => tuple.filePath != null) // TODO: Unknown cause of this
            .Select(tuple => roslynCallback(tuple.analysisInput));

        // Local function to create observable from file changes
        IObservable<(string filePath, FileAnalysisCallbackInput analysisInput)> HotChangeObserver()
        {
            return FromSourceFileChange(nameof(_watcher.Changed)).ObserveOn(_eventLoopScheduler)
                .LeadingSample(TimeSpan.FromMilliseconds(1))
                // .Do(_ => Console.WriteLine("start: " + Thread.CurrentThread.ManagedThreadId))
                .AfterRead(File.ReadAllTextAsync).ObserveOn(_eventLoopScheduler)
                .Where(result => result.FileContent != null)
                .Select(result =>
                {
                    // Console.WriteLine("finish: " + Thread.CurrentThread.ManagedThreadId);

                    var (filePath, fileContent) = result;

                    var oldTree = _pathsWithTrees.FirstOrDefault(e => e.Path == filePath).Tree;
                    if (oldTree == default) return default;
                    var newTree = CSharpSyntaxTree.ParseText(fileContent, new CSharpParseOptions(LanguageVersion)).WithFilePath(filePath);

                    // Replace oldTree with newTree
                    _pathsWithTrees.ReplaceTree(filePath, newTree);
                    Compilation = Compilation.ReplaceSyntaxTree(oldTree, newTree);

                    var semanticModel = Compilation.GetSemanticModel(newTree);

                    // Return models from the newTree
                    return (filePath, new FileAnalysisCallbackInput(newTree, semanticModel, ASSEMBLY_NAME));
                }).Publish().RefCount();
        }
    }

    public IObservable<TResult> ObserveDeletedFiles<TResult>(Func<string, TResult> deleteCallback, string specificFile = null)
    {
        _sharedDeletionObservable ??= HotDeletionObserver();

        return (specificFile == null ? _sharedDeletionObservable : _sharedDeletionObservable.Where(filePath => filePath == specificFile))
            .Select(deleteCallback);
        
        IObservable<string> HotDeletionObserver()
        {
            return FromSourceFileChange(nameof(_watcher.Deleted)).ObserveOn(_eventLoopScheduler)
                .Do(filePath =>
                {
                    var syntaxTree = Compilation.SyntaxTrees.FirstOrDefault(e => e.FilePath == filePath);
                    if (syntaxTree != default)
                    {
                        // Remove syntaxTree
                        _pathsWithTrees.Remove((filePath, syntaxTree));
                        Compilation = Compilation.RemoveSyntaxTrees(syntaxTree);
                    }
                }).Publish().RefCount();
        }
    }


    public IEnumerable<(string Path, List<string> TranslatedTypes)> GetUpToDateFiles()
    {
        var translationLogs = TranslationLogger.GetTranslationLogs();
        var loggedFilesWithLatestWriteTimes = translationLogs
            .Where(log => new FileInfo(Path.Combine(ProjectDir, log.TranslationId)).LastWriteTimeUtc - log.TranslationDateTime < TimeSpan.FromSeconds(1))
            .Select(log => (log.TranslationId, log.TranslatedTypes.ToList()))
            .ToList();

        var filesDeletedWhileOffline = loggedFilesWithLatestWriteTimes.Where(tuple => !FrontendDirectoryController.DoesFileExist(tuple.Item1)).ToList();
        filesDeletedWhileOffline.ForEach(tuple => TranslationLogger.DeleteTranslationRecord(tuple.Item1));

        return loggedFilesWithLatestWriteTimes.Except(filesDeletedWhileOffline).Select(tuple => (Path.Combine(ProjectDir, tuple.TranslationId), tuple.Item2));
    }
    

    public List<string> GetAllCsFiles() => Compilation.SyntaxTrees.Select(e => e.FilePath).Where(path => !path[(ProjectDir.Length + 1)..].StartsWith("obj")
                                                                                                         && !path.EndsWith(EndpointGenInitializer.QueryImplementationsFile)
                                                                                                         && !path.EndsWith(EndpointGenInitializer.QueryControllerFile)).ToList();

    public FileAnalysisCallbackInput CreateCallbackInput(string filePath)
    {
        var tree = Compilation.SyntaxTrees.FirstOrDefault(tree => tree.FilePath == filePath);
        if (tree == null) throw new Exception($"Filepath {filePath} doesn't belong to the project.");

        var semanticModel = Compilation.GetSemanticModel(tree);
        return new FileAnalysisCallbackInput(tree, semanticModel, ASSEMBLY_NAME);
    }


    public void Dispose()
    {
        _watcher.Dispose();
    }

    /*========================== Private API ==========================*/

    private static FileSystemWatcher CreateSourceWatcher(string csprojFile)
    {
        var watcher = new FileSystemWatcher(Directory.GetParent(csprojFile)!.FullName);
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
        watcher.Filter = "*.cs";
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    private static bool IsSourceFile(string fullPath) => Path.GetExtension(fullPath).ToLower() == ".cs";

    private IObservable<string> FromSourceFileChange(string eventName) =>
        Observable.FromEventPattern<FileSystemEventArgs>(_watcher, eventName)
            .Select(pattern => pattern.EventArgs.FullPath)
            .Where(IsSourceFile)
            .Where(filePath => Path.GetFileName(filePath) is not (EndpointGenInitializer.QueryImplementationsFile or EndpointGenInitializer.QueryControllerFile));
    // .Do(_ => Console.WriteLine("The tap."));

    static IObservable<List<string>> GetFileExclusions(string csprojFile, FileSystemWatcher watcher)
    {
        var excludedFilesComparer = CreateEqualityComparer<List<string>>((listA, listB) => listA.Count == listB.Count && listA.All(listB.Contains));

        return Observable.FromEventPattern<FileSystemEventArgs>(watcher, nameof(watcher.Changed))
            .Select(pattern => pattern.EventArgs.FullPath)
            .Where(fullPath => csprojFile == fullPath)
            .StartWith(csprojFile)
            .Select(GetExcludedFiles)
            .DistinctUntilChanged(excludedFilesComparer);
    }
}

public delegate IEnumerable<ITypeSymbol> RoslynFileCallback(FileAnalysisCallbackInput fileInput);