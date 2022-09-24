using System.Text.RegularExpressions;
using DotBond.IntegratedQueryRuntime;
using DotBond.SyntaxRewriter.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public Rewriter(SemanticModel semanticModel) : base(semanticModel)
    {
    }

    private bool _areNestedClassesBeingCaptured = false;

    /// <summary>
    /// Cleans up class declaration by:
    /// - Using export when it's public or protected
    /// - Removing private token if it's private
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var isTopLevelClass = false;

        if (_areNestedClassesBeingCaptured == false)
        {
            isTopLevelClass = true;
            _areNestedClassesBeingCaptured = true;
        }

        var overrideVisit = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
        var accessToken = overrideVisit.Modifiers.FirstOrDefault();
        var newAccessToken = accessToken.IsKind(SyntaxKind.None) ? accessToken : CreateToken(SyntaxKind.PublicKeyword, accessToken.LeadingTrivia + "export ");
        var abstractToken = overrideVisit.Modifiers.FirstOrDefault(e => e.IsKind(SyntaxKind.AbstractKeyword));
        var tokens = !abstractToken.IsKind(SyntaxKind.None) ? new[] { newAccessToken, abstractToken } : new[] { newAccessToken };

        overrideVisit = overrideVisit.WithModifiers(SyntaxFactory.TokenList(tokens))
            .WithIdentifier(overrideVisit.Identifier.WithTrailingTrivia(SyntaxFactory.Space))
            .WithOpenBraceToken(overrideVisit.OpenBraceToken.WithLeadingTrivia());

        if (isTopLevelClass)
        {
            var (nestedClasses, prunedNode) = ExtractNestedClasses(overrideVisit, true);
            if (nestedClasses != null)
            {
                nestedClasses = TsRegexRepository.EmptyLinesRx.Replace(nestedClasses, "");
                overrideVisit = ((ClassDeclarationSyntax)prunedNode).WithTrailingTrivia(SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, "\n\n" + nestedClasses));
            }

            _areNestedClassesBeingCaptured = false;
        }

        var tsOverloads = overrideVisit.DescendantNodes().OfType<MethodDeclarationSyntax>().GroupBy(e => e.Identifier.Text).Where(e => e.Count() > 1).ToList();
        foreach (var overload in tsOverloads)
        {
            var declarations = new List<string>();

            var idx = 0;
            overrideVisit = overrideVisit.ReplaceNodes(overload, (method, _) =>
            {
                declarations.Add(method.WithBody(null).ToString());
                return method.WithIdentifier(SyntaxFactory.Identifier(method.Identifier.Text + idx++))
                    .WithModifiers(SyntaxFactory.TokenList(method.Modifiers
                        .Select(e => e.IsKind(SyntaxKind.PublicKeyword) ? SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithLeadingTrivia(e.LeadingTrivia).WithTrailingTrivia(e.TrailingTrivia) : e)));
            });

            var paramRx = new Regex(@$"(?<name>\w+)\s*:\s*(?<type>{TsRegexRepository.MatchBrackets(BracketType.CurlyBrackets)}(\[\])?|(?:\s*\w+\[?\]?))\s*(?:,|\))");
            var minimumParameters = -1;

            // Compose into a single declaration
            var compositeDeclarations = declarations.Aggregate(new List<(string Name, List<string> Types)>(), (acc, curr) =>
            {
                var parameters = paramRx.Matches(curr).Select(e => (Name: e.Groups["name"].Value, Type: e.Groups["type"].Value.Trim())).ToList();
                if (minimumParameters == -1 || parameters.Count < minimumParameters) minimumParameters = parameters.Count;

                acc = acc.Select((e, i) => (e.Name == parameters[i].Name ? e.Name : e.Name + parameters[i].Name, e.Types.Append(parameters[i].Type).ToList())).ToList();
                acc.AddRange(parameters.Skip(acc.Count).Select(tuple => (tuple.Name, new List<string> { tuple.Type })));
                return acc;
            });

            // Apply nullable parameters
            compositeDeclarations = compositeDeclarations.Where(e => e.Types.Count == minimumParameters)
                .Concat(compositeDeclarations.Where(e => e.Types.Count != minimumParameters).Select(tuple => (tuple.Name + "?", tuple.Types))).ToList();


            var csOverloads = node.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => CamelCaseConversion.LowercaseWord(e.Identifier) == overload.Key).ToList();
            var isPublic = csOverloads.Any(e => e.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));
            var parameters = string.Join(", ", compositeDeclarations.Select(e => e.Name + ": " + string.Join(" | ", e.Types.Distinct())));
            var returnType = string.Join(" | ",
                declarations.Select(e => Regex.Match(e, @$"(?<=\w+{TsRegexRepository.MatchBrackets(BracketType.Parenthasis)}:\s*).+?(?=\s*;)").Value.TrimStart()).Distinct());

            var leadingTrivia = csOverloads.First().GetLeadingTrivia().Last().ToFullString();

            var maxParameters = csOverloads.Max(e => e.ParameterList.Parameters.Count);
            var parameterTuple =
                SyntaxFactory.TupleExpression(CreateArgumentList(compositeDeclarations.Select(e => SyntaxFactory.IdentifierName(e.Name.Replace("?", "")) as ExpressionSyntax).ToArray()).Arguments);
            var arms =
                SyntaxFactory.SeparatedList(csOverloads.Select((e, idx) =>
                    SyntaxFactory.SwitchExpressionArm(SyntaxFactory.RecursivePattern(null,
                            SyntaxFactory.PositionalPatternClause(SyntaxFactory.SeparatedList(e.ParameterList.Parameters.Select(p => SyntaxFactory.Subpattern(SyntaxFactory.TypePattern(p.Type)))
                                .Concat(Enumerable.Repeat(SyntaxFactory.Subpattern(SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
                                    maxParameters - e.ParameterList.Parameters.Count)))),
                            null,
                            null),
                        SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("this." + CamelCaseConversion.LowercaseWord(e.Identifier) + idx),
                            CreateArgumentList(e.ParameterList.Parameters.Select(e => SyntaxFactory.IdentifierName(e.Identifier.Text)).ToArray()))).WithLeadingTrivia(leadingTrivia + "\t")));

            var expressionBody = SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.SwitchExpression(
                parameterTuple,
                arms
            )).WithLeadingTrivia("\n" + leadingTrivia + "\t")).WithCloseBraceToken(CreateToken(SyntaxKind.CloseBraceToken, "\n" + leadingTrivia + "}"));

            var visitedExpressionBody = (BlockSyntax)VisitBlock(expressionBody);


            var overloadImplementationFunction = SyntaxFactory.MethodDeclaration(default, csOverloads.First().Modifiers, SyntaxFactory.ParseTypeName(""), null, SyntaxFactory.Identifier(overload.Key),
                    default,
                    SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameters)) }))
                        .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, "): " + returnType)),
                    default,
                    visitedExpressionBody,
                    null
                )
                .WithLeadingTrivia(csOverloads.First().GetLeadingTrivia().Last(),
                    SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, string.Join("\n" + leadingTrivia, declarations) + "\n" + leadingTrivia))
                .WithTrailingTrivia(csOverloads.First().GetTrailingTrivia().Append(SyntaxFactory.CarriageReturnLineFeed));
            

            var nodeToPlaceImplementationAfter = overrideVisit.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Last(m => m.Identifier.Text == overloadImplementationFunction.Identifier.Text + (overload.Count() - 1));
            overrideVisit = overrideVisit.InsertNodesAfter(nodeToPlaceImplementationAfter, new[] { overloadImplementationFunction });
        }

        return overrideVisit;
    }

    /// <summary>
    /// Gets nested classes syntax, what is not allowed in TS, and returns their namespace, name and string syntax.
    /// </summary>
    private (string StringContent, SyntaxNode Node) ExtractNestedClasses(SyntaxNode classOrRecordDeclaration, bool isTopLevel = false)
    {
        // as opposed of enum
        var isClass = classOrRecordDeclaration is ClassDeclarationSyntax;
        var nodeName = isClass ? ((ClassDeclarationSyntax)classOrRecordDeclaration).Identifier.Text : ((RecordDeclarationSyntax)classOrRecordDeclaration).Identifier.Text;

        var descendantNodes = classOrRecordDeclaration.DescendantNodes().ToList();
        var childClassOrRecordNodes = descendantNodes.OfType<RecordDeclarationSyntax>().Cast<SyntaxNode>().Concat(descendantNodes.OfType<ClassDeclarationSyntax>()).ToList();

        // var visitedDeclaration = rewriter.Visit(classOrRecordDeclaration);
        var extractedChildrenClasses = childClassOrRecordNodes.Any() ? string.Join("\n", childClassOrRecordNodes.Select(e => ExtractNestedClasses(e).StringContent)) : null;
        classOrRecordDeclaration = classOrRecordDeclaration.RemoveNodes(childClassOrRecordNodes, SyntaxRemoveOptions.KeepNoTrivia)!;

        var currentClassDefinition = isTopLevel ? null : classOrRecordDeclaration.ToFullString();

        var result = !childClassOrRecordNodes.Any()
            ? (currentClassDefinition != null ? FixDeclarationWhitespace(currentClassDefinition) : null)
            : $@"
{currentClassDefinition}

export namespace {nodeName} {{
{extractedChildrenClasses}
}}
";
        return (result, classOrRecordDeclaration);
    }


    public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        var modifiers = node.Modifiers.Select(e => e.IsKind(SyntaxKind.PublicKeyword) ? CreateToken(SyntaxKind.PublicKeyword, "export ") : e);
        return ((InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)).WithModifiers(SyntaxFactory.TokenList(modifiers));
    }

    public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
    {
        var overrideVisit = ((StructDeclarationSyntax)base.VisitStructDeclaration(node)).WithKeyword(CreateToken(SyntaxKind.StructKeyword, "class "));
        var accessModifier = node.Modifiers.First(e => e.IsKind(SyntaxKind.PublicKeyword));
        return !accessModifier.IsKind(SyntaxKind.None) ? overrideVisit.WithModifiers(SyntaxFactory.TokenList(CreateToken(SyntaxKind.PublicKeyword, "export "))) : overrideVisit.WithModifiers(default);
    }

    /// <summary>
    /// Converts C# record into Typescript class
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var overrideVisit = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!;
        var accessToken = node.Modifiers.FirstOrDefault();
        var newAccessToken = accessToken.IsKind(SyntaxKind.None) ? accessToken : CreateToken(SyntaxKind.PublicKeyword, accessToken.LeadingTrivia + "export ");

        // var baseRecord = (node.BaseList?.Types.FirstOrDefault()?.Type as IdentifierNameSyntax)?.Identifier.Text;

        var baseListArguments = node.BaseList?.Types.Where(e => e is PrimaryConstructorBaseTypeSyntax).Cast<PrimaryConstructorBaseTypeSyntax>()
            .SelectMany(e => e.ArgumentList.Arguments.Where(ee => ee.Expression is IdentifierNameSyntax).Select(ee => ((IdentifierNameSyntax)ee.Expression).Identifier.Text));

        var members = new SyntaxList<MemberDeclarationSyntax>(overrideVisit.Members.Select(e => e.WithLeadingTrivia(SyntaxFactory.Tab)));

        var isMemberInherited = (ParameterSyntax p) => baseListArguments?.Contains(p.Identifier.Text.Contains(':') ? p.Identifier.Text[..p.Identifier.Text.IndexOf(":")] : p.Identifier.Text) ?? false;

        var nonInheritedParameters = overrideVisit.ParameterList!.Parameters.Where(p => !isMemberInherited(p));

        var inheritedParameters = overrideVisit.ParameterList.Parameters.Where(isMemberInherited).ToList();

        var constructor = @$"
    constructor({string.Join(", ", nonInheritedParameters.Select(m => m.ToString().Split(":")).Select(m => $"public {CamelCaseConversion.LowercaseWord(m[0])}:{m[1]}")
        .Concat(inheritedParameters.Select(p => p.Identifier.Text)))}) {{
        {(node.BaseList != null ? $"super({string.Join(", ", inheritedParameters.Select(p => p.Identifier.Text.Contains(':') ? p.Identifier.Text[..p.Identifier.Text.IndexOf(":")] : p.Identifier.Text))})" : "")}
    }}
