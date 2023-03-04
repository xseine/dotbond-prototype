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
    public QueryCheckerGenerator QueryCheckerGenerator { get; set; }
    
    [SetUp]
    public void Setup()
    {
        QueryCheckerGenerator = new QueryCheckerGenerator(FileObservable.ASSEMBLY_NAME);
    }

    [Test]
    [TestCase(@"C:\Users\illus\VSProjects\BondPrototype\BondPrototype\BondPrototype.csproj")]
    public async Task TestEntities(string csprojPath)
    {
        var fileObservable = new FileObservable(csprojPath);
        var initialFiles = fileObservable.GetAllCsFiles().Select(fileObservable.CreateCallbackInput).ToList();
        var entitiesFile = initialFiles.First(e => e.FileTree.FilePath.EndsWith("Entities.cs"));
        var apiFile = initialFiles.First(e => e.FileTree.FilePath.EndsWith("MovieApiController.cs"));

        QueryCheckerGenerator.GetControllerCallback(entitiesFile);
        QueryCheckerGenerator.GetControllerCallback(apiFile);


        // var relationships = QueryCheckerGenerator.GetRelationshipsOfActions().ToList();
        //
        // Assert.NotNull(relationships.FirstOrDefault(e => e is { ActionA: "GetMovies", ActionB: "GetActors" }));
    }
}