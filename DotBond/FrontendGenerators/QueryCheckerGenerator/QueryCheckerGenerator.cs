using DotBond.Misc;
using DotBond.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.Generators.QueryCheckerGenerator;

public class QueryCheckerGenerator : AbstractGenerator
{
    private readonly Compilation _compilation;
    private Dictionary<string, string[]> _entityTypeRelationships;
    private readonly List<string> _dbContextTypes = new();
    private readonly Dictionary<string, (string PrimaryEntityType, string[] SecondaryEntityTypes)> _actions = new();
    private readonly string _executionRulesPath = Path.Combine(ApiGenerator.MainApiDirectory, "execution-rules.ts");

    public QueryCheckerGenerator(string assemblyName, Compilation compilation) : base(assemblyName)
    {
        _compilation = compilation;
    }

    public override HashSet<TypeSymbolLocation> GetControllerCallback(FileAnalysisCallbackInput input)
    {
        var dbContextInFile = input.FileTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(e => e.BaseList?.Types.Any(baseType => baseType.Type is IdentifierNameSyntax { Identifier.Text: "DbContext" }) ?? false);

        if (dbContextInFile != null)
            GenerateRulesOnChangeInDbContext(input, dbContextInFile);

        var actionsInFile = ApiGenerator.RetrieveActionsFromController(input.FileTree, input.SemanticModel);
        if (actionsInFile.Any())
            CreateExecutionRulesFile(GetRelationshipsOfActions(GetEntityUsagesInActionsFromSolution(actionsInFile, input.SemanticModel)).ToList());
        
        return null;
    }

    private void GenerateRulesOnChangeInDbContext(FileAnalysisCallbackInput input, ClassDeclarationSyntax dbContextInFile)
    {
        RetrieveEntityRelationships(input, dbContextInFile);
        if (_entityTypeRelationships == null || !_entityTypeRelationships.Any());

        _actions.Clear();

        var filesWithActions = _compilation.SyntaxTrees
            .Where(e => e.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Any(@class => @class.Identifier.Text.EndsWith("Controller")))
            .Select(e => (FileTree: e, e.FilePath, SemanticModel: _compilation.GetSemanticModel(e)))
            .Select(e => (ActionsInFile: ApiGenerator.RetrieveActionsFromController(e.FileTree, e.SemanticModel), e.FilePath, e.SemanticModel))
            .Where(e => e.ActionsInFile.Any())
            .ToArray();

        foreach (var (actionsInFile, filePath, semanticModel) in filesWithActions[..^1])
            GetRelationshipsOfActions(GetEntityUsagesInActionsFromSolution(actionsInFile, semanticModel));

        CreateExecutionRulesFile(GetRelationshipsOfActions(GetEntityUsagesInActionsFromSolution(filesWithActions[^1].ActionsInFile,
            filesWithActions[^1].SemanticModel)).ToList());
    }

    public override HashSet<TypeSymbolLocation> DeleteSource(string filePath)
    {
        return null;
    }

    /*========================== Private API ==========================*/

    /// <summary>
    /// Currently doesn't inform correctly about the type of relationship.
    /// </summary>
    /// <returns></returns>
    private IEnumerable<(string ActionA, string ActionB, ActionRelatioship relatioshipType)> GetRelationshipsOfActions(
        Dictionary<string, (string PrimaryEntityType, string[] SecondaryEntityTypes)> actionsEntityUsage)
    {
        var primaryEntityTypeUsageLookup = actionsEntityUsage?.Where(e => e.Value.PrimaryEntityType != null).GroupBy(e => e.Value.PrimaryEntityType, e => e.Key)
            .ToDictionary(e => e.Key, e => e);

        return actionsEntityUsage?.Where(e => e.Value.PrimaryEntityType != null).Where(e => e.Value.SecondaryEntityTypes.Any()).SelectMany(e =>
            e.Value.SecondaryEntityTypes.SelectMany(entityType =>
                primaryEntityTypeUsageLookup.ContainsKey(entityType)
                    ? primaryEntityTypeUsageLookup[entityType].Select(actionB => (e.Key, actionB, ActionRelatioship.OneToOne))
                    : Enumerable.Empty<(string, string, ActionRelatioship)>()));
    }

    private enum ActionRelatioship
    {
        OneToOne,
        OneToMany,
        ManyToMany
    }


