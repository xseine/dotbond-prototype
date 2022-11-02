using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.SyntaxRewriter.Core;

public class AbstractRewriterWithSemantics : AbstractAncestorRewriter
{
    public SemanticModel SemanticModel { get; set; }

    public TopLevelSymbolsSet ImportedSymbols { get; } = new(new SynonymComparer());

    protected readonly Dictionary<string, ISymbol> _savedSymbolsFromOriginalTree = new();
    protected readonly Dictionary<string, ITypeSymbol> _savedTypeSymbolsFromOriginalTree = new();
    private readonly List<string> _overridenIdentifiers = new();

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
        if (node is IdentifierNameSyntax { Identifier.Text: not "dynamic" and not "DateTime" })
        {
            var symbol = SemanticModel.SyntaxTree.GetRoot().Contains(node) ? SemanticModel.GetSymbolInfo(node).Symbol : null;
            if (symbol != null && symbol.DeclaringSyntaxReferences.Any())
                ImportedSymbols.Add((ITypeSymbol)symbol);
        }
        else if (node is GenericNameSyntax generic)
        {
            var collectionTypeNames = new[] { "List", "IReadOnlyList", "IEnumerable", "ICollection", "IReadOnlyCollection", "HashSet" };
            if (!collectionTypeNames.Contains(generic.Identifier.Text) && generic.Identifier.Text != "Dictionary")
            {
                var typeSymbol = ModelExtensions.GetTypeInfo(SemanticModel, node).Type;
                if (typeSymbol != null && typeSymbol.DeclaringSyntaxReferences.Any()) ImportedSymbols.Add(typeSymbol.OriginalDefinition);
            }
            else
                foreach (var typeSyntax in generic.TypeArgumentList.Arguments.ToList())
                    GetSymbolsFromTypeSyntax(typeSyntax);
        }
    }

    protected ISymbol GetSavedSymbol(SyntaxNode node)
    {
        return _savedSymbolsFromOriginalTree.TryGetValue(node.ToString(), out var savedSymbol) ? savedSymbol : null;
    }

    protected ITypeSymbol GetSavedTypeSymbol(SyntaxNode node)
    {
        return _savedTypeSymbolsFromOriginalTree.TryGetValue(node.ToString(), out var savedSymbol) ? savedSymbol : null;
    }

    // private static string GetNodeKey(SyntaxNode node)
    // {
    //     var key = node is IdentifierNameSyntax {Parent: MemberAccessExpressionSyntax parent} identifier ? $"{parent.ToString().ToLower()}+{identifier.ToString().ToLower()}" : node.ToString();
    //     return key;
    // }

    protected ISymbol GetSymbol(SyntaxNode node)
    {
        return GetSavedSymbol(node) ??
               (SemanticModel.SyntaxTree.GetRoot().Contains(node) ? SemanticModel.GetSymbolInfo(node).Symbol : null);
    }

    protected ITypeSymbol GetTypeSymbol(SyntaxNode node)
    {
        return GetSavedTypeSymbol(node) ??
               (SemanticModel.SyntaxTree.GetRoot().Contains(node) ? SemanticModel.GetTypeInfo(node).Type : null);
    }

    protected bool TryGetSavedSymbolsToUse<TSyntax>(ref TSyntax node) where TSyntax : SyntaxNode
    {
        if (!SemanticModel.SyntaxTree.GetRoot().Contains(node)) return false;

        var identifiersWithNonUniqueName = node.DescendantNodes().OfType<IdentifierNameSyntax>().GroupBy(e => e.Identifier.Text).Where(e => e.Count() > 1).Select(e => e.Key).ToList();
        var nonUniqueIdx = 0;

        var overridenIdentifiers = new List<string>();
        var nodesToReplace = new List<SyntaxNode>();

        foreach (var expressionSyntax in node.DescendantNodes().OfType<ExpressionSyntax>())
        {
            var symbol = SemanticModel.GetSymbolInfo(expressionSyntax).Symbol;
            if (symbol == null) continue;

            var key = expressionSyntax.ToString();

            if (expressionSyntax is IdentifierNameSyntax id && identifiersWithNonUniqueName.Contains(id.Identifier.Text))
            {
                key = id.Identifier.Text + "uniqueSufix" + nonUniqueIdx++;
                overridenIdentifiers.Add(key);
                nodesToReplace.Add(expressionSyntax);
            }

            _savedSymbolsFromOriginalTree.TryAdd(key, symbol);
            _savedTypeSymbolsFromOriginalTree.TryAdd(key, SemanticModel.GetTypeInfo(expressionSyntax).Type);
        }


        var idx = 0;
        node = node.ReplaceNodes(nodesToReplace, (_, _) => SyntaxFactory.IdentifierName(overridenIdentifiers[idx++]));
        _overridenIdentifiers.AddRange(overridenIdentifiers);

        return true;
    }

    protected void ClearSavedSymbols<TSyntax>(ref TSyntax overridenNode) where TSyntax : SyntaxNode
    {
        if (_overridenIdentifiers.Any())
        {
            var nodesToReplace = overridenNode.DescendantNodes().OfType<IdentifierNameSyntax>().Where(e => _overridenIdentifiers.Contains(e.Identifier.Text, StringComparer.OrdinalIgnoreCase));

            overridenNode = overridenNode.ReplaceNodes(nodesToReplace, (id, _) =>
                SyntaxFactory.IdentifierName(Regex.Replace(id.Identifier.Text, @"uniqueSufix\d+$", "")).WithLeadingTrivia(id.GetLeadingTrivia()).WithTrailingTrivia(id.GetTrailingTrivia())
            );
            _overridenIdentifiers.Clear();
        }

        _savedSymbolsFromOriginalTree.Clear();
        _savedTypeSymbolsFromOriginalTree.Clear();
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
        return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList((arguments ?? Array.Empty<ExpressionSyntax>()).Select(SyntaxFactory.Argument)));
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
        public TopLevelSymbolsSet(IEqualityComparer<ITypeSymbol> comparer) : base(comparer)
        {
        }

        public new bool Add(ITypeSymbol item)
        {
            return item.ContainingType != null ? Add(item.ContainingType) : base.Add(item);
        }
    }
}