using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
    {
        // foreach (var item in dic)
        // {
        //     var k = item.Key;
        //     var v = item.Value;
        //     pairs.Add(item);
        // }
        
        var overrideVisit = ((ForEachStatementSyntax)base.VisitForEachStatement(node))
            .WithForEachKeyword(CreateToken(SyntaxKind.ForEachKeyword, "for").WithLeadingTrivia(node.ForEachKeyword.LeadingTrivia))
            .WithInKeyword(CreateToken(SyntaxKind.InKeyword, "of "))
            .WithType(SyntaxFactory.IdentifierName("let "));

        var isDictionary = SemanticModel.GetTypeInfo(node.Expression).Type.Name == "Dictionary";
        if (isDictionary)
        {
            overrideVisit = overrideVisit.WithExpression(SyntaxFactory.ParseExpression($"Object.entries({node.Expression.ToString()}).map(([key, value]) => ({{key, value}}))"));
        }

        return overrideVisit;

    }
}