using ConsoleApp1.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.Misc;
using Translator.SyntaxRewriter.Core;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace Translator.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    // Use "let" for variable declaration
    public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        if (!node.Parent.IsKind(SyntaxKind.LocalDeclarationStatement)) return base.VisitVariableDeclaration(node)!;

        var baseVisit = base.VisitVariableDeclaration(node) as VariableDeclarationSyntax;

        var (a, b, (c, d)) = (baseVisit.Type, new[] { 1, 2 }.ToList(), (1, 2));
        // a.
        return baseVisit?.WithType(SyntaxFactory.IdentifierName("let").WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(SyntaxFactory.Space));
    }

    // Add "this" where needed
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbol = GetSymbol(node);

        if (symbol == null) return base.VisitIdentifierName(node);
        if (symbol.Kind is not (SymbolKind.Property or SymbolKind.Field or SymbolKind.Method) || symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
            return base.VisitIdentifierName(node);

        var parent = node.Parent!;
        if (parent is NameEqualsSyntax or NameColonSyntax) return base.VisitIdentifierName(node);

        var hasDotBeforeIt = parent.DescendantNodesAndTokens().TakeWhile(nodeOrToken => nodeOrToken != node).Reverse()
            .TakeWhile(nodeOrToken => nodeOrToken.IsKind(SyntaxKind.DotToken) || nodeOrToken.IsKind(SyntaxKind.WhitespaceTrivia))
            .Any(nodeOrToken => nodeOrToken.IsKind(SyntaxKind.DotToken));

        var isInsideObjectInitializer = parent.IsKind(SyntaxKind.SimpleAssignmentExpression) && (parent.Parent?.IsKind(SyntaxKind.ObjectInitializerExpression) ?? false);

        if (hasDotBeforeIt == false && isInsideObjectInitializer == false)
            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("this"),
                node.ChangeIdentifierToCamelCase().WithoutLeadingTrivia())
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());

        return base.VisitIdentifierName(node).ChangeIdentifierToCamelCase();
    }


    public override SyntaxNode VisitAttributeList(AttributeListSyntax node)
    {
        Attributes.UnionWith(node.Attributes.Select(attr => attr.Name.ToString()).Select(attrName => char.ToLower(attrName[0]) + attrName[1..]));

        var typescriptDecorators = string.Join("\n\t", node.Attributes.Select(attr =>
        {
            var attrName = attr.Name.ToString();

            var parameters = attr.ArgumentList?.Arguments
                .GroupBy(arg => arg.NameEquals != null)
                .Select(group => group.Key ?
                    $"{{ {string.Join(", ", group.Select(arg => $"{arg.NameEquals!.Name}: {base.Visit(arg.Expression)}"))} }}" :
                    string.Join(", ", group.Select(arg => base.Visit(arg.Expression))));

            return $@"@{char.ToLower(attrName[0]) + attrName[1..]}({(parameters != null ? string.Join(", ", parameters) : null)})";
        }));

        return SyntaxFactory.AttributeList()
            .WithOpenBracketToken(CreateToken(SyntaxKind.OpenBracketToken))
            .WithCloseBracketToken(CreateToken(SyntaxKind.CloseBracketToken))
            .WithLeadingTrivia(SyntaxFactory.Whitespace(node.GetLeadingTrivia() + typescriptDecorators + node.GetTrailingTrivia()));
    }


    // Translates parameters' types
    public override SyntaxNode VisitParameter(ParameterSyntax node)
    {
        var overrideVisit = (ParameterSyntax)base.VisitParameter(node)!;
        var parsedType = overrideVisit.Type != null ? TypeTranslation.ParseType(overrideVisit.Type, SemanticModel) : null;
        var identifier = SyntaxFactory.Identifier(overrideVisit.Identifier + (parsedType != null ? ": " + parsedType : null));
        GetSymbolsFromTypeSyntax(node.Type!);
        return overrideVisit.WithIdentifier(identifier).WithType(null).WithTrailingTrivia(SyntaxFactory.Space);
    }

    // Removes return type of local function, and adds the function keyword
    public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var overrideVisit = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node);
        if (overrideVisit.ExpressionBody != null)
        {
            overrideVisit = overrideVisit.WithBody(SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(overrideVisit.ExpressionBody.Expression)));
            overrideVisit = overrideVisit.WithExpressionBody(null);
        }
        
        return overrideVisit.WithModifiers(default).WithReturnType(SyntaxFactory.ParseTypeName("function ")).WithLeadingTrivia(node.GetLeadingTrivia());
    }

    // Removes type casting (int), (string), ...
    public override SyntaxNode VisitCastExpression(CastExpressionSyntax node)
    {
        var overrideVisit = (CastExpressionSyntax)base.VisitCastExpression(node)!;

        return overrideVisit.Expression;
    }

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.AsExpression))
        {
            var overrideVisit = (BinaryExpressionSyntax)base.VisitBinaryExpression(node);
            return overrideVisit.Left.WithTrailingTrivia(node.GetTrailingTrivia());
        }

        if (node.IsKind(SyntaxKind.IsExpression))
        {
            var overrideVisit = (BinaryExpressionSyntax)base.VisitBinaryExpression(node);
            return RewriteIsExpression(overrideVisit);
        }

        // Division of integers is rounded in C#
        if (node.IsKind(SyntaxKind.DivideExpression))
        {
            var isLeftOperandDecimal = SemanticModel.GetTypeInfo(node.Left).Type.Name is "Float" or "Double" or "Decimal";
            var isRightOperandDecimal = SemanticModel.GetTypeInfo(node.Right).Type.Name is "Float" or "Double" or "Decimal";

            if (!isLeftOperandDecimal && !isRightOperandDecimal)
            {
                var overrideVisit = base.VisitBinaryExpression(node) as BinaryExpressionSyntax;
                return SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Math"), SyntaxFactory.IdentifierName("floor")),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new []{SyntaxFactory.Argument(overrideVisit)})));
            }
        }

        return base.VisitBinaryExpression(node);
    }

    /// <summary>
    /// Converts "is" operator for checking types. This is not an "is" pattern expression, but it is very similar.
    /// Predefined primitive types are handled by the typeof operator, and reference types are checked by their name.
    /// </summary>
    /// <param name="overrideVisit"></param>
    /// <returns><see cref="IsPatternExpressionSyntax"/>, with the appropriate syntax</returns>
    public ExpressionSyntax RewriteIsExpression(BinaryExpressionSyntax overrideVisit)
    {
        var isToken = overrideVisit.OperatorToken;
        overrideVisit = overrideVisit.WithOperatorToken(SyntaxFactory.Token(isToken.LeadingTrivia, SyntaxKind.IsKeyword, "==", "", isToken.TrailingTrivia));
        var left = overrideVisit.Left;
        var right = overrideVisit.Right;
        
        if (overrideVisit.Right is IdentifierNameSyntax id)
        {
            var subClasses = RoslynUtilities.GetSubclasses(SemanticModel.Compilation, id.Identifier.Text);
            var leftWithTrivia = left.WithTrailingTrivia(left.GetTrailingTrivia().Prepend("?.constructor"));

            var exactConstructorCheck = (SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, leftWithTrivia, id));
            
            if (subClasses.Count == 0) return exactConstructorCheck;

            leftWithTrivia = leftWithTrivia.WithTrailingTrivia(leftWithTrivia.GetTrailingTrivia().ToArray()[1..].Prepend("?.constructor.name"));
            
            return SyntaxFactory.ParenthesizedExpression(subClasses.Aggregate(exactConstructorCheck,
                (acc, curr) => SyntaxFactory.BinaryExpression(SyntaxKind.LogicalOrExpression, acc.WithTrailingTrivia(" "),
                    SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, leftWithTrivia, SyntaxFactory.IdentifierName($" '{curr}'")).WithLeadingTrivia(" ")))
                .WithLeadingTrivia());
        }
        
        
        var (rhs, isPrimitive) = overrideVisit.Right switch
        {
            PredefinedTypeSyntax { Keyword.Text: not "DateTime" } predefined => (TypeTranslation.ParseType(predefined, SemanticModel), true),
            PredefinedTypeSyntax { Keyword.Text: "DateTime" } => ("Date", false),
            GenericNameSyntax generic => (generic.ToString(), false)
        };


        overrideVisit = overrideVisit
            .WithLeft(isPrimitive ? left.WithLeadingTrivia(left.GetLeadingTrivia().Append("typeof ")) : left.WithTrailingTrivia(left.GetTrailingTrivia().Prepend("?.constructor")))
            .WithRight(SyntaxFactory.ParseExpression(isPrimitive ? $"'{rhs}'" : $"{rhs}").WithLeadingTrivia(right.GetLeadingTrivia()).WithTrailingTrivia(right.GetTrailingTrivia()));


        return overrideVisit;
    }

    public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        var overrideVisit = (InterpolatedStringExpressionSyntax)base.VisitInterpolatedStringExpression(node)!;
        var isVerbatim = node.StringStartToken.Text.Contains('@');
        var startToken = isVerbatim ? "String.raw`" : "`";
        var interpolationStartToken = CreateToken(SyntaxKind.InterpolatedStringStartToken, startToken);
        var interpolationEndToken = CreateToken(SyntaxKind.InterpolatedStringEndToken, "`");

        if (isVerbatim)
        {
            var newContents = new SyntaxList<InterpolatedStringContentSyntax>(overrideVisit.Contents.Select(e =>
                e is InterpolatedStringTextSyntax text ?
                    text.WithTextToken(CreateToken(SyntaxKind.InterpolatedStringTextToken, text.TextToken.Text.Replace("\"\"", "\""))) :
                    e));
            overrideVisit = overrideVisit.WithContents(newContents);
        }

        return overrideVisit
            .WithStringStartToken(interpolationStartToken)
            .WithStringEndToken(interpolationEndToken);
    }

    public override SyntaxNode VisitInterpolation(InterpolationSyntax node)
    {
        var overrideVisit = (InterpolationSyntax)base.VisitInterpolation(node)!;
        var openBraceToken = CreateToken(SyntaxKind.OpenBraceToken, "${");
        return overrideVisit.WithOpenBraceToken(openBraceToken);
    }

    /// <summary>
    /// Handles special behavior of literal expressions.
    /// - Removes @ from strings.
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.Kind() == SyntaxKind.StringLiteralExpression && node.Token.Text.StartsWith("@"))
        {
            var newToken = CreateToken(SyntaxKind.StringLiteralToken, $"String.raw`{node.Token.Text[2..^1].Replace("\"\"", "\"")}`");
            return node.WithToken(newToken);
        }
        else if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            var type = ModelExtensions.GetTypeInfo(SemanticModel, node).Type.Name;
            return type switch
            {
                "int" or "double" or "float" or "Int32"
                    or "Int64" or "UInt32" or "UInt64" or "short" or "byte" or "long" or "decimal" => CreateNode("0"),
                "bool" => CreateNode("false"),
                "DateTime" or "DateTimeOffset" => CreateNode("new Date()"),
                // "Guid" or "String" => CreateNode("''"),
                _ => CreateNode("null")
            };
        }

        return base.VisitLiteralExpression(node);
    }

    public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node) => null;

    public override SyntaxNode VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        return ((FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node))
            .WithNamespaceKeyword(SyntaxFactory.MissingToken(SyntaxKind.NamespaceKeyword))
            .WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken))
            .WithName(SyntaxFactory.IdentifierName(""));
    }

    /// <summary>
    /// Convert to JS's throw 'errorMessage';
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitThrowStatement(ThrowStatementSyntax node)
    {
        var overrideVisit = (ThrowStatementSyntax)base.VisitThrowStatement(node);
        var argument = ((ObjectCreationExpressionSyntax)overrideVisit.Expression).ArgumentList.Arguments.FirstOrDefault();
        var message = argument.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            LiteralExpressionSyntax literal => literal.Token.Text,
            { } expression => expression.ToFullString()
        };

        return SyntaxFactory.ThrowStatement(SyntaxFactory.ParseExpression($" {message}")).WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
    }

    public override SyntaxNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, CreateToken(SyntaxKind.StringLiteralToken, $"'{TypeTranslation.ParseType(node.Type, SemanticModel)}'"));
    }
}