";

        overrideVisit = overrideVisit
            .WithModifiers(SyntaxFactory.TokenList(newAccessToken))
            .WithKeyword(CreateToken(SyntaxKind.RecordKeyword, "class"))
            .WithClassOrStructKeyword(CreateToken(SyntaxKind.ClassKeyword))
            .WithIdentifier(node.Identifier.WithLeadingTrivia(SyntaxFactory.Whitespace(" ")).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")))
            .WithParameterList(null)
            .WithMembers(members)
            .WithSemicolonToken(CreateToken(SyntaxKind.SemicolonToken, ""));

        // Indentation fixup
        if (overrideVisit.OpenBraceToken == default)
            overrideVisit = overrideVisit
                .WithOpenBraceToken(CreateToken(SyntaxKind.OpenBraceToken, "{\n").WithTrailingTrivia(SyntaxFactory.Whitespace(constructor)))
                .WithCloseBraceToken(CreateToken(SyntaxKind.CloseBraceToken, "\n}\n"));
        else
        {
            overrideVisit = overrideVisit.WithOpenBraceToken(overrideVisit.OpenBraceToken.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Whitespace(constructor)));
            overrideVisit = overrideVisit.WithCloseBraceToken(overrideVisit.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.Space));
        }

        return overrideVisit;
    }

    public override SyntaxNode VisitBaseList(BaseListSyntax node)
    {
        node.Types.Select(e => e.Type).ToList().ForEach(GetSymbolsFromTypeSyntax);

        if (node.Parent.IsKind(SyntaxKind.RecordDeclaration))
        {
            var newTypes = new SeparatedSyntaxList<BaseTypeSyntax>();
            newTypes = newTypes.AddRange(node.Types.Select(type =>
                type is PrimaryConstructorBaseTypeSyntax constructor
                    ? constructor.WithArgumentList(SyntaxFactory.ArgumentList().WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken)).WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken)))
                    : type));
            node = node.WithTypes(newTypes);
        }

        var baseType = node.Types.FirstOrDefault(e => SemanticModel.GetTypeInfo(e.Type).Type!.TypeKind == TypeKind.Class);
        var implementedInterfaces = node.Types.Where(e => SemanticModel.GetTypeInfo(e.Type).Type!.TypeKind == TypeKind.Interface).Select(e => e.Type.ToString()).ToList();

        var newBaseList = baseType != null
            ? node.WithColonToken(CreateToken(SyntaxKind.ColonToken, "extends "))
                .WithTypes(SyntaxFactory.SeparatedList(new[] { baseType }))
            : node.WithTypes(default).WithColonToken(SyntaxFactory.MissingToken(SyntaxKind.ColonToken));

        if (implementedInterfaces.Any())
            newBaseList = newBaseList.WithTrailingTrivia(node.GetTrailingTrivia().Prepend($" implements {string.Join(", ", implementedInterfaces)}"));

        return newBaseList;
    }

    public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var overrideVisit = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
        if (overrideVisit.ExpressionBody != null)
        {
            overrideVisit = overrideVisit.WithBody(SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(overrideVisit.ExpressionBody.Expression)));
            overrideVisit = overrideVisit.WithExpressionBody(null);
        }

        return overrideVisit.WithIdentifier(SyntaxFactory.Identifier("constructor"));
    }

    public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        var returnType = node.Type;
        var parameterList = node.ParameterList;
        var methodName = returnType.GetText().ToString() == ((ClassDeclarationSyntax)node.Parent).Identifier.Text
            ? $"From{parameterList.Parameters.First().Type.GetText()}"
            : $"To{returnType.GetText()}";
        var overrideBody = node.Body != null ? VisitBlock(node.Body) as BlockSyntax : null;
        var overrideExpressionBody = node.ExpressionBody != null ? VisitArrowExpressionClause(node.ExpressionBody) : null;

        if (overrideExpressionBody is BlockSyntax block)
            overrideBody = block;

        var newMethod = SyntaxFactory.MethodDeclaration(
            new SyntaxList<AttributeListSyntax>(),
            SyntaxFactory.TokenList(CreateToken(SyntaxKind.PublicKeyword, node.GetLeadingTrivia().First() + "public")),
            returnType,
            null,
            SyntaxFactory.Identifier($" {methodName} "),
            null,
            parameterList,
            new SyntaxList<TypeParameterConstraintClauseSyntax>(),
            overrideBody,
            overrideExpressionBody as ArrowExpressionClauseSyntax,
            CreateToken(SyntaxKind.SemicolonToken, ";")
        ).WithTrailingTrivia(node.GetTrailingTrivia());

        return base.Visit(newMethod);
    }

    private static Regex ExportOffsetRx = new(@"(?<=\n)\s*(?=export)");
    private static Regex NonExportOffsetRx = new(@"^([^\S\r\n]*)(?!export)(?=\S+)", RegexOptions.Multiline);

    private static string FixDeclarationWhitespace(string declaration)
    {
        var offset = ExportOffsetRx.Match(declaration).Value;
        declaration = NonExportOffsetRx.Replace(declaration, "$1" + offset);
        return declaration;
    }
}