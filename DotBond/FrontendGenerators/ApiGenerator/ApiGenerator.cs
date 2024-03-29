﻿using System.Text;
using System.Text.RegularExpressions;
using ConsoleApp1.Common;
using DotBond.Misc;
using DotBond.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.Generators;

/// <summary>
/// Used to generate controllers' and actions' definition file in Typescript.
/// </summary>
public sealed class ApiGenerator : AbstractGenerator
{
    private readonly Dictionary<string, (List<(string Name, ITypeSymbol Type, string BindAttribute)> Parameters, ITypeSymbol ReturnType, string HttpMethod, string FilePath, bool usesSimpleRoute)>
        _actions = new();

    public static string MainApiDirectory = "Actions";
    private readonly string _controllerDefinitionsPath = Path.Combine(MainApiDirectory, "controller-definitions.ts");
    private readonly string _backendEndpointsServicePath = Path.Combine(MainApiDirectory, "base-endpoints.service.ts");
    private readonly string _returnTypeDatesPath = Path.Combine(MainApiDirectory, "return-type-dates.ts");
    public static string QueryServicePath = Path.Combine(MainApiDirectory, "query.service.ts");
    public const string ControllerImportSuffix = "Definitions";

    public ApiGenerator(string assemblyName) : base(assemblyName)
    {
    }

    /// <summary>
    /// Gets action symbols from controllers. Used for generating Action enums along with paramater and return types.
    /// </summary>
    public override HashSet<TypeSymbolLocation> GetControllerCallback(FileAnalysisCallbackInput input)
    {
        var actionSymbols = RetrieveActionsFromController(input.FileTree, input.SemanticModel);

        if (!actionSymbols.Any()) return null;

        var filePath = input.FileTree.FilePath;
        if (filePath == null) throw new Exception("Check it out. You need filepath for translationrecord");
        
        var previouslyRetrieved = _actions.Where(e => e.Value.FilePath == filePath).Select(e => e.Key).ToList();
        previouslyRetrieved.ForEach(key => _actions.Remove(key));

        foreach (var actionSymbol in actionSymbols.Cast<IMethodSymbol>())
        {
            var parameters = actionSymbol.Parameters.Select(p => (p.Name, p.Type, GetBindingAttribute(p))).ToList();
            var returnType = FindReturnTypeToUse(actionSymbol.ReturnType);
            var httpMethod = GetActionMethod(actionSymbol);
            var (route, usesSimpleRoute) = GetRouteFromTemplate(actionSymbol);
            _actions[route] = (parameters, returnType, httpMethod, filePath, usesSimpleRoute);
        }

        CreateDefinitionsFile();
        CreateBackendEndpointsServiceFile();
        CreateReturnTypeDatesForConversionFile();

        // Note: Logging these translations is an unnecessary complication 

        return GetUsedTypes();
    }

    public override HashSet<TypeSymbolLocation> DeleteSource(string filePath)
    {
        var keysToRemove = _actions.Where(e => e.Value.FilePath == filePath).Select(e => e.Key).ToList();
        if (!keysToRemove.Any()) return null;

        keysToRemove.ForEach(key => _actions.Remove(key));

        CreateDefinitionsFile();
        CreateBackendEndpointsServiceFile();
        CreateReturnTypeDatesForConversionFile();
        
        return GetUsedTypes();
    }

    /// <summary>
    /// Actions = public methods with a non-void return type
    /// </summary>
    public static List<IMethodSymbol> RetrieveActionsFromController(SyntaxTree syntaxTree, SemanticModel semanticModel)
    {
        return syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Select(classSyntax => (INamedTypeSymbol)ModelExtensions.GetDeclaredSymbol(semanticModel, classSyntax)!)
            .Where(RoslynUtilities.InheritsFromController)
            .SelectMany(controllerSymbol =>
                controllerSymbol.GetMembers().Where(symbol => symbol is IMethodSymbol { DeclaredAccessibility: Accessibility.Public, ReturnsVoid: false }))
            .Cast<IMethodSymbol>()
            .ToList();
    }
    