    /// <summary>
    /// Finds all db sets from the files, and reads their relationships using navigation properties.
    /// </summary>
    private void RetrieveEntityRelationships(FileAnalysisCallbackInput input, ClassDeclarationSyntax dbContextInFile)
    {
        var entitiesInContext = dbContextInFile.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Select(prop => prop is { Type: GenericNameSyntax { Identifier.Text: "DbSet", TypeArgumentList.Arguments: [var entityType] } }
                ? input.SemanticModel.GetSymbolInfo(entityType).Symbol
                : default)
            .Where(e => e != default).ToList();

        _entityTypeRelationships = entitiesInContext.ToDictionary(entityType => entityType.Name, entityType =>
            GetTypesOfNavigationProperties(entityType, entitiesInContext.Select(e => e.Name).ToList()).Distinct().ToArray());

        _dbContextTypes.Add(dbContextInFile.Identifier.Text);
    }

    /// <summary>
    /// Forms the new actions array
    /// which is used to translate entities' relationships into actions' relationships.
    /// </summary>
    private Dictionary<string, (string PrimaryEntityType, string[] SecondaryEntityTypes)> GetEntityUsagesInActionsFromSolution(
        List<IMethodSymbol> actionsInFile, SemanticModel semanticModel)
    {
        var actionNamesInFile = actionsInFile.Select(e => e.Name).ToList();
        var existingActionsToUpdate = _actions.Where(e => actionNamesInFile.Contains(e.Key)).Select(e => e.Key).ToList();
        existingActionsToUpdate.ForEach(key => _actions.Remove(key));

        foreach (var (key, entityTypes) in actionsInFile.Select(e => (e.Name, EntityTypes: GetUsedEntityTypes(e, semanticModel))).Where(e => e.EntityTypes != default))
            _actions.Add(key, entityTypes);

        return _actions;
    }


    private static SyntaxNode FindNode(ISymbol entityType)
    {
        return entityType.DeclaringSyntaxReferences.First().SyntaxTree.GetRoot();
    }

    /// <summary>
    /// Gets entity types that are used as navigation properties of <see cref="entityType"/>. 
    /// </summary>
    private static IEnumerable<string> GetTypesOfNavigationProperties(ISymbol entityType, List<string> namesOfEntities)
    {
        return FindNode(entityType).DescendantNodes().OfType<PropertyDeclarationSyntax>().Select(e =>
            e.Type is IdentifierNameSyntax { Identifier.Text: var identifier } && namesOfEntities.Contains(identifier) ? identifier :
            e.Type is GenericNameSyntax { TypeArgumentList.Arguments: [IdentifierNameSyntax { Identifier.Text: var collectionIdentifier }] } &&
            namesOfEntities.Contains(collectionIdentifier) ? collectionIdentifier : null).Where(e => e != null);
    }

    private (string PrimaryEntityType, string[] SecondaryEntityTypes) GetUsedEntityTypes(IMethodSymbol action, SemanticModel semanticModel)
    {
        var actionSyntax = action.DeclaringSyntaxReferences[0].GetSyntax();

        var contextVariable = action.DeclaringSyntaxReferences[0].SyntaxTree.GetRoot().DescendantNodes()
            .Select(e =>
                e is FieldDeclarationSyntax { Declaration.Type: IdentifierNameSyntax { Identifier.Text: var fieldType } } field &&
                _dbContextTypes.Contains(fieldType)
                    ? field.Declaration.Variables[0].Identifier.Text
                    : e is PropertyDeclarationSyntax { Type: IdentifierNameSyntax { Identifier.Text: var propType } } prop && _dbContextTypes.Contains(propType)
                        ? prop.Identifier.Text
                        : null).FirstOrDefault(e => actionSyntax.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.Text == e));

        if (contextVariable == null)
        {
            var invocationsOfSiblingActions = actionSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(e => (IMethodSymbol)semanticModel.GetSymbolInfo(e).Symbol)
                .Where(invocationSymbol => invocationSymbol.ContainingSymbol.Name == action.ContainingSymbol.Name).ToList();

            if (invocationsOfSiblingActions.Count == 0) return default;
            if (invocationsOfSiblingActions.Count == 1) return GetUsedEntityTypes(invocationsOfSiblingActions[0], semanticModel);

            var collectionTypes = new[] { "List", "IEnumerable", "IQueryable" };
            var primaryInvocation = invocationsOfSiblingActions.FirstOrDefault(e =>
                e.ReturnType is INamedTypeSymbol { Name: var nonGenericName } && collectionTypes.Contains(nonGenericName)
                || e.ReturnType is INamedTypeSymbol { TypeArguments: [{ Name: var genericName }] } && collectionTypes.Contains(genericName));

            var usedEntities = invocationsOfSiblingActions.Select(invocation => GetUsedEntityTypes(invocation, semanticModel)).ToList();
            var primaryInvocationIdx = primaryInvocation != null ? invocationsOfSiblingActions.IndexOf(primaryInvocation) : 0;

            return (usedEntities[primaryInvocationIdx].PrimaryEntityType,
                usedEntities.SelectMany((e, idx) => idx != primaryInvocationIdx ? e.SecondaryEntityTypes.Append(e.PrimaryEntityType) : e.SecondaryEntityTypes)
                    .ToArray());
        }

