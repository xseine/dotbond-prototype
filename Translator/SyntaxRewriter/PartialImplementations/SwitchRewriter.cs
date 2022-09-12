using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.SyntaxRewriter.Core;

namespace Translator.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public override SyntaxNode VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var leadingTrivia = node.Arms.First().GetLeadingTrivia();

        var newArms = node.Arms.Select(arm =>
        {
            var isPattern = arm.Pattern is not DiscardPatternSyntax
                ? SyntaxFactory.IsPatternExpression(node.GoverningExpression, arm.Pattern.WithLeadingTrivia(SyntaxFactory.Space))
                : null;
            return (
                Condition: arm.WhenClause != null
                    ? isPattern != null
                        ? SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, isPattern, (ExpressionSyntax)base.Visit(arm.WhenClause.Condition)) as ExpressionSyntax
                        : (ExpressionSyntax)base.Visit(arm.WhenClause.Condition)
                    : isPattern,
                Expression: SyntaxFactory.ReturnStatement(((ExpressionSyntax)base.Visit(arm.Expression)).WithLeadingTrivia(SyntaxFactory.Space))
                    .WithLeadingTrivia(arm.EqualsGreaterThanToken.TrailingTrivia));
        });

        var ifStatements = newArms.Select(tuple => tuple.Condition != null ? SyntaxFactory.IfStatement(tuple.Condition, tuple.Expression) : tuple.Expression as StatementSyntax).ToList();
        ifStatements.Reverse();
        var ifAggregate = ifStatements.Skip(1).Cast<IfStatementSyntax>().Aggregate(ifStatements.First().WithLeadingTrivia(leadingTrivia),
                (acc, curr) => curr.WithElse(SyntaxFactory.ElseClause(acc.WithLeadingTrivia(SyntaxFactory.Space)).WithLeadingTrivia(leadingTrivia.Prepend(SyntaxFactory.CarriageReturnLineFeed))))
            .WithLeadingTrivia(leadingTrivia);

        var overrideVisit = (IfStatementSyntax)base.VisitIfStatement((IfStatementSyntax)ifAggregate);

        // var openingBlockTrivia = node.ArgumentList?.GetTrailingTrivia() ?? node.Type.GetTrailingTrivia();

        var block = SyntaxFactory.Block(overrideVisit)
            .WithOpenBraceToken(CreateToken(SyntaxKind.OpenBraceToken, " {").WithTrailingTrivia(node.SwitchKeyword.TrailingTrivia));
        // .WithOpenBraceToken(SyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken))
        // .WithCloseBraceToken(SyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));

        return CreateIIFE(node.GetLeadingTrivia(), block, node.CloseBraceToken.LeadingTrivia);
    }

    private static SyntaxNode CreateIIFE(SyntaxTriviaList openingTrivia, BlockSyntax block, SyntaxTriviaList closingTrivia)
    {
        block = block.WithStatements(SyntaxFactory.List(block.Statements.Take(block.Statements.Count - 1)
            .Append(block.Statements.Last().WithTrailingTrivia(block.Statements.Last().GetTrailingTrivia().AddRange(closingTrivia)))));
        return SyntaxFactory.InvocationExpression(SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ParenthesizedLambdaExpression(block.WithTrailingTrivia(closingTrivia))))
            .WithLeadingTrivia(openingTrivia);
    }
}