using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.CodeAnalysis;
using DotBond;
using DotBond.Generators;
using DotBond.Generators.QueryCheckerGenerator;
using DotBond.IntegratedQueryRuntime;
using DotBond.Misc;
using DotBond.Workspace;
using static DotBond.Misc.Utilities;


Console.WriteLine("Hello.");

var csprojPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetFiles(Directory.GetCurrentDirectory()).FirstOrDefault(e => e.EndsWith(".csproj"));
if (csprojPath == null || !csprojPath.EndsWith(".csproj")) throw new ArgumentException(args.Length > 0 ? "Invalid path to .csproj file." : "Can't locate the csproj file in the current directory.");

var backendRoot = Directory.GetParent(csprojPath)!.FullName;
var bondConfig = BondConfigSchema.LoadFromFile(Path.Combine(backendRoot, "bond.json"));
FrontendDirectoryController.Setup(backendRoot, Path.Combine(backendRoot, bondConfig.OutputFolder));
LoggingUtilities.CsprojPath = csprojPath;
    
var fileObservable = new FileObservable(csprojPath);

Console.WriteLine("Compiled the project successfully.");

// Frontend generators that provide callbacks to analyze file file observable outputs
var generators = new List<AbstractGenerator> { new ApiGenerator(FileObservable.ASSEMBLY_NAME), new QueryCheckerGenerator(FileObservable.ASSEMBLY_NAME, fileObservable.Compilation) };

var trackedFileImports = new List<string>();
var translatedSymbolsAggregate = new Dictionary<string, List<(string identifierName, string translation)>>();

var initialFiles = fileObservable.GetAllCsFiles().Select(fileObservable.CreateCallbackInput);
var upToDateFiles = fileObservable.GetUpToDateFiles(); // Prevents unnecessary translation of 
// Translated and observed types/files
var currentlyTranslatedTypes = upToDateFiles.SelectMany(file => file.TranslatedTypes.Select(type => new TypeSymbolLocation(file.Path, type))).ToList();


var usedTypesSubject = new Subject<(List<TypeSymbolLocation> UsedTypes, TypeSymbolLocation? UsingType)>();
var usedTypesCollection = DependencyLogger.GetDependencyLogs().ToHashSet();
var usedTypesObservable = usedTypesSubject.AsObservable().Do(tuple => usedTypesCollection = UpdateUsedTypesCollection(tuple.UsedTypes, tuple.UsingType, usedTypesCollection))
    .Replay(1);

usedTypesObservable.Connect();