        var primaryEntityType = actionSyntax
            .DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Select(e => RetrieveEntityTypeAccessedFromContextVariable(e, contextVariable, semanticModel)).FirstOrDefault(e => e != null);

        var relationships = _entityTypeRelationships[primaryEntityType];

        var secondaryEntityTypes = actionSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .SelectMany(invocation => invocation switch
            {
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Select" or "Include" },
                    ArgumentList.Arguments: [{ Expression: var lambda }]
                } => lambda.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Select(access =>
                    access is { Expression: var expression, Name: var name } && semanticModel.GetTypeInfo(expression).Type?.Name == primaryEntityType &&
                    semanticModel.GetTypeInfo(name).Type is INamedTypeSymbol nameType
                        ? nameType is { TypeArguments.Length: 0 } nonGeneric && relationships.Contains(nonGeneric.Name) ? nonGeneric.Name
                        : nameType is { TypeArguments: [var generic] } && relationships.Contains(generic.Name) ? generic.Name
                        : null
                        : null).Where(e => e != null),
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Join" },
                    ArgumentList.Arguments: [{ Expression: var entitySet }, ..]
                } => entitySet.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Select(e => RetrieveEntityTypeAccessedFromContextVariable(e, contextVariable, semanticModel)).Where(e => e != null).Take(1),
                _ => Enumerable.Empty<string>()
            }).Distinct().ToArray();

        return (primaryEntityType, secondaryEntityTypes);
    }

    private void CreateExecutionRulesFile(IReadOnlyCollection<(string ActionA, string ActionB, ActionRelatioship relatioshipType)> relationships)
    {
        if (relationships == null) return;
        var actionsToGenerate = relationships.Select(e => e.ActionA).Concat(relationships.Select(e => e.ActionB)).Distinct().ToList();

        var queryInterfaces = string.Join("\n", actionsToGenerate
            .Select((name, idx) => $$"""
                export interface {{name}}<Depth extends number = 0> {
                    0: -1 extends Depth ? true : false;
                    {{idx + 1}}: Depth
                }
            """));

        var runOnClientWhen = $$"""
        type RunOnClientWhen = {
            {{string.Join(",\n", relationships.GroupBy(e => e.ActionA, e => e.ActionB).Select(group => $"'{group.Key}': [{string.Join(", ", group)}]"))}}
        };
        """;

        var result = $$"""
        import {Increment, IntRange} from "./library/utilities";

        export type ExecutionInsights = {
            {{string.Join("\n", actionsToGenerate.Select(e => $"{e}: {e},"))}}
        } & {
            [key in Exclude<string, {{string.Join(" | ", actionsToGenerate.Select(e => $"'{e}'"))}}>]: void
        }

        {{queryInterfaces}}

        export type SuperInterface<Depth extends number = 0> = {
            [key in IntRange<1, 20>]?: Depth;
        } & {
            0: -1 extends Depth ? true : false;
        }

        type GetQuery<Query, Depth extends number> = {{string.Join("\n:", actionsToGenerate.Select(e => $"Query extends {e}<any>\n? {e}<Depth>"))}}
                    : never;

        type GetQueryName<Query> = {{string.Join("\n:", actionsToGenerate.Select(e => $"Query extends {e}<any>\n? '{e}'"))}}
                    : never;

        export type ClientSide = { _ };

        {{runOnClientWhen}}

        export type CombineQuery<TFirst, TSecondary = 0> = TSecondary extends (ClientSide | RunOnClientWhen[GetQueryName<TFirst>][number])
            ? ClientSide
            : TSecondary extends SuperInterface<infer Depth>
                ? GetQuery<TSecondary, Increment<Depth>>
                : never;
        """;

        // Write rules to file
        FrontendDirectoryController.WriteToAngularDirectory(_executionRulesPath, result);
    }
    
    /// <summary>
    /// db.Movies => Movie
    /// </summary>
    private static string
        RetrieveEntityTypeAccessedFromContextVariable(MemberAccessExpressionSyntax expression, string contextVariable, SemanticModel semanticModel) =>
        expression is { Expression: IdentifierNameSyntax { Identifier.Text: var variable }, Name: var name } &&
        contextVariable == variable
            ? ((INamedTypeSymbol)semanticModel.GetTypeInfo(name).Type).TypeArguments.First().Name
            : null;
}