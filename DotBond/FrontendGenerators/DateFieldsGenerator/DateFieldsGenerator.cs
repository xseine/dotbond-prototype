using DotBond.Misc;
using DotBond.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using IdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;

namespace DotBond.Generators.DateFieldsGenerator;

public sealed class DateFieldsGenerator : AbstractGenerator 
{
    private const string ControllerDefinitionsPath = "Actions/date-fields.ts";

    public DateFieldsGenerator(string assemblyName) : base(assemblyName)
    {
    }

    public override HashSet<TypeSymbolLocation> GetControllerCallback(FileAnalysisCallbackInput input)
    {
        return null;
    }

    public override HashSet<TypeSymbolLocation> DeleteSource(string filePath)
    {
        throw new NotImplementedException();
    }
    //
    // public override IEnumerable<ITypeSymbol> GetDefinitionFileCallback(FileObservables.FileAnalysisCallbackInput input)
    // {
    //     var (tree, _, _) = input;
    //     var a = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
    //         .Where(prop => prop.Type is IdentifierNameSyntax identifierNameSyntax && identifierNameSyntax.Identifier.Text.StartsWith(nameof(DateTime)));
    //
    //     return new List<ITypeSymbol>();
    // }
}