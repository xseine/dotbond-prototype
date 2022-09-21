using ConsoleApp1.Common;
using DotBond.TranslatorFiles.Translator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AnonymousObjectCreationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousObjectCreationExpressionSyntax;
using ArgumentSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax;
using ArrayCreationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ArrayCreationExpressionSyntax;
using ExpressionStatementSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionStatementSyntax;
using ExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using IdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using InvocationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
using LocalDeclarationStatementSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax;
using ObjectCreationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax;
using ReturnStatementSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax;
using TypeSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax;
using VariableDeclaratorSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax;
using DotBond.SyntaxRewriter.Core;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    // Handle arrays
    public override SyntaxNode VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
    {
        var baseVisit = (ImplicitArrayCreationExpressionSyntax)base.VisitImplicitArrayCreationExpression(node)!;

        return baseVisit.Initializer
            .WithOpenBraceToken(SyntaxFactory.Token(node.GetLeadingTrivia(), SyntaxKind.OpenBraceToken, "[", "[", node.GetTrailingTrivia()))
            .WithCloseBraceToken(SyntaxFactory.Token(node.GetLeadingTrivia(), SyntaxKind.CloseBraceToken, "]", "]", node.GetTrailingTrivia()));
    }

    public override SyntaxNode VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        var tsType = TypeTranslation.ParseType(node.Type, SemanticModel);
        var values = string.Join(", ", node.Initializer?.Expressions.Select(e => base.Visit(e).ToFullString().Trim()) ?? new List<string>());
        var newExpression = $"[{values}] as {tsType}";
        return SyntaxFactory.ParseExpression(newExpression);
    }

    /// <summary>
    /// Topmost initializer is responsible for the assignments,
    /// so nested ones should not alter their structure, as far as initializers are concerned.
    /// </summary>
    private bool _skipNestedObjectInitializerExpressions;


    public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        return VisitObjectCreationExpression(node, true);
    }

    /// <summary>
    /// Object initializer syntax is handled by adding extra assignment statements.
    /// It handles:
    ///
    /// - Local declarations. It adds extra assignment statements.
    ///
    /// - Assignment expression. Also adds extra assignment statements.
    ///
    /// - Return statement. Creates a new local variable, assigns value to the object
    /// and returns that variable's value;
    ///
    /// - Invocations. Creates a local variable above the invocation and uses it as an argument.
    ///
    /// - Lambda expression. Creates a block expression with the extra assignment statements.
    /// 
    /// </summary>
    /// <param name="node">Node to visit.</param>
    /// <param name="isNodeOriginal">If the node is not from the original source, its sub-expressions won't be visited.</param>
    /// <returns></returns>
    private SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node, bool isNodeOriginal)
    {
        SyntaxNode knownTranslation;
        // (1) Without initializer either see if it's got a manual translation
        if (node.Initializer == null)
        {
            knownTranslation = KnownObjectsRewrites.RewriteObjectCreationExpression(node, this);
            if (knownTranslation != null)
                return knownTranslation;
        }

        // Find imports from other files
        GetSymbolsFromTypeSyntax(node.Type);

        var type = GetFullTypeSyntax(node.Type);

        // (1) or simply return the node
        if (node.Initializer == null) return base.VisitObjectCreationExpression(node.WithType(type));

        var overrideVisit = isNodeOriginal ? (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)! : node;
        overrideVisit = overrideVisit.WithType(type);

        var typeName = node.Type switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            GenericNameSyntax genericName => genericName.Identifier.Text,
            _ => null
        };

        var openingBlockTrivia = node.ArgumentList?.GetTrailingTrivia() ?? node.Type.GetTrailingTrivia();
        var fieldLeadingTrivia =
            node.Initializer.Expressions.First().GetLeadingTrivia(); // node.Initializer.Expressions.First().GetLeadingTrivia().Prepend(SyntaxFactory.CarriageReturnLineFeed).ToList();
        var leadingTriviaString = string.Join("", fieldLeadingTrivia);

        if (overrideVisit.Initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            var trailingTrivias = overrideVisit.Initializer.Expressions.GetSeparators().Select(e => e.GetAllTrivia().Prepend(","))
                .Append(node.Initializer.Expressions.Last().GetTrailingTrivia()).ToList();

            if (typeName == "List")
            {
                var expressions = overrideVisit.Initializer.Expressions
                    .Select((e, idx) =>
                    {
                        if (idx == 0) e = e.WithLeadingTrivia(openingBlockTrivia.Concat(e.GetLeadingTrivia()));
                        return e.WithTrailingTrivia(trailingTrivias[idx]).ToFullString();
                    }).ToList();

                var closeBracketTrivia = overrideVisit.Initializer.CloseBraceToken.LeadingTrivia.ToFullString();
                var tsType = TypeTranslation.ParseType(node.Type, SemanticModel);

                var expressionString = $"[{string.Join("", expressions)}{closeBracketTrivia}] as {tsType}";
                if (node.Parent is MemberAccessExpressionSyntax)
                    expressionString = $"({expressionString})";

                var newRhsExpresion = SyntaxFactory.ParseExpression(expressionString);

                return newRhsExpresion.WithTrailingTrivia(newRhsExpresion.GetTrailingTrivia());
            }
            else if (typeName == "Dictionary")
            {
                var expressions = overrideVisit.Initializer.Expressions.Cast<InitializerExpressionSyntax>()
                    .Select((e, idx) =>
                    {
                        return $"{(idx == 0 ? openingBlockTrivia : null)}{e.GetLeadingTrivia()}[{e.Expressions[0].ToFullString()}]: {e.Expressions[1].WithTrailingTrivia(trailingTrivias[idx]).ToFullString()}";
                    }).ToList();

                var closeBracketTrivia = overrideVisit.Initializer.CloseBraceToken.LeadingTrivia.ToFullString();
                var tsType = TypeTranslation.ParseType(node.Type, SemanticModel);

                var expressionString = $"{{{string.Join("", expressions)}{closeBracketTrivia}}} as {tsType}";
                if (node.Parent is MemberAccessExpressionSyntax)
                    expressionString = $"({expressionString})";

                var newRhsExpresion = SyntaxFactory.ParseExpression(expressionString);

                return newRhsExpresion.WithTrailingTrivia(newRhsExpresion.GetTrailingTrivia());
            }

            return null;
        }
        else
        {
            var statements = new SyntaxList<SyntaxNode>();
            var a = typeName != "Dictionary" 
                ? overrideVisit.WithInitializer(null).WithLeadingTrivia(SyntaxFactory.Space).WithoutTrailingTrivia()
                : SyntaxFactory.ParseExpression($"{{}} as {TypeTranslation.ParseType(node.Type, SemanticModel)}");
            var newLocalDeclaration = SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("let").WithLeadingTrivia(fieldLeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space),
                new SeparatedSyntaxList<VariableDeclaratorSyntax>().Add(
                    SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("__ret "), null,
                        SyntaxFactory.EqualsValueClause(a)))));

            statements = statements.Add(newLocalDeclaration);


            var idx = 0;
            var trailingTrivias = overrideVisit.Initializer.Expressions.GetSeparators().Select(e => e.GetAllTrivia()).Append(overrideVisit.Initializer.Expressions.Last().GetTrailingTrivia()).ToList();
            foreach (var expression in overrideVisit.Initializer.Expressions.Cast<AssignmentExpressionSyntax>())
            {
                var assignmentExpressionSyntax = expression;
                if (idx == 0) assignmentExpressionSyntax = expression.WithLeadingTrivia(openingBlockTrivia.Concat(expression.GetLeadingTrivia()));
                // Prepend "__ret." to assignment
                assignmentExpressionSyntax = assignmentExpressionSyntax.WithLeft(
                    assignmentExpressionSyntax.Left.WithLeadingTrivia(assignmentExpressionSyntax.Left.GetLeadingTrivia().Append("__ret" + (assignmentExpressionSyntax.Left.IsKind(SyntaxKind.ImplicitElementAccess) ? "" : "."))));

                statements = statements.Add(SyntaxFactory.ExpressionStatement(assignmentExpressionSyntax.WithoutTrailingTrivia()).WithTrailingTrivia(trailingTrivias[idx++]));
            }

            var returnStatement = SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(" __ret"))
                .WithLeadingTrivia(fieldLeadingTrivia); //.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            statements = statements.Add(returnStatement);

            var closingBlockTrivia = overrideVisit.Initializer.Expressions.Last().GetTrailingTrivia();

            var blockStatement = SyntaxFactory.Block(statements)
                .WithOpenBraceToken(CreateToken(SyntaxKind.OpenBraceToken, " {").WithTrailingTrivia(openingBlockTrivia))
                .WithTrailingTrivia(closingBlockTrivia);
            // .WithoutTrailingTrivia();

            var closingTrivia = SyntaxFactory.TriviaList(fieldLeadingTrivia.SkipLast(1));
            return CreateIIFE(node.GetLeadingTrivia(), blockStatement, closingTrivia);
        }
    }

    /// <summary>
    /// Converts anonymous object to JS value object by removing "new" keyword, and adding parenthasis around braces for the inline function case. 
    /// </summary>
    public override SyntaxNode VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
    {
        var overrideVisit = (AnonymousObjectCreationExpressionSyntax) base.VisitAnonymousObjectCreationExpression(node);
        overrideVisit = overrideVisit.WithNewKeyword(SyntaxFactory.MissingToken(SyntaxKind.NewKeyword));
        
        if (node.Parent is not SimpleLambdaExpressionSyntax) return overrideVisit;
        
        // Attach parenthasis to braces
        return overrideVisit
            .WithOpenBraceToken(CreateToken(SyntaxKind.OpenBraceToken, "({")
                .WithLeadingTrivia(overrideVisit.OpenBraceToken.LeadingTrivia)
                .WithTrailingTrivia(overrideVisit.OpenBraceToken.TrailingTrivia))
            .WithCloseBraceToken(CreateToken(SyntaxKind.CloseBraceToken, "})")
                .WithLeadingTrivia(overrideVisit.CloseBraceToken.LeadingTrivia)
                .WithTrailingTrivia(overrideVisit.CloseBraceToken.TrailingTrivia));
    }

    /// <summary>
    /// 
    /// </summary>
    public override SyntaxNode VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
    {
        var overrideVisit = (AnonymousObjectMemberDeclaratorSyntax) base.VisitAnonymousObjectMemberDeclarator(node);

        if (overrideVisit.NameEquals != null)
            return overrideVisit.WithNameEquals(overrideVisit.NameEquals.WithEqualsToken(CreateToken(SyntaxKind.EqualsToken, ": ")));
        else
        {
            var expressionString = overrideVisit.Expression.ToString();
            var name = expressionString[(expressionString.LastIndexOf(".") + 1)..];
            return overrideVisit.WithNameEquals(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(name), CreateToken(SyntaxKind.EqualsToken, ": ")));
        }
    }

    private TypeSyntax GetFullTypeSyntax(TypeSyntax type)
    {
        var typeSymbol = SemanticModel.SyntaxTree.GetRoot().Contains(type) ? SemanticModel.GetTypeInfo(type).Type ?? SemanticModel.GetSymbolInfo(type).Symbol as ITypeSymbol : null;
        var containingPath = TypeTranslation.GetContainingTypesPath(typeSymbol);
        if (containingPath != null) type = SyntaxFactory.ParseTypeName(containingPath + "." + ((IdentifierNameSyntax)type).Identifier.Text);
        return type;
    }

    public override SyntaxNode VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        var typeSymbol = ((IMethodSymbol)ModelExtensions.GetSymbolInfo(SemanticModel, node).Symbol).ContainingType;
        var typeSyntax = SyntaxFactory.ParseTypeName(typeSymbol.Name);

        ImportedSymbols.Add(typeSymbol);

        return VisitObjectCreationExpression(SyntaxFactory.ObjectCreationExpression(typeSyntax.WithLeadingTrivia(SyntaxFactory.Space), node.ArgumentList,
            node.Initializer != null ? (InitializerExpressionSyntax)VisitInitializerExpression(node.Initializer) : null), false);
    }

    private static LocalDeclarationStatementSyntax CreateLocalDeclaration(string variableName, ExpressionSyntax value, IEnumerable<SyntaxTrivia> leadingTrivia)
    {
        return SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("let").WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(SyntaxFactory.Space),
            new SeparatedSyntaxList<VariableDeclaratorSyntax>().Add(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(variableName + " "), null,
                    SyntaxFactory.EqualsValueClause(value.WithLeadingTrivia(SyntaxFactory.Space)))))).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }
}