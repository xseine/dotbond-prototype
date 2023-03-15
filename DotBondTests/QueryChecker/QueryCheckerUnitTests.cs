using System;
using System.Linq;
using System.Threading.Tasks;
using DotBond.Generators.QueryCheckerGenerator;
using DotBond.Misc;
using DotBond.Workspace;
using NUnit.Framework;

namespace DotBondTests.QueryChecker;


public class QueryCheckerUnitTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    [TestCase(@"C:\Users\illus\VSProjects\BondPrototype\BondPrototype\BondPrototype.csproj")]
    public async Task TestEntities(string csprojPath)
    {
        var fileObservable = new FileObservable(csprojPath);
        var initialFiles = fileObservable.GetAllCsFiles().Select(fileObservable.CreateCallbackInput).ToList();
        var entitiesFile = initialFiles.First(e => e.FileTree.FilePath.EndsWith("Entities.cs"));
        var apiFile = initialFiles.First(e => e.FileTree.FilePath.EndsWith("MovieApiController.cs"));

        var queryCheckerGenerator = new QueryCheckerGenerator(FileObservable.ASSEMBLY_NAME, fileObservable.Compilation);
        
        queryCheckerGenerator.GetControllerCallback(entitiesFile);
        queryCheckerGenerator.GetControllerCallback(apiFile);


        // var relationships = QueryCheckerGenerator.GetRelationshipsOfActions().ToList();
        //
        // Assert.NotNull(relationships.FirstOrDefault(e => e is { ActionA: "GetMovies", ActionB: "GetActors" }));
    }
}