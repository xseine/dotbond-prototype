using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Translator.SyntaxRewriter.Core;

public class AbstractRewriterWithSemantics : AbstractAncestorRewriter
{
    public SemanticModel SemanticModel { get; set; }

    public TopLevelSymbolsSet ImportedSymbols { get; } = new(new SynonymComparer());

    /// <summary>
    /// Lists all the properties used by the classes and their properties in the syntax tree.
    /// </summary>
    public HashSet<string> Attributes = new();

    public AbstractRewriterWithSemantics(SemanticModel semanticModel) : base(true) // : base(SyntaxWalkerDepth.Token)
    {
        SemanticModel = semanticModel;
    }

    /// <summary>
    /// Extracts symbols of non-predefined types. This also includes generic type arguments of types.
    /// </summary>
    /// <param name="node"></param>
    protected void GetSymbolsFromTypeSyntax(TypeSyntax node)
    {
        if (node is IdentifierNameSyntax {Identifier.Text: not "dynamic" and not "DateTime"})
        {
            var symbol = SemanticModel.SyntaxTree.GetRoot().Contains(node) ? SemanticModel.GetSymbolInfo(node).Symbol : null;
            if (symbol != null)
                ImportedSymbols.Add((ITypeSymbol)SemanticModel.GetSymbolInfo(node).Symbol);
        }
        else if (node is GenericNameSyntax generic)
        {
            var collectionTypeNames = new[] { "List", "IReadOnlyList", "IEnumerable", "ICollection", "IReadOnlyCollection", "HashSet" };
            if (!collectionTypeNames.Contains(generic.Identifier.Text) && generic.Identifier.Text != "Dictionary")
            {
                var typeSymbol = ModelExtensions.GetTypeInfo(SemanticModel, node).Type;
                if (typeSymbol != null) ImportedSymbols.Add(typeSymbol.OriginalDefinition);
            }
            else
                foreach (var typeSyntax in generic.TypeArgumentList.Arguments.ToList()) 
                    GetSymbolsFromTypeSyntax(typeSyntax);
        }
    }

    public static SyntaxToken CreateToken(SyntaxKind tokenKind, string text = "")
    {
        return SyntaxFactory.Token(default, tokenKind, text, text, default);
    }

    public static SyntaxNode CreateNode(string text)
    {
        return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, CreateToken(SyntaxKind.StringLiteralToken, text));
    }

    public static ArgumentListSyntax CreateArgumentList(params ExpressionSyntax[] arguments)
    {
        return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.Select(SyntaxFactory.Argument)));
    }

    protected static readonly Random Random = new();

    public static string GenerateRandomVariableName()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz";
        return new string(Enumerable.Repeat(chars, 30)
            .Select(s => s[Random.Next(s.Length)]).ToArray());
    }

    private class SynonymComparer : IEqualityComparer<ITypeSymbol>
    {
        public bool Equals(ITypeSymbol one, ITypeSymbol two)
        {
            // Adjust according to requirements.
            return StringComparer.InvariantCultureIgnoreCase
                .Equals(one?.ContainingNamespace.Name + one?.Name, two?.ContainingNamespace.Name + two?.Name);

        }

        public int GetHashCode(ITypeSymbol item)
        {
            return StringComparer.InvariantCultureIgnoreCase
                .GetHashCode(item.ContainingNamespace.Name + item.Name);

        }
    }

    public class TopLevelSymbolsSet : HashSet<ITypeSymbol>
    {
        public TopLevelSymbolsSet(IEqualityComparer<ITypeSymbol> comparer) : base(comparer) { }

        public new bool Add(ITypeSymbol item)
        {
            return item.ContainingType != null ? Add(item.ContainingType) : base.Add(item);
        }
    }
}