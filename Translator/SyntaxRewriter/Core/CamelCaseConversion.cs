using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Translator.SyntaxRewriter.Core;

public static class CamelCaseConversion
{
    public static TNode ChangeIdentifierToCamelCase<TNode>(this TNode node) where TNode : SyntaxNode
    {
        var result = node switch
        {
            PropertyDeclarationSyntax property => property.WithIdentifier(SyntaxFactory.Identifier(LowercaseWord(property.Identifier))) as TNode,
            MethodDeclarationSyntax method => method.WithIdentifier(SyntaxFactory.Identifier(LowercaseWord(method.Identifier))) as TNode,
            IdentifierNameSyntax identifier => identifier.WithIdentifier(SyntaxFactory.Identifier(LowercaseWord(identifier.Identifier))) as TNode,
            // TupleElementSyntax tuple => tuple.WithIdentifier(SyntaxFactory.Identifier(LowercaseWord(tuple.Identifier))) as TNode,
            _ => throw new ArgumentOutOfRangeException(nameof(node), node, null)
        };

        return result.WithLeadingTrivia(node.GetLeadingTrivia());
    }

    public static string LowercaseWord(SyntaxToken token) => token.Text[0].ToString().ToLower() + token.Text[1..];
    public static string LowercaseWord(string token) => token[0].ToString().ToLower() + token[1..];
}