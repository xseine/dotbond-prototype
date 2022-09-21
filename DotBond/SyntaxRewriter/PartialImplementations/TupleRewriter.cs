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
            .WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "{"))
            .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, "}"));
    }

    /// <summary>
    /// Converts tuple element syntax into object field syntax: Type Element -> element: Type
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitTupleElement(TupleElementSyntax node)
    {
        var overrideVisit = (TupleElementSyntax)base.VisitTupleElement(node)!;

        var leadingTrivia = overrideVisit.GetLeadingTrivia().LastOrDefault();
        var trailingTrivia = overrideVisit.GetTrailingTrivia();

        var fieldWithNewName = overrideVisit.WithIdentifier(
            SyntaxFactory.Identifier(
                CamelCaseConversion.LowercaseWord(overrideVisit.Identifier) + ": " + TypeTranslation.ParseType(overrideVisit.Type, SemanticModel)));

        overrideVisit = fieldWithNewName
            .WithType(SyntaxFactory.ParseTypeName(""))
            // .WithVariables(SyntaxFactory.SeparatedList(new[] { fieldWithNewName })))
            .WithLeadingTrivia(leadingTrivia)
            .WithoutTrailingTrivia()
            // .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .WithTrailingTrivia(trailingTrivia);

        return overrideVisit;
    }

    public override SyntaxNode VisitTupleExpression(TupleExpressionSyntax node)
    {
        var overrideVisit = (TupleExpressionSyntax)base.VisitTupleExpression(node);
        overrideVisit = overrideVisit.WithArguments(
            SyntaxFactory.SeparatedList(overrideVisit.Arguments.Select((arg, idx) =>
                arg.NameColon != null ?
                    arg :
                    arg.WithNameColon(SyntaxFactory
                        .NameColon(arg.Expression is InvocationExpressionSyntax or LiteralExpressionSyntax or TupleExpressionSyntax ? $"Item{idx + 1}" : arg.Expression.ToString()[(arg.Expression.ToString().LastIndexOf(".") + 1)..])
                        .WithLeadingTrivia(SyntaxFactory.Space)
                        .WithTrailingTrivia(SyntaxFactory.Space)))));

        return overrideVisit
            .WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "{"))
            .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, " }"));
    }

    public override SyntaxNode VisitParenthesizedVariableDesignation(ParenthesizedVariableDesignationSyntax node)
    {
        var tupleExpression = (TupleExpressionSyntax)VisitTupleExpression((TupleExpressionSyntax)((AssignmentExpressionSyntax)(((DeclarationExpressionSyntax)node.Parent).Parent)).Right);
        
        List<VariableDesignationSyntax> AttachNameColon(List<VariableDesignationSyntax> variables, List<ArgumentSyntax> arguments)
        {
            var resultVariables = new List<VariableDesignationSyntax>();

            var idx = 0;
            foreach (var variable in variables)
            {
                if (variable is SingleVariableDesignationSyntax single)
                {
                    var argumentName = arguments[idx].NameColon!.ToString();
                    var token = SyntaxFactory.Identifier(argumentName + single.Identifier.Text);
                    resultVariables.Add(SyntaxFactory.SingleVariableDesignation(token));
                }
                else
                {
                    var paren = (ParenthesizedVariableDesignationSyntax)variable;
                    var innerVariables = AttachNameColon(paren.Variables.ToList(), ((TupleExpressionSyntax)arguments[idx].Expression).Arguments.ToList());
                    var argumentName = arguments[idx].NameColon!.ToString();
                    var token = SyntaxFactory.Identifier($"{argumentName} {{{string.Join(", ", innerVariables)}}}");
                    resultVariables.Add(SyntaxFactory.SingleVariableDesignation(token));
                }

                idx++;
            }

            return resultVariables;
        }

        var variables = AttachNameColon(node.Variables.ToList(), tupleExpression.Arguments.ToList());
        
        return node.WithVariables(SyntaxFactory.SeparatedList(variables))
            .WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "{"))
            .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, "}"));
    }
}