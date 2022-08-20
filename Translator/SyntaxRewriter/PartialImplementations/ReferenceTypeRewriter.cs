using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.IntegratedQueryRuntime;
using Translator.SyntaxRewriter.Core;

namespace Translator.SyntaxRewriter.PartialImplementations;

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

        overrideVisit = overrideVisit.WithModifiers(SyntaxFactory.TokenList(newAccessToken))
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
            ?
            (currentClassDefinition != null ? FixDeclarationWhitespace(currentClassDefinition) : null)
            : $@"
{currentClassDefinition}

export namespace {nodeName} {{
{extractedChildrenClasses}
}}
";
        return (result, classOrRecordDeclaration);
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
                type is PrimaryConstructorBaseTypeSyntax constructor ?
                    constructor.WithArgumentList(SyntaxFactory.ArgumentList().WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken)).WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken))) :
                    type));
            node = node.WithTypes(newTypes);
        }

        return node.WithColonToken(CreateToken(SyntaxKind.ColonToken, "extends "));
    }

    public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var overrideVisit = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
        return overrideVisit.WithIdentifier(SyntaxFactory.Identifier("constructor"));
    }

    public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        var returnType = node.Type;
        var parameterList = node.ParameterList;
        var methodName = returnType.GetText().ToString() == ((ClassDeclarationSyntax)node.Parent).Identifier.Text ? $"From{parameterList.Parameters.First().Type.GetText()}" : $"To{returnType.GetText()}";
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