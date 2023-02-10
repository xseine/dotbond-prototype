using DotBond.Misc;
using DotBond.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.Generators.QueryCheckerGenerator;

public class QueryCheckerGenerator : AbstractGenerator
{
    private Dictionary<string, string[]> _entityRelationships;
    private readonly List<string> _dbContextTypes = new();
    private readonly List<(string Route, IMethodSymbol ActionSymbol, string FilePath)> _actions = new();
    private Dictionary<string, string[]> _entitiesUsageInActions;

    public QueryCheckerGenerator(string assemblyName) : base(assemblyName)
    {
    }

    public override HashSet<TypeSymbolLocation> GetControllerCallback(FileAnalysisCallbackInput input)
    {
        var dbContextInFile = input.FileTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(e => e.BaseList?.Types.Any(baseType => baseType.Type is IdentifierNameSyntax { Identifier.Text: "DbContext" }) ?? false);

        if (dbContextInFile != null)
            RetrieveEntityRelationships(input, dbContextInFile);

        if (_entityRelationships == null || !_entityRelationships.Any()) return null;

        RetrieveEntityUsagesInActions(input);

        return null;
    }

    public override HashSet<TypeSymbolLocation> DeleteSource(string filePath)
    {
        throw new NotImplementedException();
    }

    /*========================== Public API ==========================*/

    /// <summary>
    /// Currently doesn't inform correctly about the type of relationship.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<(string ActionA, string ActionB, ActionRelatioship relatioshipType)> GetRelationshipsOfActions()
    {
        return _entityRelationships.SelectMany(pair => pair.Value.SelectMany(entityB =>
            _entitiesUsageInActions[pair.Key]
                .SelectMany(actionA => _entitiesUsageInActions[entityB].Select(actionB => (actionA, actionB, ActionRelatioship.OneToOne)))));
    }

    public enum ActionRelatioship
    {
        OneToOne,
        OneToMany,
        ManyToMany
    }

    /*========================== Private API ==========================*/

    /// <summary>
    /// Finds all db sets from the files, and reads their relationships using navigation properties.
    /// </summary>
    private void RetrieveEntityRelationships(FileAnalysisCallbackInput input, ClassDeclarationSyntax dbContextInFile)
    {
        var entitiesInContext = dbContextInFile.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Select(prop => prop is { Type: GenericNameSyntax { Identifier.Text: "DbSet", TypeArgumentList.Arguments: [var entityType] } }
                ? (DbSetName: prop.Identifier.Text, Symbol: input.SemanticModel.GetSymbolInfo(entityType).Symbol)
                : default)
            .Where(e => e != default).ToList();

        var namesOfEntities = entitiesInContext.Select(e => e.Symbol.Name).ToList();
        _entityRelationships = entitiesInContext.ToDictionary(entityType => entityType.DbSetName, entityType =>
            GetTypesOfNavigationProperties(entityType, namesOfEntities)
                .Select(type => entitiesInContext.First(e => e.Symbol.Name == type).DbSetName).Distinct().ToArray());

        _dbContextTypes.Add(dbContextInFile.Identifier.Text);
    }

    /// <summary>
    /// Forms the new actions array, and sets <see cref="_entitiesUsageInActions"/>
    /// which is used to translate entities' relationships into actions' relationships.
    /// </summary>
    private void RetrieveEntityUsagesInActions(FileAnalysisCallbackInput input)
    {
        var actionsInFile = ApiGenerator.RetrieveActionsFromController(input.FileTree, input.SemanticModel);
        if (actionsInFile.Any() == false) return;

        var previouslyRetrieved = _actions.Where(e => e.FilePath != input.FileTree.FilePath).ToList();
        previouslyRetrieved.ForEach(key => _actions.Remove(key));

        _actions.AddRange(actionsInFile.Select(action => (ApiGenerator.GetRouteFromTemplate(action).Route, action, input.FileTree.FilePath)));

        var entityContextVariables = input.FileTree.GetRoot().DescendantNodes()
            .Select(e =>
                e is FieldDeclarationSyntax { Declaration.Type: IdentifierNameSyntax { Identifier.Text: var fieldType } } field &&
                _dbContextTypes.Contains(fieldType)
                    ? field.Declaration.Variables[0].Identifier.Text
                    : e is PropertyDeclarationSyntax { Type: IdentifierNameSyntax { Identifier.Text: var propType } } prop && _dbContextTypes.Contains(propType)
                        ? prop.Identifier.Text
                        : null).ToList();

        _entitiesUsageInActions = _actions.Select(e => (e.ActionSymbol.Name, DbSet: GetAccessedDbSet(e.ActionSymbol, entityContextVariables)))
            .Where(e => e.DbSet != null)
            .GroupBy(e => e.DbSet, e => e.Name).ToList()
            .ToDictionary(group => group.Key, group => group.ToArray());
    }
    

    private static SyntaxNode FindNode((string DbSetName, ISymbol Symbol) entityType)
    {
        return entityType.Symbol.DeclaringSyntaxReferences.First().SyntaxTree.GetRoot();
    }

    /// <summary>
    /// Gets entity types that are used as navigation properties of <see cref="entityType"/>. 
    /// </summary>
    private static IEnumerable<string> GetTypesOfNavigationProperties((string DbSetName, ISymbol Symbol) entityType, List<string> namesOfEntities)
    {
        return FindNode(entityType).DescendantNodes().OfType<PropertyDeclarationSyntax>().Select(e =>
            e.Type is IdentifierNameSyntax { Identifier.Text: var identifier } && namesOfEntities.Contains(identifier) ? identifier :
            e.Type is GenericNameSyntax { TypeArgumentList.Arguments: [IdentifierNameSyntax { Identifier.Text: var collectionIdentifier }] } &&
            namesOfEntities.Contains(collectionIdentifier) ? collectionIdentifier : null).Where(e => e != null);
    }

    private static string GetAccessedDbSet(IMethodSymbol action, List<string> contextVariables) => action.DeclaringSyntaxReferences[0].GetSyntax()
        .DescendantNodes().Select(e =>
            e is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: var variable }, Name.Identifier.Text: var name } &&
            contextVariables.Contains(variable)
                ? name
                : null).FirstOrDefault(e => e != null);
}