    public static (string Route, bool UsesSimpleRoute) GetRouteFromTemplate(IMethodSymbol actionSymbol)
    {
        var isNameUsedInRoute = ((INamedTypeSymbol)actionSymbol.ContainingSymbol).GetAttributes()
            .All(e => e.AttributeClass is not { Name: "RouteAttribute" } || !e.ConstructorArguments.First().Value!.Equals("[controller]"));

        var httpMethod = GetActionMethod(actionSymbol);
        var secondPartOfRoute = isNameUsedInRoute ? actionSymbol.Name : httpMethod[0] + httpMethod[1..].ToLower();

        var fullRoute = actionSymbol.ContainingSymbol.Name + "/" + secondPartOfRoute;
        return (fullRoute, !isNameUsedInRoute);
    }
    
    /*========================== Private API ==========================*/

    /// <summary>
    /// Gets types (and type arguments) of parameters and the return type for each action.
    /// </summary>
    private HashSet<TypeSymbolLocation> GetUsedTypes()
    {
        return _actions.Values
            .SelectMany(action =>
            {
                try
                {
                    return action.Parameters.Select(p => p.Type)
                        .Concat(action.ReturnType is INamedTypeSymbol { IsGenericType: true } type ? type.TypeArguments.ToArray() : new[] { action.ReturnType })
                        .Select(symbol => symbol is INamedTypeSymbol { IsGenericType: true, Name: "List" or "IList" or "IEnumerable" or "ICollection" } namedTypeSymbol
                            ? namedTypeSymbol.TypeArguments.First()
                            : symbol)
                        .Where(IsReferenceTypeForTranslation)
                        .Select(type => type.GetLocation()).ToList();
                }
                catch
                {
                    return new List<TypeSymbolLocation>();
                }
            }).ToHashSet();
    }
    
