using System.Text.RegularExpressions;

namespace DotBond.IntegratedQueryRuntime;

public static class TsRegexRepository
{
    private static readonly string GenericNamedType = @$"\w+\s*{MatchBrackets(BracketType.AngleBrackets)}";
    private static readonly string NonGenericNamedType = @"\w+";
    private static readonly string ObjectValueType = MatchBrackets(BracketType.CurlyBrackets);

    public static Regex PublicMethodDeclarationHeader =
        new(@$"public\s+\w+{MatchBrackets(BracketType.Parenthasis)}\s*:?\s*({GenericNamedType}|{NonGenericNamedType}|{ObjectValueType})?(?=\s*{{)");


    /// <summary>
    /// Used for finding definitions of partial action methods in a pre-generated controller's file.
    /// </summary>
    public static Regex GeneratedActionHeader =
        new(@$"public partial ({GenericNamedType}|{NonGenericNamedType}|{ObjectValueType}) \w+{MatchBrackets(BracketType.Parenthasis)}(?=\s*{{)");

    public static string MatchBrackets(BracketType bracketType)
    {
        var (openingToken, closingToken) = bracketType switch
        {
            BracketType.Parenthasis => ("(", ")"),
            BracketType.SquareBrackets => ("[", "]"),
            BracketType.AngleBrackets => ("<", ">"),
            BracketType.CurlyBrackets => ("{", "}")
        };

        return MatchBalancedTokens(openingToken, closingToken);
    }

    /// <summary>
    /// Creates Regex for balanced group.
    /// </summary>
    /// <param name="openingToken"></param>
    /// <param name="closingToken">If null, openingToken is used for balancing.</param>
    /// <returns></returns>
    public static string MatchBalancedTokens(string openingToken, string closingToken = null)
    {
        closingToken ??= openingToken;

        // https://stackoverflow.com/a/35271017/15500203
        return
            @$"(\{openingToken}(?>\{openingToken}(?<c>)|[^\{openingToken}\{closingToken}]+|\{closingToken}(?<-c>))*(?(c)(?!))\{closingToken})";
    }

    private static readonly Regex BlockCommentRx = new(@"(\/\*(?>\/\*(?<c>)|[^/\*\*/]+|\\*/(?<-c>))*(?(c)(?!))\\*/)");
    private static readonly Regex LineCommentRx = new(@"//.*");
    public static string TrimComments(string source) => BlockCommentRx.Replace(LineCommentRx.Replace(source, ""), "");

    public static Regex CastAndConversionRx = new(@$"{MatchBrackets(BracketType.AngleBrackets)}|( as\s+(\w+?|({MatchBrackets(BracketType.CurlyBrackets)}[?]?\s*&\|\s*)+?))(?=,|\))");
    public static Regex FieldAssignmentRx = new(@"(?<leading>(\{|,)\s*)(?<name>\w+)\s*:");
    public static Regex ConditionalAccessRx = new(@"\?\s*(\.|\[)");
    public static string KeySelectorRx = @"\s*(""(?:\w|\.)+""|\[\s*""(?:\w|\.)+""\s*,\s*""(?:\w|\.)+""\s*\])\s*";
    public static Regex EmptyLinesRx = new(@"^\s*$\n|\r", RegexOptions.Multiline);

    // Array syntax must be locked before using this Regex for replacement
    public static Regex NominalFieldAssignmentRx = new(@"(?<!new\[\] \{)(?<=(\{|,)\s*)(?<name>\w+)(?=\s*(,|\}))");

    public static Regex QueryActionsRx =
        new(
            $@"((?<attributes>({MatchBrackets(BracketType.SquareBrackets)}\s*)*)|\s*)public[^\(]+? (?<actionName>\w+){MatchBrackets(BracketType.Parenthasis)}(?<implementation> => base\..+?;|\s*{MatchBrackets(BracketType.CurlyBrackets)})");
}

public enum BracketType
{
    /// <summary>
    /// ()
    /// </summary>
    Parenthasis,

    /// <summary>
    /// []
    /// </summary>
    SquareBrackets,

    /// <summary>
    /// &lt;&gt;
    /// </summary>
    AngleBrackets,

    /// <summary>
    /// {}
    /// </summary>
    CurlyBrackets
}