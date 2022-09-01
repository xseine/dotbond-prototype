using System.Text;
using System.Text.RegularExpressions;
using ConsoleApp1.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.Misc;
using Translator.Workspace;

namespace Translator.Generators;

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


    public ApiGenerator(string assemblyName) : base(assemblyName)
    {
    }

    /// <summary>
    /// Gets action symbols from controllers. Used for generating Action enums along with paramater and return types.
    /// </summary>
    public override HashSet<TypeSymbolLocation> GetControllerCallback(FileAnalysisCallbackInput input)
    {
        var actionSymbols = GetActionsFromController(input.FileTree, input.SemanticModel);

        if (!actionSymbols.Any()) return null;

        var filePath = input.FileTree.FilePath;
        var keysToRemove = _actions.Where(e => e.Value.FilePath == filePath).Select(e => e.Key).ToList();
        keysToRemove.ForEach(key => _actions.Remove(key));

        foreach (var actionSymbol in actionSymbols.Cast<IMethodSymbol>())
        {
            // 1) Get parameter types
            // TODO: provjeriti ovo
            var parameterTypes = actionSymbol.Parameters.SelectMany(p => p.Type is INamedTypeSymbol { IsGenericType: true } type ? type.TypeArguments.ToArray() : new[] { p.Type }).ToList();

            // 2) Get return type
            var returnType = actionSymbol.ReturnType is INamedTypeSymbol { IsGenericType: true, Name: "ActionResult" } namedTypeSymbol ?
                namedTypeSymbol.TypeArguments.First() :
                actionSymbol.ReturnType;

            // 3) Get HTTP Method
            var httpMethod = actionSymbol.GetAttributes().Any(attribute => attribute.AttributeClass is { Name: "HttpPostAttribute" }) ? "POST" : "GET";

            if (filePath == null)
                throw new Exception("Check it out. You need filepath for translationrecord");

            var (route, usesSimpleRoute) = GetRouteFromTemplate(actionSymbol, httpMethod);

            // 4) Register action
            _actions[route] = (actionSymbol.Parameters.Select(p => (p.Name, p.Type, GetBindingAttribute(p))).ToList(), returnType, httpMethod, filePath, usesSimpleRoute);

            // 5) Get types (if List, use generic parameter)
            // typesInFile.AddRange(parameterTypes.Concat(returnType is INamedTypeSymbol { IsGenericType: true } type ? type.TypeArguments.ToArray() : new[] { returnType })
            //     .Select(symbol => symbol is INamedTypeSymbol { IsGenericType: true, Name: "List" or "IList" or "IEnumerable" or "ICollection" } namedTypeSymbol ?
            //         namedTypeSymbol.TypeArguments.First() :
            //         symbol)
            //     .Where(IsReferenceTypeForTranslation).ToList());
        }

        CreateDefinitionsFile();
        CreateBackendEndpointsServiceFile();
        CreateReturnTypeDatesForConversionFile();

        // Note: Logging these translation is an unnecessary complication 

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
    /// Gets types (and type arguments) of parameters and the return type for each action.
    /// </summary>
    public HashSet<TypeSymbolLocation> GetUsedTypes()
    {
        return _actions.Values
            .SelectMany(action => action.Parameters.Select(p => p.Type)
                .Concat(action.ReturnType is INamedTypeSymbol { IsGenericType: true } type ? type.TypeArguments.ToArray() : new[] { action.ReturnType })
                .Select(symbol => symbol is INamedTypeSymbol { IsGenericType: true, Name: "List" or "IList" or "IEnumerable" or "ICollection" } namedTypeSymbol ?
                    namedTypeSymbol.TypeArguments.First() :
                    symbol)
                .Where(IsReferenceTypeForTranslation)
                .Select(type => type.GetLocation())).ToHashSet();
    }

    /// <summary>
    /// Actions = public methods with a non-void return type
    /// </summary>
    public static List<IMethodSymbol> GetActionsFromController(SyntaxTree syntaxTree, SemanticModel semanticModel)
    {
        return syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Select(classSyntax => (INamedTypeSymbol)ModelExtensions.GetDeclaredSymbol(semanticModel, classSyntax)!)
            .Where(RoslynUtilities.InheritsFromController)
            .SelectMany(controllerSymbol =>
                controllerSymbol.GetMembers().Where(symbol => symbol is IMethodSymbol { DeclaredAccessibility: Accessibility.Public, ReturnsVoid: false }))
            .Cast<IMethodSymbol>()
            .ToList();
    }
    /*========================== Private API ==========================*/

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
                .SelectMany(e => e.Select(ee => (ee, e.Key))));

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
                var symbol = returnTypeSymbol is INamedTypeSymbol { IsGenericType: true, Name: "ActionResult" } namedTypeSymbol ?
                    namedTypeSymbol.TypeArguments.First() :
                    returnTypeSymbol;

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


    private static (string, bool) GetRouteFromTemplate(IMethodSymbol actionSymbol, string httpMethod)
    {
        var isNameUsedInRoute = ((INamedTypeSymbol)actionSymbol.ContainingSymbol).GetAttributes()
            .All(e => e.AttributeClass is not { Name: "RouteAttribute" } || !e.ConstructorArguments.First().Value!.Equals("[controller]"));

        var secondPartOfRoute = isNameUsedInRoute ? actionSymbol.Name : httpMethod[0] + httpMethod[1..].ToLower();

        var fullRoute = actionSymbol.ContainingSymbol.Name + "/" + secondPartOfRoute;
        return (fullRoute, !isNameUsedInRoute);
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
            var result = TypeTranslation.ParseType(SyntaxFactory.ParseTypeName(RemoveNamespace(type.ToString())), type);
            if (result.EndsWith(" | null")) result = result[..^" | null".Length];
            return result;
        }
        
        return string.Join(", ",
            parameters.Select(p =>
                $"{(p.BindingAttribute != null ? $"@{LowerCaseWord(p.BindingAttribute)} " : null)}{p.Name}{(p.Type.NullableAnnotation == NullableAnnotation.Annotated ? "?" : null)}: {GetNonNullableType(p.Type)}"));
    }
    
    private static string CreateReturnExpression(ITypeSymbol returnType) => returnType.Name == "IActionResult" ?
        "any" :
        TypeTranslation.ParseType(
            SyntaxFactory.ParseTypeName(RemoveNamespace(returnType.ToString())), returnType);

    private static bool DoesReturnView(SyntaxNode methodSyntax) =>
        methodSyntax.DescendantNodes().OfType<ReturnStatementSyntax>().Any(returnSyntax => returnSyntax.Expression is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "View" or "PartialView" }
        });
}