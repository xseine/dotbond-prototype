using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBond.SyntaxRewriter.Core;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    // Converts [^1] to .at(-1)
    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {

        if (node.IsKind(SyntaxKind.IndexExpression))
        {
            RegisterAncestorRewrite(syntaxNode =>
                {
                    var accessExpression = (ElementAccessExpressionSyntax)syntaxNode;
                    
                    return accessExpression.WithArgumentList(SyntaxFactory.BracketedArgumentList(CreateArgumentList(SyntaxFactory.BinaryExpression(
                        SyntaxKind.SubtractExpression,
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, accessExpression.Expression.WithoutLeadingTrivia(), SyntaxFactory.IdentifierName("length")),
                        node.Operand
                    )).Arguments));
                },
                SyntaxKind.ElementAccessExpression);
            
            // var index = int.Parse((overrideVisit.Operand as LiteralExpressionSyntax).Token.Text);
            // return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal("-" + index, -1 * index));
        }

        
        return base.VisitPrefixUnaryExpression(node);
    }

    public override SyntaxNode VisitRangeExpression(RangeExpressionSyntax node)
    {
        var left = node.LeftOperand ?? SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal("0", 0));
        var right = node.RightOperand;
    
        if (node.LeftOperand is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.IndexExpression } leftIndex)
            left = leftIndex.Operand.WithLeadingTrivia(leftIndex.GetLeadingTrivia().Append("-"));
        if (node.RightOperand is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.IndexExpression } rightIndex)
            right = rightIndex.Operand.WithLeadingTrivia(rightIndex.GetLeadingTrivia().Append("-"));
    
        RegisterAncestorRewrite(syntaxNode =>
            {
                var accessExpression = (ElementAccessExpressionSyntax)syntaxNode;
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, accessExpression.Expression, SyntaxFactory.IdentifierName("slice")),
                    right != null ? CreateArgumentList(left, right) : CreateArgumentList(left));
            },
            SyntaxKind.ElementAccessExpression);
    
    
        return node;
    }

    public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var typeSymbol = GetSavedSymbol(node.Expression) ?? (SemanticModel.SyntaxTree.GetRoot().Contains(node.Expression) ? SemanticModel.GetTypeInfo(node.Expression).Type : null);

        if (typeSymbol.Name == "GroupCollection")
        {
            var overrideVisit = (ElementAccessExpressionSyntax)base.VisitElementAccessExpression(node);
            return overrideVisit.WithArgumentList(overrideVisit.ArgumentList.WithOpenBracketToken(CreateToken(SyntaxKind.OpenBracketToken, "?.[")));
        }

        return base.VisitElementAccessExpression(node);
    }
}