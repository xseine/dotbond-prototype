using System.Text.RegularExpressions;
using DotBond.Generators;
using DotBond.IntegratedQueryRuntime;
using DotBond.Misc;
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

        ClassDeclarationSyntax overrideVisit;

        var isWebApiController = isTopLevelClass && RoslynUtilities.InheritsFromController(SemanticModel.GetDeclaredSymbol(node));
        if (isWebApiController)
        {
            var everythingButClassOrRecord = node.ChildNodes().Where(e => !e.IsKind(SyntaxKind.ClassDeclaration) && !e.IsKind(SyntaxKind.RecordDeclaration));
            overrideVisit = node.RemoveNodes(everythingButClassOrRecord, SyntaxRemoveOptions.KeepNoTrivia);
            overrideVisit = (ClassDeclarationSyntax)base.VisitClassDeclaration(overrideVisit);
            overrideVisit = overrideVisit.WithIdentifier(SyntaxFactory.Identifier(overrideVisit.Identifier.Text + ApiGenerator.ControllerImportSuffix));
        }
        else
            overrideVisit = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);

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

        overrideVisit = (ClassDeclarationSyntax)HandleOverloads(node, overrideVisit);

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

    /// <summary>
    /// Structs should get an explicit parameterless constructor if one doesn't exist and it has other constructors defined.
    /// This is done so their definition is compatible with a class, which is what they are translated to.
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
    {
        var overrideVisit = ((StructDeclarationSyntax)base.VisitStructDeclaration(node)).WithKeyword(CreateToken(SyntaxKind.StructKeyword, "class "));

        var constructors = overrideVisit.ChildNodes().OfType<ConstructorDeclarationSyntax>().ToList();
        if (constructors.Any() && constructors.All(e => e.ParameterList.Parameters.Count != 0))
        {
            var parameterlessConstructor = constructors.First().WithBody(SyntaxFactory.Block()).WithParameterList(SyntaxFactory.ParameterList());
            overrideVisit = overrideVisit.InsertNodesBefore(constructors.First(), new[] { parameterlessConstructor });
        }

        overrideVisit = (StructDeclarationSyntax)HandleOverloads(node, overrideVisit);

        // Add "Equals" method
        if (node.ChildNodes().OfType<MethodDeclarationSyntax>().Any(e => e.Identifier.Text == "Equals") == false)
        {
            var structMembers = node.ChildNodes().OfType<PropertyDeclarationSyntax>().Select(e => CamelCaseConversion.LowercaseWord(e.Identifier))
                .Concat(node.ChildNodes().OfType<FieldDeclarationSyntax>()
                    .SelectMany(e => e.Declaration.Variables.Select(fieldDeclaration => CamelCaseConversion.LowercaseWord(fieldDeclaration.Identifier)))).ToList();
            var overrideBody = SyntaxFactory.Block(
                SyntaxFactory.ReturnStatement(structMembers.Skip(1).Aggregate(SyntaxFactory.ParseExpression($" this.{structMembers[0]} == obj.{structMembers[0]}"),
                    (acc, curr) => SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, acc, SyntaxFactory.ParseExpression($"this.{curr} == obj.{curr}")))));

            var newMethod = SyntaxFactory.MethodDeclaration(
                new SyntaxList<AttributeListSyntax>(),
                SyntaxFactory.TokenList(CreateToken(SyntaxKind.PublicKeyword, node.GetLeadingTrivia().FirstOrDefault() + "public")),
                SyntaxFactory.ParseTypeName("bool"),
                null,
                SyntaxFactory.Identifier($" equals "),
                null,
                SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier($"obj: {node.Identifier.Text}"))
                })),
                new SyntaxList<TypeParameterConstraintClauseSyntax>(),
                overrideBody,
                null,
                CreateToken(SyntaxKind.SemicolonToken, ";")
            ).WithTrailingTrivia(node.GetTrailingTrivia());

            var overrideVisitNewMethod = (MethodDeclarationSyntax)VisitMethodDeclaration(newMethod);
            ;
            var (leadingTrivia, trailingTrivia) = (node.ChildNodes().First().GetLeadingTrivia(), node.ChildNodes().First().GetTrailingTrivia());
            overrideVisit = overrideVisit.InsertNodesAfter(overrideVisit.ChildNodes().Last(), new[] { overrideVisitNewMethod.WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(trailingTrivia) });
        }

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

        if (node.Parent is TypeDeclarationSyntax { BaseList: { } })
        {
            var (leading, trailing) = overrideVisit.Body.Statements.FirstOrDefault() is { } first
                ? (first.GetLeadingTrivia(), first.GetTrailingTrivia())
                : (SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed), SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed));
            overrideVisit = overrideVisit.WithBody(overrideVisit.Body.WithStatements(
                overrideVisit.Body.Statements.Insert(0,
                    SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("super"))).WithLeadingTrivia(leading).WithTrailingTrivia(trailing))));
        }

        return overrideVisit.WithIdentifier(SyntaxFactory.Identifier("constructor"));
    }

    private TypeDeclarationSyntax HandleOverloads(TypeDeclarationSyntax node, TypeDeclarationSyntax overrideVisit)
    {
        var tsOverloads = overrideVisit.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => e.ReturnType is not IdentifierNameSyntax id || id.Identifier.Text.Trim() is not "get" or "set")
            .GroupBy(e => e.Identifier.Text, e => (BaseMethodDeclarationSyntax)e).Where(e => e.Count() > 1).ToList();

        if (overrideVisit.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Count() > 1)
            tsOverloads.Insert(0, overrideVisit.DescendantNodes().OfType<ConstructorDeclarationSyntax>().GroupBy(e => e.Identifier.Text).First());

        foreach (var entry in tsOverloads)
        {
            var isConstructorDeclaration = entry.Key == "constructor";
            var overloadName = entry.Key;
            var overload = entry.ToList();
            var overloadsWithUnknownParameterTypes = overload.Where(e => e.ParameterList.Parameters.Any(param => param.Identifier.Text.EndsWith(": any"))).ToList();

            if (overload.Count - overloadsWithUnknownParameterTypes.Count < 2)
            {
                overrideVisit = overrideVisit.RemoveNodes(overloadsWithUnknownParameterTypes, SyntaxRemoveOptions.KeepNoTrivia);
                continue;
            }


            var declarations = new List<string>();

            var idx = 0;
            var overloadsToRemoveIdx = new List<int>();
            overrideVisit = overrideVisit.ReplaceNodes(overload, (method, _) =>
            {
                if (overloadsWithUnknownParameterTypes.Contains(method))
                {
                    overloadsToRemoveIdx.Add(idx);
                    return null;
                }

                declarations.Add(method.WithBody(null).ToString());
                return ((dynamic)method).WithIdentifier(SyntaxFactory.Identifier(((dynamic)method).Identifier.Text + idx++))
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


            var csOverloads = (isConstructorDeclaration
                ? node.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Cast<BaseMethodDeclarationSyntax>()
                : node.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(e => CamelCaseConversion.LowercaseWord(((dynamic)e).Identifier) == overloadName)).ToList();

            csOverloads = csOverloads.Where((_, idx) => !overloadsToRemoveIdx.Contains(idx)).ToList();

            var isPublic = csOverloads.Any(e => e.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));
            var parameters = string.Join(", ", compositeDeclarations.Select(e => e.Name + ": " + string.Join(" | ", e.Types.Distinct())));
            var returnType = string.Join(" | ",
                declarations.Select(e => Regex.Match(e, @$"(?<=\w+{TsRegexRepository.MatchBrackets(BracketType.Parenthasis)}:\s*).+?(?=\s*;)").Value.TrimStart()).Distinct());
            
            var maxParameters = csOverloads.Max(e => e.ParameterList.Parameters.Count);
            var parameterTuple =
                SyntaxFactory.TupleExpression(CreateArgumentList(compositeDeclarations.Select(e => SyntaxFactory.IdentifierName(e.Name.Replace("?", "")) as ExpressionSyntax).ToArray()).Arguments);
            var wasParameterlessConstructorAddedInOverride = isConstructorDeclaration && tsOverloads[0].Count() > csOverloads.Count();

            var arms =
                SyntaxFactory.SeparatedList(csOverloads.Select((csOverload, idx) =>
                    SyntaxFactory.SwitchExpressionArm(SyntaxFactory.RecursivePattern(null,
                            SyntaxFactory.PositionalPatternClause(SyntaxFactory.SeparatedList(csOverload.ParameterList.Parameters
                                .Select(p => SyntaxFactory.Subpattern(SyntaxFactory.TypePattern(p.Type)))
                                .Concat(Enumerable.Repeat(SyntaxFactory.Subpattern(SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
                                    maxParameters - csOverload.ParameterList.Parameters.Count)))),
                            null,
                            null),
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName("this." + (isConstructorDeclaration
                                                                     ? "constructor"
                                                                     : (string)CamelCaseConversion.LowercaseWord(((dynamic)csOverload).Identifier))
                                                                 + (idx + (wasParameterlessConstructorAddedInOverride ? 1 : 0))),
                            CreateArgumentList(compositeDeclarations.Select(e => SyntaxFactory.IdentifierName(e.Name.Replace("?", ""))).ToArray())))));

            var switchExpression = SyntaxFactory.SwitchExpression(
                parameterTuple,
                arms
            );

            var expressionBody = isConstructorDeclaration
                ? SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(switchExpression))
                : SyntaxFactory.Block(SyntaxFactory.ReturnStatement(switchExpression));


            // Add super() in constructors, and remove it from other calls
            if (isConstructorDeclaration && node.Parent is TypeDeclarationSyntax { BaseList: { } })
            {
                expressionBody = expressionBody.WithStatements(
                    expressionBody.Statements.Insert(0,
                        SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("super")))
                            .WithTrailingTrivia("\n")));

                var superCalls = overrideVisit.DescendantNodes().OfType<ExpressionStatementSyntax>().Where(e => e.Expression is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "super" }
                });

                overrideVisit = overrideVisit.RemoveNodes(superCalls, SyntaxRemoveOptions.KeepNoTrivia);
            }

            // var hasSavedSymbols = TryGetSavedSymbolsToUse(node);
            var visitedExpressionBody = (BlockSyntax)VisitBlock(expressionBody);

            // if (hasSavedSymbols) ClearSavedSymbols(ref visitedExpressionBody);

            var overloadImplementationFunction = SyntaxFactory.MethodDeclaration(default, csOverloads.First().Modifiers, SyntaxFactory.ParseTypeName(""), null, SyntaxFactory.Identifier(overloadName),
                    default,
                    SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameters)) }))
                        .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken, isConstructorDeclaration ? ") " : "): " + returnType)),
                    default,
                    visitedExpressionBody,
                    null
                );


            var nodeToPlaceImplementationAfter = overrideVisit.DescendantNodes().OfType<BaseMethodDeclarationSyntax>()
                .Last(m => ((dynamic)m).Identifier.Text == overloadImplementationFunction.Identifier.Text + (overload.Count - overloadsWithUnknownParameterTypes.Count - 1));
            overrideVisit = overrideVisit.InsertNodesAfter(nodeToPlaceImplementationAfter, new[] { overloadImplementationFunction });
        }

        return overrideVisit;
    }

    public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        var returnType = node.Type;
        var parameterList = node.ParameterList;
        var methodName = returnType.GetText().ToString() == ((TypeDeclarationSyntax)node.Parent).Identifier.Text
            ? $"From{parameterList.Parameters.First().Type.GetText()}"
            : $"To{returnType.GetText()}";
        var overrideBody = node.Body != null ? VisitBlock(node.Body) as BlockSyntax : null;
        var overrideExpressionBody = node.ExpressionBody != null ? VisitArrowExpressionClause(node.ExpressionBody) : null;

        if (overrideExpressionBody is BlockSyntax block)
            overrideBody = block;

        var newMethod = SyntaxFactory.MethodDeclaration(
            new SyntaxList<AttributeListSyntax>(),
            SyntaxFactory.TokenList(CreateToken(SyntaxKind.PublicKeyword)),
            returnType,
            null,
            SyntaxFactory.Identifier($" {methodName} "),
            null,
            parameterList,
            new SyntaxList<TypeParameterConstraintClauseSyntax>(),
            overrideBody,
            overrideExpressionBody as ArrowExpressionClauseSyntax,
            CreateToken(SyntaxKind.SemicolonToken, ";")
        );

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