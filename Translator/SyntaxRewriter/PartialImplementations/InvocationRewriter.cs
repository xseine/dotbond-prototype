using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Translator.SyntaxRewriter.PartialImplementations;


public partial class Rewriter
{

    /// <summary>
    /// Method handling is done in MemberAccessRewriter
    /// This method handles only:
    /// - nameof()
    /// -  
    /// </summary>
    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" })
        {
            var argument = ((IdentifierNameSyntax)node.ArgumentList.Arguments.First().Expression).Identifier.Text;
            return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, CreateToken(SyntaxKind.StringLiteralToken, $"'{argument}'"));
        }

        return base.VisitInvocationExpression(node);
    }
}