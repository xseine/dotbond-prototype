using ConsoleApp1.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBond.SyntaxRewriter.Core;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        // 1. Get plain IsExpression
        // 2. add all the other logic if (recursive pattern is used)
        // 3. replace pattern variables in the block (of if statement)

        var overrideVisit = (IsPatternExpressionSyntax)base.VisitIsPatternExpression(node)!;

        var overridePattern = overrideVisit.Pattern;
        var isNot = overridePattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern };
        if (isNot) overridePattern = ((UnaryPatternSyntax)overridePattern).Pattern;
        ExpressionSyntax resultExpression = overrideVisit;
        
        if (overridePattern is DeclarationPatternSyntax declaration)
        {
            var designationToken = (declaration.Designation as SingleVariableDesignationSyntax)!.Identifier;
            var type = TypeTranslation.ParseType(declaration.Type, SemanticModel);
            var replacement = node.Expression
                .WithLeadingTrivia(SyntaxFactory.Whitespace("("))
                .WithTrailingTrivia(SyntaxFactory.Whitespace($" as {type})"));

            RegisterAncestorRewrite(syntaxNode =>
            {
                var ifStatement = (IfStatementSyntax)syntaxNode;

                var rewriter = new VariableRewriter(new() { { designationToken.Text, replacement } });

                var overrideStatement = (StatementSyntax)rewriter.Visit(ifStatement.Statement);
                var overrideCondition = (ExpressionSyntax)rewriter.Visit(ifStatement.Condition);
                return ifStatement.WithStatement(overrideStatement).WithCondition(overrideCondition);
            }, SyntaxKind.IfStatement);

            resultExpression = RewriteIsPattern(overrideVisit.Expression, overrideVisit.IsKeyword, declaration.Type);
        }
        else if (overridePattern is TypePatternSyntax pattern)
        {
            resultExpression = RewriteIsPattern(overrideVisit.Expression, overrideVisit.IsKeyword, pattern.Type);
        }
        else if (overridePattern is ConstantPatternSyntax constant)
        {
            resultExpression = constant.Expression.IsKind(SyntaxKind.IdentifierName)
                ? RewriteIsPattern(overrideVisit.Expression, overrideVisit.IsKeyword, constant.Expression) 
                : SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, overrideVisit.Expression, constant.Expression.WithLeadingTrivia(SyntaxFactory.Space));
        }
        else if (overridePattern is RelationalPatternSyntax relational)
        {
            resultExpression = HandleRelationalPattern(relational, overrideVisit);
        }
        else if (overridePattern is BinaryPatternSyntax binary)
        {
            resultExpression = SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, 
                HandleRelationalPattern(binary.Left as RelationalPatternSyntax, overrideVisit),
                HandleRelationalPattern(binary.Right as RelationalPatternSyntax, overrideVisit));
        }

        // var overrideVisit = (IsPatternExpressionSyntax)base.VisitIsPatternExpression(node);
        // var a = ((overrideVisit.Pattern as DeclarationPatternSyntax).Designation as SingleVariableDesignationSyntax).Identifier.Text;

        if (isNot)
        {
            resultExpression = SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, 
                resultExpression.WithLeadingTrivia(SyntaxFactory.Whitespace("(")).WithTrailingTrivia(SyntaxFactory.Whitespace(") ")),
                SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression).WithLeadingTrivia(SyntaxFactory.Space));
        }
        
        
        return resultExpression;
    }
    

    public override SyntaxNode VisitRecursivePattern(RecursivePatternSyntax node)
    {
        var overrideVisit = (RecursivePatternSyntax)base.VisitRecursivePattern(node)!;
        
        var patternPathOfIdentifiers = new List<string>();
        var deferredConditionalsAndReplacements = overrideVisit.PropertyPatternClause?.Subpatterns
            .Select(propertyPattern =>
            {
                // property name, prefixed with the path of that property
                var path = patternPathOfIdentifiers.Count > 0 ? new String('(', patternPathOfIdentifiers.Count(e => e.EndsWith(")"))) + string.Join("?.", patternPathOfIdentifiers) + "?." : null;
                var name = SyntaxFactory.ParseExpression(path + propertyPattern.NameColon.Name.Identifier.Text);
                
                var newCondition = propertyPattern.Pattern switch
                {
                    TypePatternSyntax or DeclarationPatternSyntax
                        => RewriteIsPattern(name, (propertyPattern.Pattern as TypePatternSyntax)?.Type ?? ((DeclarationPatternSyntax)propertyPattern.Pattern).Type),
                    ConstantPatternSyntax constant => SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, name, constant.Expression),
                    // "is not null"
                    UnaryPatternSyntax {RawKind: (int)SyntaxKind.NotPattern, Pattern: ConstantPatternSyntax {Expression: LiteralExpressionSyntax {RawKind: (int)SyntaxKind.NullLiteralExpression}}}
                        => name,
                    _ => null
                };

                (string, ExpressionSyntax)? declarationReplacement = propertyPattern.Pattern switch
                {
                    DeclarationPatternSyntax declaration => (((SingleVariableDesignationSyntax)declaration.Designation).Identifier.Text, name),
                    VarPatternSyntax var => (((SingleVariableDesignationSyntax)var.Designation).Identifier.Text, name),
                    _ => null
                };

                return (newCondition, declarationReplacement);
            });


        var patternType = overrideVisit.Type;
        
        SyntaxNode HandleRecursivePatternParents(SyntaxNode syntaxNode)
        {
            var originalType = patternType != null ? TypeTranslation.ParseType(patternType, SemanticModel) : null;

            if (syntaxNode is IsPatternExpressionSyntax isPattern)
            {
                patternPathOfIdentifiers.Insert(0, originalType != null ? $"{isPattern.Expression.ToString()} as {originalType})" : isPattern.Expression.ToString());

                var (newConditionals, replacements) = deferredConditionalsAndReplacements.Aggregate((new List<ExpressionSyntax>(), new List<(string, ExpressionSyntax)?>()), (acc, curr) =>
                {
                    if (curr.newCondition != null) acc.Item1.Add(curr.newCondition);
                    if (curr.declarationReplacement != null) acc.Item2.Add(curr.declarationReplacement);
                    return acc;
                });

                var combinedConditional = newConditionals.Any() 
                    ? newConditionals.Skip(1).Aggregate(newConditionals.First(), (acc, curr) => SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, acc, curr))
                    : null;

                RegisterAncestorRewrite(syntaxNode =>
                {
                    var ifStatement = (IfStatementSyntax)syntaxNode;
                    if (combinedConditional != null)
                        ifStatement = ifStatement.WithCondition(SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, ifStatement.Condition, combinedConditional));

                    var rewriter = new VariableRewriter(new(replacements.Cast<(string, ExpressionSyntax)>().Select(tuple => new KeyValuePair<string, ExpressionSyntax>(tuple.Item1, tuple.Item2))));

                    var overrideStatement = (StatementSyntax)rewriter.Visit(ifStatement.Statement);
                    var overrideCondition = (ExpressionSyntax)rewriter.Visit(ifStatement.Condition);
                    return ifStatement.WithStatement(overrideStatement).WithCondition(overrideCondition);
                }, SyntaxKind.IfStatement);
            }
            else if (syntaxNode is SubpatternSyntax subpattern)
            {
                patternPathOfIdentifiers.Insert(0, originalType != null ? $"{subpattern.NameColon.Name.Identifier.Text} as {originalType})" : null);
                patternType = subpattern.Pattern is TypePatternSyntax typePatternSyntax ? typePatternSyntax.Type : (subpattern.Pattern as DeclarationPatternSyntax).Type;
                RegisterAncestorRewrite(HandleRecursivePatternParents, SyntaxKind.IsPatternExpression, SyntaxKind.Subpattern);
            }

            return syntaxNode;
        }

        RegisterAncestorRewrite(HandleRecursivePatternParents, SyntaxKind.IsPatternExpression, SyntaxKind.Subpattern);

        // For {} patterns (i.e. without type) replace with "is not null", 
        if (node.Type == null)
            return SyntaxFactory.UnaryPattern(SyntaxFactory.Token(SyntaxKind.NotKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)).WithLeadingTrivia(SyntaxFactory.Space));
        if (node.Designation != null)
            return SyntaxFactory.DeclarationPattern(node.Type, node.Designation);
        return SyntaxFactory.TypePattern(node.Type);
    }




    private static ExpressionSyntax HandleRelationalPattern(RelationalPatternSyntax relational, IsPatternExpressionSyntax overrideVisit)
    {
        ExpressionSyntax resultExpression;
        var expressionKind = relational.OperatorToken.IsKind(SyntaxKind.LessThanToken)
            ? SyntaxKind.LessThanExpression
            : relational.OperatorToken.IsKind(SyntaxKind.LessThanOrEqualExpression)
                ? SyntaxKind.LessThanOrEqualExpression
                : relational.OperatorToken.IsKind(SyntaxKind.GreaterThanToken)
                    ? SyntaxKind.GreaterThanExpression
                    : relational.OperatorToken.IsKind(SyntaxKind.GreaterThanEqualsToken)
                        ? SyntaxKind.GreaterThanOrEqualExpression
                        : default;

        resultExpression = SyntaxFactory.BinaryExpression(expressionKind, overrideVisit.Expression, relational.Expression);
        return resultExpression;
    }


    private ExpressionSyntax RewriteIsPattern(ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
    {
        return (ExpressionSyntax) RewriteIsExpression(SyntaxFactory.BinaryExpression(SyntaxKind.IsExpression, left, operatorToken, right));
    }

    private ExpressionSyntax RewriteIsPattern(ExpressionSyntax left, ExpressionSyntax right)
    {
        var operatorToken = CreateToken(SyntaxKind.IsKeyword, " is ");
        return RewriteIsPattern(left, operatorToken, right);
    }

    /// <summary>
    /// Used to replace pattern designations with source expressions
    /// </summary>
    public class VariableRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, ExpressionSyntax> _variableReplacements;

        public VariableRewriter(Dictionary<string, ExpressionSyntax> variableDictionary)
        {
            _variableReplacements = variableDictionary;
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            var originalIdentifier = node.Identifier.Text;
            var hasReplacement = _variableReplacements.TryGetValue(originalIdentifier, out var identifierReplacement);
            
            if (hasReplacement)
                identifierReplacement = identifierReplacement
                    .WithLeadingTrivia(node.GetLeadingTrivia().Concat(identifierReplacement.GetLeadingTrivia()))
                    .WithTrailingTrivia(identifierReplacement.GetTrailingTrivia().Concat(node.GetTrailingTrivia()));
            
            return hasReplacement ? identifierReplacement : node;
        }
    }
}