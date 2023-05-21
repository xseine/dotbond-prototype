using System.Collections;
using System.Text.RegularExpressions;
using DotBond.Misc.Exceptions;
using DotBond.SyntaxRewriter.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using IPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using ITypeSymbol = Microsoft.CodeAnalysis.ITypeSymbol;
using SymbolKind = Microsoft.CodeAnalysis.SymbolKind;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace DotBond.SyntaxRewriter.PartialImplementations;

public partial class Rewriter : AbstractRewriterWithSemantics
{
    public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        var memberBinding = node.DescendantNodes().OfType<MemberBindingExpressionSyntax>().Last();
        var newMemberAccess = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, node.Expression,
            CreateToken(SyntaxKind.DotToken, "?."), memberBinding.Name);
        _savedSymbolsFromOriginalTree.Add(newMemberAccess.ToString(), GetSymbol(memberBinding));
        var nonConditionalAccess = node.WhenNotNull.ReplaceNode(memberBinding, newMemberAccess);

        var memberAccessAfterConditional = node.WhenNotNull.DescendantNodes().OfType<MemberAccessExpressionSyntax>().ToList();
        var idx = 0;
        foreach (var memberAccess in nonConditionalAccess.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Take(memberAccessAfterConditional.Count))
        {
            _savedSymbolsFromOriginalTree.Add(memberAccess.ToString(), GetSymbol(memberAccessAfterConditional[idx++]));
        }

        TryGetSavedSymbolsToUse(ref node);
        var overrideVisit = base.Visit(nonConditionalAccess);
        ClearSavedSymbols(ref overrideVisit);

        return overrideVisit;
    }

    // Predfined translations
    // + server-side method evaluation
    // + registering types for ensuing translation
    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var overrideVisit = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);

        var (methodName, symbol) = (node.Name.ToString(), GetSymbol(node));

        switch (symbol)
        {
            case null:
                return overrideVisit;
            case IPropertySymbol propertySymbol when GetSymbol(node.Expression)?.Kind == SymbolKind.Local && propertySymbol.DeclaringSyntaxReferences.Any():
                ImportedSymbols.Add(propertySymbol.ContainingSymbol as ITypeSymbol);
                break;
            case IPropertySymbol { IsStatic: true } propertySymbol:
                return TranslateStaticProperty(propertySymbol) ??
                       throw new MissingStaticClassException($"No translation provided for the {propertySymbol}. This part of the source must be removed.");
        }

        // New name for the method to, e.g. "toString"
        string newMethodName = null;

        switch (symbol.ContainingType.ContainingNamespace.ToString())
        {
            // List methods
            case "System.Collections.Generic":
            case "System.Linq":
            {
                switch (methodName)
                {
                    case "ForEach":
                    {
                        newMethodName = "forEach";
                        break;
                    }

                    case nameof(Enumerable.Select):
                    {
                        newMethodName = "map";
                        break;
                    }

                    case nameof(Enumerable.SelectMany):
                    {
                        newMethodName = "flatMap";
                        break;
                    }

                    case nameof(Enumerable.Where):
                    case nameof(List<object>.FindAll):
                    {
                        newMethodName = "filter";
                        break;
                    }

                    case nameof(Enumerable.Count):
                    {
                        newMethodName = "length";
                        if (node.Parent is InvocationExpressionSyntax)
                            RegisterAncestorRewrite(syntaxNode => ((InvocationExpressionSyntax)syntaxNode)
                                    .WithArgumentList(_emptyArgumentsList.WithTrailingTrivia(((InvocationExpressionSyntax)syntaxNode).GetTrailingTrivia())),
                                SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(Enumerable.Contains):
                    {
                        newMethodName = ((InvocationExpressionSyntax)node.Parent).ArgumentList.Arguments.Count == 1 ? "includes" : "includesWithComparer";
                        break;
                    }

                    case nameof(Enumerable.First):
                    case nameof(Enumerable.FirstOrDefault):
                    case nameof(List<object>.Find):
                    {
                        newMethodName = "find";

                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;

                            return invocation.ArgumentList.Arguments.Any()
                                ? syntaxNode
                                : invocation.WithArgumentList(CreateArgumentList(SyntaxFactory.ParseExpression("_ => true")));
                        }, SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(Enumerable.Last):
                    case nameof(Enumerable.LastOrDefault):
                    case nameof(List<object>.FindLast):
                    {
                        var hasArguments = ((InvocationExpressionSyntax)node.Parent).ArgumentList.Arguments.Any();

                        if (hasArguments)
                            newMethodName = "slice().reverse().find";
                        else
                        {
                            newMethodName = "at";
                            RegisterAncestorRewrite(syntaxNode => ((InvocationExpressionSyntax)syntaxNode)?.WithArgumentList(
                                    CreateArgumentList(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal("-1", -1)))),
                                SyntaxKind.InvocationExpression);
                        }

                        break;
                    }

                    case nameof(List<object>.FindIndex):
                    {
                        newMethodName = "findIndex";
                        break;
                    }

                    case nameof(List<object>.FindLastIndex):
                    {
                        newMethodName = "slice().reverse().findIndex";
                        break;
                    }

                    case nameof(Enumerable.Aggregate):
                    {
                        newMethodName = "reduce";

                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var reversedArguments = SyntaxFactory.SeparatedList(((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments.Reverse());

                            return ((InvocationExpressionSyntax)syntaxNode)?.WithArgumentList(SyntaxFactory.ArgumentList(reversedArguments));
                        }, SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(Enumerable.Sum):
                    {
                        newMethodName =
                            "reduce((acc, curr) => acc + curr)"; // Note: Parent InvocationExpression will add "map" transformation, and provide the callback function
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocationExpression = (InvocationExpressionSyntax)syntaxNode;
                            var memberAccessExpression = (MemberAccessExpressionSyntax)invocationExpression.Expression;

                            if (invocationExpression.ArgumentList.Arguments.Any())
                                memberAccessExpression = memberAccessExpression.WithExpression(
                                    SyntaxFactory.InvocationExpression(
                                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, memberAccessExpression.Expression,
                                            SyntaxFactory.IdentifierName("map")),
                                        invocationExpression.ArgumentList));

                            invocationExpression = invocationExpression.WithExpression(memberAccessExpression)
                                .WithArgumentList(SyntaxFactory.ArgumentList().WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken))
                                    .WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken)));

                            return invocationExpression;
                        }, SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(Enumerable.Take):
                    {
                        var argOfSkipBeforeTake = node switch
                        {
                            { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax x1 } x2 }
                                when x1.Name.ToString() == nameof(Enumerable.Skip) => x2.ArgumentList.Arguments.First().Expression.ToString(),
                            _ => null
                        };

                        var takeArg = node switch
                        {
                            { Parent: InvocationExpressionSyntax x1 } => x1.ArgumentList.Arguments.First().Expression.ToString()
                        };

                        newMethodName = argOfSkipBeforeTake != null ? $"slice({argOfSkipBeforeTake}, {argOfSkipBeforeTake + takeArg})" : $"slice(0, {takeArg})";

                        if (argOfSkipBeforeTake != null)
                        {
                            var emptyName = SyntaxFactory.IdentifierName("");
                            var emptyToken = SyntaxFactory.MissingToken(SyntaxKind.DotToken);

                            overrideVisit = overrideVisit switch
                            {
                                MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax x1 } x2 } x3
                                    => x3.WithExpression(x2.WithArgumentList(_emptyArgumentsList)
                                        .WithExpression(x1.WithName(emptyName).WithOperatorToken(emptyToken)))
                            };
                        }

                        var isStringSource = ((IMethodSymbol)symbol).TypeArguments.First() is { Name: "Char" };
                        if (isStringSource && argOfSkipBeforeTake == null)
                        {
                            var leadingArrayTrivia = SyntaxFactory.Whitespace("[...");
                            var trailingArrayTrivia = SyntaxFactory.Whitespace("]");

                            overrideVisit = overrideVisit switch
                            {
                                { Expression: { } x1 } => overrideVisit.WithExpression(x1.WithLeadingTrivia(x1.GetLeadingTrivia().Append(leadingArrayTrivia))
                                    .WithTrailingTrivia(x1.GetTrailingTrivia().Prepend(trailingArrayTrivia))),
                            };
                        }

                        RegisterAncestorRewrite(syntaxNode =>
                            ((InvocationExpressionSyntax)syntaxNode).WithArgumentList(_emptyArgumentsList), SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(Enumerable.Skip):
                    {
                        var skipArg = node switch
                        {
                            { Parent: InvocationExpressionSyntax x1 } => x1.ArgumentList.Arguments.First().Expression.ToString(),
                            _ => null
                        };

                        newMethodName = $"slice({skipArg})";

                        var isStringSource = ((IMethodSymbol)symbol).TypeArguments.First() is { Name: "Char" };
                        if (isStringSource)
                        {
                            var leadingArrayTrivia = SyntaxFactory.Whitespace("[...");
                            var trailingArrayTrivia = SyntaxFactory.Whitespace("]");

                            overrideVisit = overrideVisit switch
                            {
                                { Expression: { } x1 } x2
                                    => x2.WithExpression(x1.WithLeadingTrivia(x1.GetLeadingTrivia().Append(leadingArrayTrivia))
                                        .WithTrailingTrivia(x1.GetTrailingTrivia().Prepend(trailingArrayTrivia)))
                            };
                        }

                        var emptyArgumentsList = SyntaxFactory.ArgumentList(
                            SyntaxFactory.MissingToken(SyntaxKind.OpenParenToken),
                            default,
                            SyntaxFactory.MissingToken(SyntaxKind.CloseParenToken));

                        RegisterAncestorRewrite(syntaxNode =>
                            ((InvocationExpressionSyntax)syntaxNode).WithArgumentList(emptyArgumentsList), SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(Enumerable.OrderBy):
                    case nameof(Enumerable.OrderByDescending):
                    {
                        var orderKey = GetOrderKey(node);
                        orderKey = orderKey[0].ToString().ToLower() + orderKey[1..];
                        newMethodName = methodName == nameof(Enumerable.OrderBy)
                            ? $"sort((a, b) => a.{orderKey} - b.{orderKey})"
                            : $"sort((a, b) => b.{orderKey} - a.{orderKey})";
                        RegisterAncestorRewrite(syntaxNode =>
                            ((InvocationExpressionSyntax)syntaxNode).WithArgumentList(_emptyArgumentsList), SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(Enumerable.Max):
                    case nameof(Enumerable.Min):
                    {
                        var isMax = methodName == nameof(Enumerable.Max);

                        var invocation = (InvocationExpressionSyntax)node.Parent;

                        // If argument is present, use map to select just those values
                        if (invocation.ArgumentList.Arguments.Any())
                        {
                            newMethodName = "map";
                            RegisterAncestorRewrite(syntaxNode =>
                            {
                                var invocation = (InvocationExpressionSyntax)syntaxNode;

                                return SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("Math"),
                                        SyntaxFactory.IdentifierName(isMax ? "max" : "min")
                                    ),
                                    CreateArgumentList(invocation).WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "(..."))
                                );
                            }, SyntaxKind.InvocationExpression);
                        }
                        else
                        {
                            RegisterAncestorRewrite(_ => SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("Math"),
                                    SyntaxFactory.IdentifierName(isMax ? "max" : "min")
                                ),
                                CreateArgumentList(overrideVisit.Expression).WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken, "(..."))
                            ), SyntaxKind.InvocationExpression);

                            return overrideVisit.Expression;
                        }

                        break;
                    }
                    
                    case nameof(Enumerable.Any):
                    {
                        if (node.Parent is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 })
                            newMethodName = "some";
                        else
                            RegisterAncestorRewrite(syntaxNode =>
                            {
                                var invocation = (InvocationExpressionSyntax)syntaxNode;
                                return SyntaxFactory.BinaryExpression(SyntaxKind.GreaterThanExpression,
                                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ((MemberAccessExpressionSyntax) invocation.Expression).Expression, SyntaxFactory.IdentifierName("length")),
                                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1)));
                            }, SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(Enumerable.All):
                    {
                        newMethodName = "every";
                        break;
                    }
                    
                    case nameof(Enumerable.Range):
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;
                            var start = invocation.ArgumentList.Arguments[0].Expression.ToString();
                            var count = invocation.ArgumentList.Arguments[1].Expression.ToString();

                            return SyntaxFactory.ParseExpression($"[...Array({count} + {start}).keys()].slice({start})");
                        }, SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(Enumerable.Append):
                    {
                        newMethodName = "concat";
                        break;
                    }

                    case nameof(Enumerable.Prepend):
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                            {
                                var invocation = (InvocationExpressionSyntax)syntaxNode;
                                var argument = invocation.ArgumentList.Arguments.First().Expression;

                                var concat = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, argument
                                    .WithLeadingTrivia(argument.GetLeadingTrivia().Append("["))
                                    .WithTrailingTrivia(argument.GetTrailingTrivia().Prepend("]")), SyntaxFactory.IdentifierName("concat"));

                                return SyntaxFactory.InvocationExpression(concat,
                                    CreateArgumentList(((MemberAccessExpressionSyntax)invocation.Expression).Expression));
                            },
                            SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(IList.Clear):
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                                ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)syntaxNode).Expression).Expression
                                .WithTrailingTrivia(syntaxNode.GetTrailingTrivia().Prepend(".length = 0")),
                            SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(IDictionary.Add):
                    {
                        if (symbol.ContainingType.Name == "Dictionary")
                            RegisterAncestorRewrite(syntaxNode =>
                            {
                                var invExpression = (InvocationExpressionSyntax)syntaxNode;
                                return SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.ElementAccessExpression(
                                        (invExpression.Expression as MemberAccessExpressionSyntax).Expression,
                                        SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList(new[]
                                            { SyntaxFactory.Argument(invExpression.ArgumentList.Arguments[0].Expression) }))),
                                    invExpression.ArgumentList.Arguments[1].Expression);
                            }, SyntaxKind.InvocationExpression);
                        else if (symbol.ContainingType.Name == "List")
                            newMethodName = "push";

                        break;
                    }

                    case nameof(IDictionary.Remove):
                    {
                        if (symbol.ContainingType.Name == "Dictionary")
                            RegisterAncestorRewrite(syntaxNode =>
                            {
                                var invExpression = (InvocationExpressionSyntax)syntaxNode;
                                var dictionaryExp = (invExpression.Expression as MemberAccessExpressionSyntax).Expression;

                                return SyntaxFactory.ElementAccessExpression(
                                        dictionaryExp,
                                        SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(new[]
                                            { SyntaxFactory.Argument(invExpression.ArgumentList.Arguments[0].Expression) })))
                                    .WithLeadingTrivia(invExpression.GetLeadingTrivia().Append("delete "));
                            }, SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(IDictionary<object, object>.ContainsKey):
                    {
                        if (symbol.ContainingType.Name == "Dictionary")
                            RegisterAncestorRewrite(syntaxNode =>
                            {
                                var invocation = (InvocationExpressionSyntax)syntaxNode;
                                var dictionaryExp = (invocation.Expression as MemberAccessExpressionSyntax).Expression;

                                return invocation.ArgumentList.Arguments[0].Expression
                                    .WithTrailingTrivia(invocation.GetTrailingTrivia().Prepend($" in {dictionaryExp.ToString()}"));
                            }, SyntaxKind.InvocationExpression);

                        break;
                    }

                    case "GetValueOrDefault":
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;
                            var dictionaryExp = (invocation.Expression as MemberAccessExpressionSyntax).Expression;

                            ExpressionSyntax result = SyntaxFactory.ElementAccessExpression(
                                dictionaryExp,
                                SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList(new[]
                                    { SyntaxFactory.Argument(invocation.ArgumentList.Arguments[0].Expression) })));

                            if (invocation.ArgumentList.Arguments.Count > 1)
                                result = SyntaxFactory.ParenthesizedExpression(SyntaxFactory.BinaryExpression(SyntaxKind.CoalesceExpression, result,
                                    invocation.ArgumentList.Arguments[1].Expression));

                            return result;

                            return invocation.ArgumentList.Arguments[0].Expression
                                .WithTrailingTrivia(invocation.GetTrailingTrivia().Prepend($" in {dictionaryExp.ToString()}"));
                        }, SyntaxKind.InvocationExpression);

                        break;
                    }
                }

                goto default;
            }

            case "System":
            case "System.Text":
            {
                switch (methodName)
                {
                    case "Write":
                    case "WriteLine":
                    {
                        if (((MemberAccessExpressionSyntax)node).Expression.ToString() == "Console")
                        {
                            newMethodName = "log";
                            overrideVisit = ((MemberAccessExpressionSyntax)overrideVisit).WithExpression(SyntaxFactory.IdentifierName("console"))
                                .WithLeadingTrivia(node.GetLeadingTrivia());
                        }

                        break;
                    }

                    case nameof(Console.Read):
                    case nameof(Console.ReadLine):
                    {
                        if (((MemberAccessExpressionSyntax)node).Expression.ToString() == "Console")
                        {
                            newMethodName = "prompt";
                            overrideVisit = ((MemberAccessExpressionSyntax)overrideVisit).WithExpression(SyntaxFactory.IdentifierName("window"))
                                .WithLeadingTrivia(node.GetLeadingTrivia());
                        }

                        break;
                    }

                    case "ToString":
                    {
                        if (symbol.ContainingType.Name == "StringBuilder")
                        {
                            RegisterAncestorRewrite(
                                syntaxNode => ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)syntaxNode).Expression).Expression,
                                SyntaxKind.InvocationExpression);
                            break;
                        }

                        newMethodName = symbol.ContainingType.Name switch
                        {
                            nameof(DateTime) => "format",
                            _ => "toString"
                        };
                        break;
                    }

                    case "Append":
                    {
                        if (symbol.ContainingType.Name == "StringBuilder")
                            RegisterAncestorRewrite(syntaxNode =>
                                    SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression,
                                        ((MemberAccessExpressionSyntax)overrideVisit).Expression,
                                        ((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments.First().Expression),
                                SyntaxKind.InvocationExpression);

                        break;
                    }

                    case "Join":
                    {
                        var isStringJoin = symbol.ContainingType.Name == "String";
                        if (isStringJoin)
                            RegisterAncestorRewrite(
                                syntaxNode =>
                                {
                                    var arguments = ((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments;
                                    var array = arguments.Count > 2
                                        ? $"[{string.Join(", ", arguments.Skip(1).Select(arg => arg.Expression.ToFullString()))}]"
                                        : arguments[1].Expression.ToFullString();
                                    return CreateNode($"{array}.join({arguments[0].Expression})");
                                },
                                SyntaxKind.InvocationExpression);
                        break;
                    }

                    case "Parse":
                    {
                        var targetType = symbol.ContainingType.Name;

                        if (new[]
                            {
                                nameof(Byte), nameof(Int16), nameof(Int32), nameof(Int64), nameof(Byte), nameof(UInt16), nameof(UInt32), nameof(UInt64),
                                nameof(Double), nameof(Decimal)
                            }
                            .Contains(targetType))
                        {
                            RegisterAncestorRewrite(
                                syntaxNode =>
                                {
                                    var arguments = ((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments;
                                    return arguments[0].Expression.WithLeadingTrivia(SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, " (+"))
                                        .WithTrailingTrivia(SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, ")"));
                                },
                                SyntaxKind.InvocationExpression);
                        }
                        else if (nameof(Boolean) == targetType)
                        {
                            RegisterAncestorRewrite(syntaxNode =>
                                {
                                    var value = (((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax)!
                                        .Token.Text;
                                    return SyntaxFactory.LiteralExpression(value == "false"
                                        ? SyntaxKind.FalseLiteralExpression
                                        : SyntaxKind.TrueLiteralExpression);
                                },
                                SyntaxKind.InvocationExpression);
                        }
                        else if (nameof(DateTime) == targetType)
                        {
                            newMethodName = "";
                            overrideVisit = ((MemberAccessExpressionSyntax)overrideVisit).WithExpression(SyntaxFactory.IdentifierName("new Date"))
                                .WithOperatorToken(SyntaxFactory.Token(default, SyntaxKind.DotToken, "", "", default))
                                .WithLeadingTrivia(node.GetLeadingTrivia());
                        }

                        break;
                    }

                    case "IsNullOrEmpty":
                    {
                        if (symbol.ContainingType.Name == "String")
                        {
                            RegisterAncestorRewrite(syntaxNode =>
                                {
                                    var argument = (syntaxNode as InvocationExpressionSyntax).ArgumentList.Arguments.First().Expression.ToString();
                                    return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        CreateToken(SyntaxKind.StringLiteralToken, $"(!({argument}))"));
                                },
                                SyntaxKind.InvocationExpression);
                        }

                        break;
                    }

                    case "IsNullOrWhiteSpace":
                    {
                        if (symbol.ContainingType.Name == "String")
                        {
                            RegisterAncestorRewrite(syntaxNode =>
                                {
                                    var argument = (syntaxNode as InvocationExpressionSyntax).ArgumentList.Arguments.First().Expression.ToString();
                                    return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                                        CreateToken(SyntaxKind.StringLiteralToken, $"(!({argument}).trim())"));
                                },
                                SyntaxKind.InvocationExpression);
                        }

                        break;
                    }

                    case "Empty":
                    {
                        if (symbol.ContainingSymbol.Name == "String")
                            return SyntaxFactory.ParseExpression("''");

                        break;
                    }

                    case { } when methodName.StartsWith("Empty<"):
                    {
                        if (symbol.ContainingSymbol.Name == "Array")
                            RegisterAncestorRewrite(syntaxNode => { return SyntaxFactory.ParseExpression("[]"); },
                                SyntaxKind.InvocationExpression);

                        break;
                    }

                    case "GetType":
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                            {
                                var a = (overrideVisit as MemberAccessExpressionSyntax).WithName(SyntaxFactory.IdentifierName("constructor.name"));
                                return a;
                            },
                            SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(string.ToLower):
                    case nameof(string.ToLowerInvariant):
                    {
                        newMethodName = "toLowerCase";
                        break;
                    }

                    case nameof(string.ToUpper):
                    case nameof(string.ToUpperInvariant):
                    {
                        newMethodName = "toLowerCase";
                        break;
                    }

                    case nameof(Random.Next):
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;
                            var (min, max) = invocation is { ArgumentList.Arguments: [{Expression: var x}, {Expression: var y}] } ? (x.ToString(), y.ToString()) : ("0", "2147483647");
                                return SyntaxFactory.ParseExpression($"Math.floor(Math.random() * ({max} - {min} + 1)) + {max}");
                        },
                        SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(DateTime.Year):
                    {
                        newMethodName = "getFullYear()";
                        break;
                    }
                    case nameof(DateTime.Month):
                    {
                        newMethodName = "getMonth()";
                        break;
                    }
                    case nameof(DateTime.Day):
                    {
                        newMethodName = "getDay()";
                        break;
                    }
                    case nameof(DateTime.Hour):
                    {
                        newMethodName = "getHours()";
                        break;
                    }
                    case nameof(DateTime.Minute):
                    {
                        newMethodName = "getMinutes()";
                        break;
                    }
                    case nameof(DateTime.Second):
                    {
                        newMethodName = "getSeconds()";
                        break;
                    }
                    case nameof(DateTime.Millisecond):
                    {
                        newMethodName = "getMilliseconds()";
                        break;
                    }
                    case nameof(DateTime.AddYears):
                    {
                        newMethodName = "addYears";
                        break;
                    }
                    case nameof(DateTime.AddMonths):
                    {
                        newMethodName = "addMonths";
                        break;
                    }
                    case nameof(DateTime.AddDays):
                    {
                        newMethodName = "addDays";
                        break;
                    }
                    case nameof(DateTime.AddHours):
                    {
                        newMethodName = "addHours";
                        break;
                    }
                    case nameof(DateTime.AddMinutes):
                    {
                        newMethodName = "addMinutes";
                        break;
                    }
                    case nameof(DateTime.AddSeconds):
                    {
                        newMethodName = "addSeconds";
                        break;
                    }
                    case nameof(DateTime.AddMilliseconds):
                    {
                        newMethodName = "addMilliseconds";
                        break;
                    }

                    case nameof(Nullable<int>.HasValue):
                    {
                        return SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression, ((MemberAccessExpressionSyntax)overrideVisit).Expression,
                            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
                    }

                    case nameof(Nullable<int>.Value):
                    {
                        return ((MemberAccessExpressionSyntax)overrideVisit).Expression;
                    }
                }

                goto default;
            }
            case "System.Text.RegularExpressions":
            {
                switch (methodName)
                {
                    case "IsMatch":
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;

                            return SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(" RegExp"),
                                        CreateArgumentList(invocation.ArgumentList.Arguments[1].Expression), null),
                                    SyntaxFactory.IdentifierName("test")),
                                CreateArgumentList(invocation.ArgumentList.Arguments[0].Expression));
                        }, SyntaxKind.InvocationExpression);
                        break;
                    }

                    case "Split":
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;

                            return SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    invocation.ArgumentList.Arguments[0].Expression,
                                    SyntaxFactory.IdentifierName("split")),
                                CreateArgumentList(SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(" RegExp"),
                                    CreateArgumentList(invocation.ArgumentList.Arguments[1].Expression),
                                    null)));
                        }, SyntaxKind.InvocationExpression);
                        break;
                    }

                    case "Replace":
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;

                            var flags = new List<char>() { 'g' };
                            if (invocation.ArgumentList.Arguments.Count > 3 && invocation.ArgumentList.Arguments[2] is
                                    { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "IgnoreCase" } })
                                flags.Add('i');

                            var regExpArgs = CreateArgumentList(invocation.ArgumentList.Arguments[1].Expression,
                                SyntaxFactory.ParseExpression($" '{string.Join("", flags)}'"));

                            return SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    invocation.ArgumentList.Arguments[0].Expression,
                                    SyntaxFactory.IdentifierName("replaceAll")),
                                CreateArgumentList(SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(" RegExp"), regExpArgs,
                                    null), invocation.ArgumentList.Arguments[2].Expression));
                        }, SyntaxKind.InvocationExpression);
                        break;
                    }

                    case "Match":
                    case "Matches":
                    {
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocation = (InvocationExpressionSyntax)syntaxNode;

                            var flags = new List<char>();
                            if (methodName == "Matches")
                                flags.Add('g');
                            if (invocation.ArgumentList.Arguments.Count > 2 && invocation.ArgumentList.Arguments[2] is
                                    { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "IgnoreCase" } })
                                flags.Add('i');

                            var regExpArgs = flags.Any()
                                ? CreateArgumentList(invocation.ArgumentList.Arguments[1].Expression,
                                    SyntaxFactory.ParseExpression($" '{string.Join("", flags)}'"))
                                : CreateArgumentList(invocation.ArgumentList.Arguments[1].Expression);

                            return SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    invocation.ArgumentList.Arguments[0].Expression,
                                    SyntaxFactory.IdentifierName("match")),
                                CreateArgumentList(SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(" RegExp"), regExpArgs,
                                    null)));
                        }, SyntaxKind.InvocationExpression);
                        break;
                    }

                    case "Value":
                    {
                        var containinExpressionSymbols = GetTypeSymbol(node.Expression);

                        if (containinExpressionSymbols?.Name == "Match")
                            return SyntaxFactory.ElementAccessExpression(overrideVisit.Expression,
                                SyntaxFactory.BracketedArgumentList(CreateToken(SyntaxKind.OpenBracketToken, "?.["),
                                    SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Argument(SyntaxFactory.ParseExpression("0")) }),
                                    CreateToken(SyntaxKind.CloseBracketToken, "]")));

                        if (containinExpressionSymbols?.Name == "Group")
                            return overrideVisit.Expression;

                        break;
                    }

                    case "Groups":
                    {
                        if (symbol.ContainingSymbol.Name == "Match" && overrideVisit is MemberAccessExpressionSyntax member1)
                            return SyntaxFactory.ConditionalAccessExpression(member1.Expression, SyntaxFactory.MemberBindingExpression(member1.Name));

                        break;
                    }

                    case "Count":
                    {
                        newMethodName = "length";
                        if (node.Parent is InvocationExpressionSyntax)
                            RegisterAncestorRewrite(syntaxNode => ((InvocationExpressionSyntax)syntaxNode)
                                    .WithArgumentList(_emptyArgumentsList.WithTrailingTrivia(((InvocationExpressionSyntax)syntaxNode).GetTrailingTrivia())),
                                SyntaxKind.InvocationExpression);
                        break;
                    }
                }

                goto default;
            }
            default:
            {
                if (newMethodName != null) break;

                switch (methodName)
                {
                    case nameof(Enumerable.ToList):
                    case nameof(Enumerable.ToArray):
                        RegisterAncestorRewrite(syntaxNode => ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)syntaxNode).Expression).Expression,
                            SyntaxKind.InvocationExpression);
                        break;
                    case nameof(Enumerable.Aggregate):
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var reversedArguments = SyntaxFactory.SeparatedList(((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments.Reverse());

                            return ((InvocationExpressionSyntax)syntaxNode)?.WithArgumentList(SyntaxFactory.ArgumentList(reversedArguments));
                        }, SyntaxKind.InvocationExpression);
                        break;
                    default:
                        if (GetSymbol(node.Expression)?.Kind == SymbolKind.Local) break;

                        if ((symbol.ContainingSymbol as ITypeSymbol)!.IsValueType == false && symbol.ContainingSymbol.Name != "String" &&
                            symbol.ContainingSymbol.DeclaringSyntaxReferences.Any())
                            ImportedSymbols.Add(symbol.ContainingSymbol as ITypeSymbol);

                        if ((symbol.ContainingSymbol as ITypeSymbol).IsStatic && symbol is not IMethodSymbol { IsExtensionMethod: true })
                            overrideVisit = ((MemberAccessExpressionSyntax)overrideVisit).WithExpression(
                                GetFullTypeSyntax(((MemberAccessExpressionSyntax)node).Expression as TypeSyntax));

                        if ((symbol.ContainingSymbol as ITypeSymbol).TypeKind == TypeKind.Enum)
                            overrideVisit = node;

                        break;
                }

                break;
            }
        }

        if (newMethodName != null)
            overrideVisit = overrideVisit.WithName(SyntaxFactory.IdentifierName(newMethodName));

        if (symbol.ContainingType.IsTupleType)
        {
            var position = symbol.ContainingType.TupleElements.ToList().FindIndex(e => e.Name.ToLower() == methodName.ToLower());
            return SyntaxFactory.ElementAccessExpression(node.Expression,
                SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList(new[]
                    { SyntaxFactory.Argument(SyntaxFactory.ParseExpression(position.ToString())) })));
        }

        return overrideVisit;
    }

    private static string GetOrderKey(SyntaxNode node)
    {
        var orderKey =
            ((IdentifierNameSyntax)(((MemberAccessExpressionSyntax)((LambdaExpressionSyntax)(((InvocationExpressionSyntax)node.Parent)!).ArgumentList.Arguments
                    .First().Expression)
                .ExpressionBody)!).Name).Identifier.Text;
        return orderKey;
    }

    private SyntaxNode TranslateStaticProperty(IPropertySymbol propertySymbol)
    {
        if (propertySymbol.ContainingType.Name == nameof(DateTime))
        {
            if (propertySymbol.Name == "Now")
                return SyntaxFactory.ParseExpression("new Date()");
        }
        else if (propertySymbol.ContainingType.Name == nameof(Match))
        {
            if (propertySymbol.Name == nameof(Match.Empty))
                return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (propertySymbol.ContainingType.Name == "Environment")
        {
            if (propertySymbol.Name == "NewLine")
                return SyntaxFactory.ParseExpression("'\\n'");
        } else if (propertySymbol.ContainingType.Name == "StringComparer")
        {
            if (propertySymbol.Name == "OrdinalIgnoreCase")
                return SyntaxFactory.ParseExpression("'ordinalCaseInvariant'");
        } 

        return null;
    }

    private ArgumentListSyntax _emptyArgumentsList = SyntaxFactory.ArgumentList(
        SyntaxFactory.MissingToken(SyntaxKind.OpenParenToken),
        default,
        SyntaxFactory.MissingToken(SyntaxKind.CloseParenToken));
}