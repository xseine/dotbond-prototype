using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBond.SyntaxRewriter.Core;

public static class SyntaxExtensions
{
    public static IEnumerable<SyntaxTrivia> Append(this SyntaxTriviaList source, string trivia)
    {
        return source.Append(SyntaxFactory.Whitespace(trivia));
    }
    
    public static IEnumerable<SyntaxTrivia> Prepend(this SyntaxTriviaList source, string trivia)
    {
        return source.Prepend(SyntaxFactory.Whitespace(trivia));
    }

    public static IEnumerable<SyntaxTrivia> Append(this IEnumerable<SyntaxTrivia> source, string trivia)
    {
        return source.Append(SyntaxFactory.Whitespace(trivia));
    }

    public static IEnumerable<SyntaxTrivia> Prepend(this IEnumerable<SyntaxTrivia> source, string trivia)
    {
        return source.Prepend(SyntaxFactory.Whitespace(trivia));
    }

    public static TSyntax WithLeadingTrivia<TSyntax>(this TSyntax node, string trivia) where TSyntax : SyntaxNode
    {
        return node.WithLeadingTrivia(SyntaxFactory.Whitespace(trivia));
    }

    public static TSyntax WithTrailingTrivia<TSyntax>(this TSyntax node, string trivia) where TSyntax : SyntaxNode
    {
        return node.WithTrailingTrivia(SyntaxFactory.Whitespace(trivia));
    }
}