generators.Select(generator =>
        Observable.Merge(
            fileObservable.ObserveAddedFiles(generator.GetControllerCallback).Where(e => e != null)
                .StartWith(initialFiles.Select(generator.GetControllerCallback).Where(e => e != null).SelectMany(e => e).Distinct()),
            fileObservable.ObserveChangedFiles(generator.GetControllerCallback).Where(e => e != null).Delay(TimeSpan.FromTicks(1)),
            fileObservable.ObserveDeletedFiles(generator.DeleteSource).Where(e => e != null)
        )
    ).CombineLatest()
    .Select(result => result.Where(e => e != null).SelectMany(e => e).ToList())
    .Do(types => usedTypesCollection = UpdateUsedTypesCollection(types, null, usedTypesCollection))
    .Do(_ => Console.WriteLine($"Updated the frontend references. ({DateTime.Now.TimeOfDay:g})"))
    .Select(_ => usedTypesObservable.Select(_ => usedTypesCollection.Select(e => e.Location).Distinct().ToList()))
    .Switch()
    .DistinctUntilChanged(new LocationsComparer())
    .Select(types =>
    {
        var noLongerUsedTranslationFiles = currentlyTranslatedTypes.Select(e => e.FilePath).Except(types.Select(e => e.FilePath)).ToList();
        var filesWithAlteredTypeStructure = types.Except(currentlyTranslatedTypes).Concat(currentlyTranslatedTypes.Except(types))
            .Select(e => e.FilePath).Distinct().Except(noLongerUsedTranslationFiles);
        // Remove now empty files
        noLongerUsedTranslationFiles.ForEach(file =>
        {
            FrontendDirectoryController.DeleteFileAndEmptyParents(file);
            TranslationLogger.DeleteTranslationRecord(file[(backendRoot.Length + 1)..]);
        });

        currentlyTranslatedTypes = types;

        var filesWithTypes =
            types.GroupBy(symbol => symbol.FilePath).ToDictionary(e => e.Key, e => e.Select(ee => ee.FullName).Distinct().ToList()); //.Select(group => (Path: group.Key, Types: group.ToList()));

        return filesWithTypes
            .Select(tuple =>
                fileObservable.ObserveChangedFiles(analysisInput => RoslynUtilities.GetTypesInFile(analysisInput, tuple.Value), tuple.Key)
                    .Select(symbolsToTranslate => (tuple.Key, symbolsToTranslate))
                    .Merge(fileObservable.ObserveDeletedFiles(FrontendDirectoryController.DeleteFileAndEmptyParents, tuple.Key)
                        .Select(_ => (tuple.Key, (IEnumerable<ITypeSymbol>)null)).Where(_ => false))
            ).Merge()
            .StartWith(filesWithAlteredTypeStructure.Select(file => (file, RoslynUtilities.GetTypesInFile(fileObservable.CreateCallbackInput(file), filesWithTypes[file]))))
            .Do(tuple =>
            {
                var (sourcePath, symbolsToTranslate) = tuple;

                var attributesInFile = new HashSet<string>();

                var shouldCaptureReferences = !trackedFileImports.Contains(sourcePath);
                if (shouldCaptureReferences)
                {
                    trackedFileImports.Add(sourcePath);
                    translatedSymbolsAggregate[sourcePath] = new List<(string identifierName, string translation)>();
                }

                foreach (var referencedTypeSymbol in symbolsToTranslate.Where(e => e.Kind == SymbolKind.NamedType))
                {
                    var (translation, importedSymbols, attributes) = TranslateApi.Translate(referencedTypeSymbol, fileObservable.Compilation);
                    attributesInFile.UnionWith(attributes);

                    importedSymbols = importedSymbols.ToList();

                    usedTypesSubject.OnNext((importedSymbols.Any() ? importedSymbols.Select(e => e.GetLocation()).ToList() : null, referencedTypeSymbol.GetLocation()));

                    // Translation is added after all of its imports are translated, because order of declarations matters in TS
                    if (translatedSymbolsAggregate[sourcePath].All(e => e.identifierName != referencedTypeSymbol.GetSymbolFullName()))
                        translatedSymbolsAggregate[sourcePath].Add((referencedTypeSymbol.GetSymbolFullName(), translation));
                }

                if (shouldCaptureReferences)
                {
                    FrontendDirectoryController.WriteToAMirroredPath(sourcePath, string.Join("\n", translatedSymbolsAggregate[sourcePath].Select(e => e.translation)));
                    var b = usedTypesCollection.Where(e => e.UsingTypeLocation?.FilePath == sourcePath)
                        .Select(e => (e.Location.FullName[(e.Location.FullName.LastIndexOf(".") + 1)..], e.Location.FilePath)).ToList();
                    FrontendDirectoryController.AddImportStatementsToFile(sourcePath, b
                        .Concat(attributesInFile.Select(e => (e, TypescriptDecorators.DecoratorsPath))).ToList());
                    TypescriptDecorators.AddDecorators(attributesInFile.ToArray());

                    TranslationLogger.LogTranslation(sourcePath[(backendRoot.Length + 1)..], translatedSymbolsAggregate[sourcePath].Select(e => e.identifierName));
                    DependencyLogger.LogDependencies(usedTypesCollection);

                    trackedFileImports.Remove(sourcePath);
                    translatedSymbolsAggregate.Remove(sourcePath);
                }
            });
    }).Switch().Subscribe(tuple => Console.WriteLine($"Translated: {tuple.Item1}"));

usedTypesSubject.OnNext((null, null));

if (!File.Exists(FrontendDirectoryController.DetermineAngularPath(ApiGenerator.QueryServicePath)))
    FrontendDirectoryController.WriteToAngularDirectory(ApiGenerator.QueryServicePath, ApiBoilerplate.GetQueryServiceContent(await FrontendDirectoryController.GetServerAddressFromProxy()));
RequiredFiles.CreateRequiredFiles(FrontendDirectoryController.GetAngularRootPath());

EndpointGenInitializer.InitializeTranslator(fileObservable, csprojPath);

Console.WriteLine($"Started file watcher on: {ApiGenerator.QueryServicePath}.");

await EndpointSignatureObservable.InitializeObservable(fileObservable, csprojPath);

Console.WriteLine("Started file watcher on C# source files.");
Console.WriteLine("""
###################
DotBond is enabled!
###################
""");

await Observable.Never<int>();