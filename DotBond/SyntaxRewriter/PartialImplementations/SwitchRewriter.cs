using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBond.SyntaxRewriter.Core;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public override SyntaxNode VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var leadingTrivia = node.Arms.First().GetLeadingTrivia().ToFullString();

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
                Expression: SyntaxFactory.ReturnStatement(((ExpressionSyntax)base.Visit(arm.Expression).WithoutTrailingTrivia()).WithLeadingTrivia(SyntaxFactory.Space))
                    .WithLeadingTrivia(arm.EqualsGreaterThanToken.TrailingTrivia).WithTrailingTrivia(arm.Expression.GetTrailingTrivia()));
        });

        var ifStatements = newArms.Select(tuple => tuple.Condition != null ? SyntaxFactory.IfStatement(tuple.Condition, tuple.Expression) : tuple.Expression as StatementSyntax).ToList();
        ifStatements.Reverse();
        var ifAggregate = ifStatements.Skip(1).Cast<IfStatementSyntax>().Aggregate(ifStatements.First().WithLeadingTrivia(leadingTrivia + "\t"),
                (acc, curr) => curr.WithElse(SyntaxFactory.ElseClause(acc.WithLeadingTrivia(SyntaxFactory.Space)).WithLeadingTrivia("\n" + leadingTrivia + "\t")))
            .WithLeadingTrivia(leadingTrivia + "\t");

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
        return SyntaxFactory.InvocationExpression(SyntaxFactory.ParenthesizedExpression(
                SyntaxFactory.ParenthesizedLambdaExpression(block.WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)).WithCloseBraceToken(
                    block.CloseBraceToken.WithLeadingTrivia(
                        closingTrivia)))))
            .WithLeadingTrivia(openingTrivia);
    }
}