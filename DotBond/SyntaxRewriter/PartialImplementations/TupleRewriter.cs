using System.Text.RegularExpressions;
using ConsoleApp1.Common;
using DotBond.SyntaxRewriter.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public override SyntaxNode VisitTupleType(TupleTypeSyntax node)
    {
        var overrideVisit = (TupleTypeSyntax)base.VisitTupleType(node);

        return overrideVisit
            .WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "["))
            .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, "]"));
    }

    /// <summary>
    /// Converts tuple element syntax into object field syntax: Type Element -> element: Type
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitTupleElement(TupleElementSyntax node)
    {
        var overrideVisit = (TupleElementSyntax)base.VisitTupleElement(node)!;
        overrideVisit = overrideVisit.WithType(SyntaxFactory.ParseTypeName(TypeTranslation.ParseType(overrideVisit.Type, SemanticModel))).WithIdentifier(SyntaxFactory.Identifier(""));

        return overrideVisit;
    }

    public override SyntaxNode VisitTupleExpression(TupleExpressionSyntax node)
    {
        var overrideVisit = (TupleExpressionSyntax)base.VisitTupleExpression(node);
        overrideVisit = overrideVisit.WithArguments(
            SyntaxFactory.SeparatedList(overrideVisit.Arguments.Select(arg => SyntaxFactory.Argument(arg.Expression))));

        return overrideVisit
            .WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "["))
            .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, " ]"));
    }

    public override SyntaxNode VisitParenthesizedVariableDesignation(ParenthesizedVariableDesignationSyntax node)
    {
        node = node.ReplaceTokens(node.DescendantTokens().Where(e => e.IsKind(SyntaxKind.OpenParenToken)), (_, _) => CreateToken(SyntaxKind.OpenParenToken, "["));
        node = node.ReplaceTokens(node.DescendantTokens().Where(e => e.IsKind(SyntaxKind.CloseParenToken)), (_, _) => CreateToken(SyntaxKind.CloseParenToken, "]"));
        
        return node;
    }
}