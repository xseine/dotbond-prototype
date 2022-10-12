using ConsoleApp1.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBond.SyntaxRewriter.Core;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    // Changes position of property's type (and translates it). Adds getter and setter if needed.
    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var overrideVisit = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;

        var accessorList = overrideVisit.AccessorList;


        var leadingTrivia = overrideVisit.GetLeadingTrivia().LastOrDefault();
        var trailingTrivia = overrideVisit.GetTrailingTrivia();

        var parsedType = TypeTranslation.ParseType(node.Type, SemanticModel); // overrideVisit.Type throws exception for not being in Compilation?

        // Adding get/set to class declaration
        RegisterAncestorRewrite(syntaxNode =>
        {
            var classDeclaration = (ClassDeclarationSyntax)syntaxNode;

            if (accessorList == null)
                return classDeclaration;

            foreach (var accessor in accessorList.Accessors.Where(accessor => accessor.Body != null || accessor.ExpressionBody != null))
            {
                var isGet = accessor.Kind() == SyntaxKind.GetAccessorDeclaration;

                var methodName = isGet ? "get" : "set";
                // Getter gets the return type assigned, setter gets the value as a parameter
                var parameters = isGet
                    ? SyntaxFactory.ParameterList().WithCloseParenToken(SyntaxFactory.Token(default, SyntaxKind.CloseParenToken, "): " + parsedType, "): " + parsedType, default))
                    : SyntaxFactory.ParameterList(new SeparatedSyntaxList<ParameterSyntax>().Add(SyntaxFactory.Parameter(SyntaxFactory.ParseToken("value: " + parsedType))));

                var expressionBody = accessor.Body ?? SyntaxFactory.Block(SyntaxFactory.ReturnStatement(accessor.ExpressionBody!.Expression.WithLeadingTrivia(SyntaxFactory.Space)));

                classDeclaration = classDeclaration.AddMembers(
                    SyntaxFactory.MethodDeclaration(default, default, SyntaxFactory.ParseTypeName(methodName + " "), null!,
                            SyntaxFactory.ParseToken(overrideVisit.Identifier.Text),
                            null!, parameters, default,
                            expressionBody.WithLeadingTrivia(SyntaxFactory.Space),
                            SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken))
                        .ChangeIdentifierToCamelCase()
                        .WithLeadingTrivia(leadingTrivia)
                        .WithTrailingTrivia(trailingTrivia));
            }

            return classDeclaration;
        }, SyntaxKind.ClassDeclaration);

        GetSymbolsFromTypeSyntax(node.Type);

        // If accessors are defined, getter, setter and private field will be used instead
        if (accessorList?.Accessors.Any(accessor => accessor.Body != null || accessor.ExpressionBody != null) is true)
            return null;

        // Those with expression body are: MyProp => some expression (get-only properties) are handled specially
        if (overrideVisit.ExpressionBody != null)
            overrideVisit = overrideVisit
                .WithType(SyntaxFactory.ParseTypeName("get "))
                .WithLeadingTrivia(leadingTrivia)
                .WithAccessorList(null).WithoutTrailingTrivia()
                .WithExpressionBody(overrideVisit.ExpressionBody.WithArrowToken(CreateToken(SyntaxKind.EqualsGreaterThanToken, "() { return ")))
                .WithSemicolonToken(CreateToken(SyntaxKind.SemicolonToken, "; }")).WithTrailingTrivia(trailingTrivia);
        else
            overrideVisit = overrideVisit
                .WithType(SyntaxFactory.ParseTypeName(""))
                .WithIdentifier(SyntaxFactory.Identifier(overrideVisit.Identifier + ": " + parsedType))
                .WithLeadingTrivia(leadingTrivia)
                .WithAccessorList(null).WithoutTrailingTrivia().WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithTrailingTrivia(trailingTrivia);

        overrideVisit = overrideVisit.ChangeIdentifierToCamelCase();
        return overrideVisit;
    }

    // Doing similar things as above in a different way (necessary)
    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var overrideVisit = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node)!;

        var leadingTrivia = overrideVisit.GetLeadingTrivia().LastOrDefault();
        var trailingTrivia = overrideVisit.GetTrailingTrivia();

        var field = overrideVisit.Declaration.Variables.First();
        var fieldWithNewName = field.WithIdentifier(
            SyntaxFactory.Identifier(field.Identifier.Text + ": " + TypeTranslation.ParseType(node.Declaration.Type, SemanticModel)));

        // No "const" on fields in TS, but use static if parent is static
        var isClassStatic = ((ClassDeclarationSyntax)node.Parent).Modifiers.Any(e => e.IsKind(SyntaxKind.StaticKeyword));
        var modifiers = isClassStatic
            ? SyntaxFactory.TokenList(overrideVisit.Modifiers.Select(e => e.IsKind(SyntaxKind.ConstKeyword) ? CreateToken(SyntaxKind.StaticKeyword, "static ") : e))
            : SyntaxFactory.TokenList(overrideVisit.Modifiers.Where(e => !e.IsKind(SyntaxKind.ConstKeyword)));

        overrideVisit = overrideVisit
            .WithDeclaration(overrideVisit.Declaration.WithType(SyntaxFactory.ParseTypeName("")).WithVariables(SyntaxFactory.SeparatedList(new[] { fieldWithNewName })))
            .WithLeadingTrivia(leadingTrivia)
            .WithoutTrailingTrivia().WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithTrailingTrivia(trailingTrivia)
            .WithModifiers(modifiers);

        overrideVisit = overrideVisit.ChangeIdentifierToCamelCase();

        return overrideVisit;
    }

    // Changes position of the return type (and translates it).
    // Replaces expression body with the block statement
    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var overrideVisit = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;

        var leadingTrivia = overrideVisit.GetLeadingTrivia().LastOrDefault();
        var trailingTrivia = overrideVisit.GetTrailingTrivia();

        if (overrideVisit.ExpressionBody != null)
        {
            overrideVisit = overrideVisit.WithBody(SyntaxFactory.Block(SyntaxFactory
                .ReturnStatement(overrideVisit.ExpressionBody.Expression.WithLeadingTrivia(SyntaxFactory.Space))
                .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed, leadingTrivia, leadingTrivia)));
            overrideVisit = overrideVisit.WithExpressionBody(null);
        }

        var parsedReturnType = TypeTranslation.ParseType(node.ReturnType, SemanticModel) + " ";

        overrideVisit = overrideVisit
            .WithReturnType(SyntaxFactory.ParseTypeName(""))
            .WithLeadingTrivia(leadingTrivia)
            .WithoutTrailingTrivia()
            .WithParameterList(overrideVisit.ParameterList.WithCloseParenToken(
                SyntaxFactory.Token(default, SyntaxKind.CloseParenToken, "): " + parsedReturnType, "): " + parsedReturnType, default)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithTrailingTrivia(trailingTrivia)
            .ChangeIdentifierToCamelCase();

        return overrideVisit;
    }
}