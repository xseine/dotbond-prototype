using System.Text.RegularExpressions;
using DotBond.Misc.Exceptions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.IntegratedQueryRuntime;

public static class EndpointGenUtilities
{
    /// <summary>
    /// Gets types and names of services that are injected into specific controllers.
    /// </summary>
    public static (List<(string ControllerName, IEnumerable<(string Name, string Type)> Injected)>, List<(string Name, string Type)>) GetInjectedServices(
        IEnumerable<SyntaxTree> containingSyntaxTrees,
        IEnumerable<string> controllerNames
    )
    {
        var controllersSource = string.Join("\n", containingSyntaxTrees);
        var splitNameAndTypeRx = new Regex(@"(?<type>\w+\s*(<(([^<^>]*(<|>)[^<^>]*(<|>))*?|([^<^>]+))>)*)\s+(?<name>\w+)\s*(,|$)");
        var controllerInjections = controllerNames.Select(controllerName => (controllerName, rx: new Regex(@$"public\s*{controllerName}\s*\((?<injected>[^)]*)\)")))
            .Select(nameAndRx => (ControllerName: nameAndRx.controllerName, Injected:
                splitNameAndTypeRx
                    .Matches(nameAndRx.rx.Match(controllersSource).Groups["injected"].Value)
                    .Select(e => (Name: e.Groups["name"].Value, Type: e.Groups["type"].Value)))).ToList(); //.ToDictionary(e => e.name, e => e.injected);

        // Since differenct controllers can inject same services under different names
        var distinctServiceInjections = controllerInjections
            .SelectMany(e => e.Injected)
            .DistinctBy(e => e.Type).ToList();

        controllerInjections = controllerInjections.Select(e => e with { Injected = distinctServiceInjections.Where(ee => e.Injected.Any(eee => eee.Type == ee.Type)) }).ToList();

        return (controllerInjections, distinctServiceInjections);
    }


    public static string CreateRecord(string name, List<string> namedTypes, List<string> objectValueTypeFields = null, IEnumerable<string> recordTranslations = null)
    {
        var indent = Regex.Matches(name, @"Record\d").Count - 1;
        var isOnlyOneField = namedTypes.Count + objectValueTypeFields?.Count == 1;

        var record = $@"public record {name}({string.Join(", ", namedTypes)})
{{
    {(objectValueTypeFields != null ? string.Join("\n\t", objectValueTypeFields.Select(e => $"public {e} = null!;")) : null)}
    {(recordTranslations != null ? string.Join("\n\t", recordTranslations) : null)}
    
    {(indent == 0 ? $@"public void Deconstruct({string.Join(", ", namedTypes.Concat(objectValueTypeFields ?? new()).Select(e => $"out {e.Split(" ")[0]} {e.Split(" ")[1].ToLower()}"))}{(isOnlyOneField ? ", out object _" : null)})
    {{
        {string.Join("\n\t\t", namedTypes.Concat(objectValueTypeFields ?? new()).Select(e => $"{e.Split(" ")[1].ToLower()} = {e.Split(" ")[1]};"))}
        {(isOnlyOneField ? "_ = new object();" : null)}
    }}" : null)}
}}";

        if (indent > 0)
            record = record.Replace("\n", "\n" + string.Join("", Enumerable.Repeat("\t", indent)));

        // Trim empty bodies
        record = Regex.Replace(record, @"(?<=\{)\s+(?=\})", "");

        return record;
    }

    /// <summary>
    /// Gets information about the provided action.
    /// </summary>
    /// <param name="action"></param>
    /// <param name="controller"></param>
    /// <param name="containingSyntaxTrees">List of syntax tree's where definitions of all actions are stored.</param>
    /// <param name="compilation">Used for getting type's symbol information.</param>
    /// <returns></returns>
    public static (ITypeSymbol actionReturnTypeSymbol, bool isCollection, bool isTask, bool isActionResult) GetActionReturnType
        (string action, string controller, List<SyntaxTree> containingSyntaxTrees, ref Compilation compilation)
    {
        var tree = containingSyntaxTrees.FirstOrDefault(tree =>
        {
            var treeText = tree.GetText().ToString();
            return treeText.Contains("class " + controller + "Controller") && treeText.Contains(action);
        });

        if (tree == null)
            throw new MissingDefinitionException($"Missing controller: \"{controller}\", or its actions: \"{action}\".");

        var semanticModel = compilation.GetSemanticModel(tree);
        var actionDeclarationSyntax = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Where(classDeclaration => classDeclaration.Identifier.Text == controller + "Controller")
            .Select(controllerDeclaration => controllerDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(methodDeclaration => methodDeclaration.Identifier.Text == action))
            .First(actionDeclaration => actionDeclaration != null);

        var actionReturnTypeSymbol = (ModelExtensions.GetDeclaredSymbol(semanticModel, actionDeclarationSyntax) as IMethodSymbol)!.ReturnType;
        var isCollection = actionReturnTypeSymbol is INamedTypeSymbol { Name: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable" } or INamedTypeSymbol
        {
            Name: "ActionResult", TypeArguments: [INamedTypeSymbol { Name: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable" }]
        } or INamedTypeSymbol
        {
            Name: "Task", TypeArguments: [INamedTypeSymbol {Name: "ActionResult", TypeArguments: [INamedTypeSymbol { Name: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable" }]} 
                or INamedTypeSymbol { Name: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable" }]
        };

        var isTask = actionReturnTypeSymbol is INamedTypeSymbol { Name: "Task" };
        var isActionResult = isTask && actionReturnTypeSymbol is INamedTypeSymbol { TypeArguments: [INamedTypeSymbol { Name: "ActionResult" }] } ||
                             actionReturnTypeSymbol is INamedTypeSymbol { Name: "ActionResult" };

        return (actionReturnTypeSymbol, isCollection, isTask, isActionResult);
    }

    /// <summary>
    /// Gets parameter types of the action. 
    /// </summary>
    public static IEnumerable<string> GetActionParameterTypes
        (string action, string controller, List<SyntaxTree> containingSyntaxTrees, Compilation compilation)
    {
        var tree = containingSyntaxTrees.First(tree =>
        {
            var treeText = tree.GetText().ToString();
            return treeText.Contains("class " + controller + "Controller") && treeText.Contains(action);
        });

        var semanticModel = compilation.GetSemanticModel(tree);
        var actionDeclarationSyntax = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Where(classDeclaration => classDeclaration.Identifier.Text == controller + "Controller")
            .Select(controllerDeclaration => controllerDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(methodDeclaration => methodDeclaration.Identifier.Text == action))
            .First(actionDeclaration => actionDeclaration != null);

        return (ModelExtensions.GetDeclaredSymbol(semanticModel, actionDeclarationSyntax) as IMethodSymbol)!.Parameters.Select(p => p.Type.Name);
    }

    public static readonly List<string> NumericTypes = new() { nameof(Int16), nameof(Int32), nameof(Int64), nameof(UInt16), nameof(UInt32), nameof(UInt64), "byte", "short", "int", "float", "double" };

    public static string CapitalizeWord(string source) => source[0].ToString().ToUpper() + source[1..];
    public static string CapitalizeWord(Group source) => source.Value[0].ToString().ToUpper() + source.Value[1..];

    public static string ReplaceIfKeyword(string name)
    {
        return Keywords.Contains(name) ? "@" + name : name;
    }

    private static readonly string[] Keywords =
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while"
    };
}