    /// <summary>
    /// Generate types for names, parameters and responses of actions.
    /// Also generates the required imports.
    /// </summary>
    private void CreateDefinitionsFile()
    {
        var usedTypes = GetUsedTypes();

        // Imports
        var importStatementsText = FrontendDirectoryController.GenerateImportStatementsForFile(
            FrontendDirectoryController.DetermineAngularPath(_controllerDefinitionsPath),
            usedTypes.GroupBy(type => type.FilePath, group => group.FullName[(group.FullName.LastIndexOf(".") + 1)..])
                .SelectMany(e => e.Select(ee => (SymbolName: ee, SymbolPath: e.Key)))
                // Add "Defini
                .Select(e => e.SymbolName.EndsWith("Controller") ? (e.SymbolName + ControllerImportSuffix, e.SymbolPath) : e)
            );

        importStatementsText += @"
import {Observable} from ""rxjs"";
import {method, fromBody, fromUri} from ""./library/miscellaneous"";
";

        // Controller & actions definitions
        var controllersWithActions = _actions.Select(e => e.Key.Split("/")).GroupBy(e => e[0],
            (controllerName, values) => (controllerName, values.Select(e => e[1])));

        var definitions = "";

        foreach (var (controllerName, actions) in controllersWithActions)
        {
            definitions += $@"
export class {controllerName} {{
{string.Join("", actions.Select(actionName => {
    var (parameters, returnType, httpMethod, _, usesSimpleRoute) = _actions[controllerName + "/" + actionName];
    var parametersString = CreateParametersList(parameters);
    var returnString = CreateReturnExpression(returnType);

    return $@"
    @method('{httpMethod}'{(usesSimpleRoute ? ", true" : null)})
    {actionName}({parametersString}): Observable<{returnString}> {{
        return [{string.Join(", ", parameters.Select(e => $"'{e.Name}'"))}] as any;
    }}
"; }))}
}}
";
        }

        var autogeneratedText = Utilities.GetAutogeneratedText(@"Provides definition of backend controllers and actions.
Actions body should be overriden by the implementing http service.");
        
        // Apply controller suffix to types defined in a controller
        definitions = Regex.Replace(definitions, @"(?<=( |<)\w+)Controller(?=\.\w+)", _ => "Controller" + ControllerImportSuffix);

        // Write definitions to file
        FrontendDirectoryController.WriteToAngularDirectory(_controllerDefinitionsPath, autogeneratedText + "\n" + importStatementsText + "\n" + definitions);
    }

    private void CreateBackendEndpointsServiceFile()
    {
        var controllersNames = _actions.Select(e => e.Key.Split("/")[0]).Distinct().ToList();
        var content = ApiBoilerplate.GenerateBackendHttpEndpointsServiceContent(controllersNames);

        FrontendDirectoryController.WriteToAngularDirectory(_backendEndpointsServicePath, content);
    }

    /// <summary>
    /// List of fields of a specific action retunr type that will need to be converted to JS Date object.
    /// </summary>
    private void CreateReturnTypeDatesForConversionFile()
    {
        var fileContentSb = new StringBuilder(@"
export const dateFieldsInReturnTypes: {[key: string]: {[key2: string]: string[]}} = {
");
        foreach (var (controller, actionReturnTypes) in _actions.Select(e => (e.Key.Split("/")[0], e.Key.Split("/")[1], returnType: e.Value.ReturnType))
                     .GroupBy(e => e.Item1, tuple => (tuple.Item2, tuple.returnType)).Select(e => (e.Key, e.AsEnumerable())))
        {
            fileContentSb.Append($"\t'{controller[..^"Controller".Length]}': {{");

            foreach (var (action, returnTypeSymbol) in actionReturnTypes)
            {
                var symbol = FindReturnTypeToUse(returnTypeSymbol);

                symbol = symbol is INamedTypeSymbol { IsGenericType: true, Name: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable" } collectionSymbol ?
                    collectionSymbol.TypeArguments.First() :
                    symbol;

                var typeSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
                if (typeSyntax == null) continue;

                var dates = typeSyntax.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
                    .Where(prop => prop.Type is IdentifierNameSyntax identifierNameSyntax && identifierNameSyntax.Identifier.Text.StartsWith(nameof(DateTime)))
                    .Select(e => e.Identifier.Text[0].ToString().ToLower() + e.Identifier.Text[1..]).ToList();

                if (!dates.Any()) continue;

                fileContentSb.Append($"'{action}': [{string.Join(", ", dates.Select(e => $"'{e}'"))}], ");
            }


            fileContentSb.Append("},\n");
        }

        fileContentSb.Append("};");

        FrontendDirectoryController.WriteToAngularDirectory(_returnTypeDatesPath, fileContentSb.ToString());
    }

    private static string GetBindingAttribute(IParameterSymbol p)
    {
        return p.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "FromBodyAttribute" or "FromUri" })?.AttributeClass?.Name[..^"Attribute".Length];
    }

    private static string CreateParametersList(List<(string Name, ITypeSymbol Type, string BindingAttribute)> parameters)
    {
        if (parameters.Count == 0) return null;

        string LowerCaseWord(string word) => word[0].ToString().ToLower() + word[1..];

        string GetNonNullableType(ITypeSymbol type)
        {
            var result = TypeTranslation.ParseType(type);
            if (result.EndsWith(" | null")) result = result[..^" | null".Length];
            return result;
        }
        
        return string.Join(", ",
            parameters.Select(p =>
                $"{(p.BindingAttribute != null ? $"@{LowerCaseWord(p.BindingAttribute)} " : null)}{p.Name}{(p.Type.NullableAnnotation == NullableAnnotation.Annotated ? "?" : null)}: {GetNonNullableType(p.Type)}"));
    }
    
    // TypeTranslation.GetContainingTypesPath(returnType) is string a ? a + "." + returnType.Name : returnType.Name
    
    private static string CreateReturnExpression(ITypeSymbol returnType) => returnType.Name == "IActionResult" ?
        "any" :
        TypeTranslation.ParseType(returnType);


    private static ITypeSymbol FindReturnTypeToUse(ITypeSymbol returnTypeSymbol)
    {
        return returnTypeSymbol is INamedTypeSymbol { IsGenericType: true, Name: "ActionResult" } actionResultSymbol
            ? actionResultSymbol.TypeArguments.First()
            : returnTypeSymbol is INamedTypeSymbol { IsGenericType: true, Name: "Task" } taskSymbol
                ? taskSymbol.TypeArguments.First() is INamedTypeSymbol { IsGenericType: true, Name: "ActionResult" } taskActionResultSymbol
                    ? taskActionResultSymbol.TypeArguments.First()
                    : taskSymbol.TypeArguments.First()
                : returnTypeSymbol;
    }

    private static string GetActionMethod(IMethodSymbol actionSymbol) => actionSymbol.GetAttributes().Any(attribute => attribute.AttributeClass is { Name: "HttpPostAttribute" }) ? "POST" : "GET";
}