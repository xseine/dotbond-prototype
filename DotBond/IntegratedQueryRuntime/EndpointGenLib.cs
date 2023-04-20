using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.RegularExpressions;
using DotBond.Misc;
using DotBond.Misc.Exceptions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotBond.IntegratedQueryRuntime.TsRegexRepository;
using static DotBond.IntegratedQueryRuntime.EndpointGenUtilities;

namespace DotBond.IntegratedQueryRuntime;

public static class EndpointGenLib
{
    private static readonly Regex EndpointDefinitionRx = new(@$"customQuery\s+[^\(]+{MatchBrackets(BracketType.Parenthasis)}\s+{MatchBrackets(BracketType.CurlyBrackets)}");
    public static readonly string SpreadFunctionName = "Spread";
    
    /*========================== Public API ==========================*/

    /// <summary>
    /// Returns a .NET Controller class using the endpoint definitions.
    /// </summary>
    public static (string, string) GenerateNewEndpoints(string tsDefinitionsFile, ref Compilation compilation)
    {
        var retryCnt = 0;
        // Note: async can't have ref
        var tsDefinitionsFileContent = Observable.FromAsync(() => File.ReadAllTextAsync(tsDefinitionsFile)).Delay(_ => Observable.Timer(TimeSpan.FromMilliseconds(retryCnt++ * 100))).Retry(10).ToTask().Result;
        tsDefinitionsFileContent = TrimComments(tsDefinitionsFileContent);
        var tsDefinitions = EndpointDefinitionRx.Matches(tsDefinitionsFileContent);

        var translations = new List<TranslatedEndpoint>();
        foreach (Match tsDefinition in tsDefinitions)
            try
            {
                translations.Add(TranslateEndpoint(tsDefinition.Value, ref compilation));
            }
            catch (MissingDefinitionException) { }

        // Ignore those translations without body
        translations = translations.Where(t => t.Body.Length > 1).ToList();
        
        return GenerateFiles(translations);
    }

    /*========================== Private API ==========================*/

    /// <summary>
    /// Translates a single custom endpoint.
    /// </summary>
    private static TranslatedEndpoint TranslateEndpoint(string source, ref Compilation compilation)
    {
        source = new Regex(@"^\s*customQuery\s*").Replace(source, ""); // Remove the extra
        var actionName = new Regex(@"^\s*public\s+(?<name>\w+)").Match(source).Groups["name"].Value; // Get action name

        // Translate parameters and add parameter reference type definitions
        var paramsLineRx = PublicMethodDeclarationHeader; // new Regex(@"\w+\([^\)]*?\)\s*\{");
        var (parameters, generatedRecords, deconstructStatement) = GetParameters(paramsLineRx.Match(source).Value, actionName);
        source = paramsLineRx.Replace(source, ""); // Removing name and params

        source = new Regex(@"^\s*{(\s*)").Replace(source, "$1");
        source = new Regex(@";?\s*}\s*$").Replace(source, ";");

        
        (var ctrParams, source, var controllerNamespaces, var isAsync) = ExtractControllers(source, ref compilation); // Extract controllers

        source = ReplaceSingleWithDoubleQuotes(source); // Replace single quotes
        source = ReplaceBracketWithDotNotation(source);
        source = ReplaceGroupJoinWithMagicalSelect(source);
        (source, var unlock) = LockStrings(source);
        source = RemoveCasting(source); // Remove casting and type conversion (as)
        source = UpperCaseNominalAssignment(source); // actor => Actor = actor,
        source = ReplaceArraySyntax(source);
        source = ReplaceSpreadSyntax(source);
        // source = SpreadSyntaxHandler.InjectCustomMethodsAndExpressions(source);
        source = ReplaceMethodNames(source);            // Replace method names
        source = RemoveInnerQueryableExcess(source);
        source = RemoveParameterType(source);
        source = RestorePascalCaseForMembers(source);   // .name => .Name
        source = ReplaceObjectCreation(source);         // Replace object creation
        source = ReplaceAssignments(source);            // Replace assignments
        source = ReplaceSlice(source);                  // Replace slice
        source = NullCoalesceInnerQueryable(source);    // In Join, inner sequence can't be null
        source = AddNullForgiving(source, compilation);
        source = RemoveRemainingThisKeywords(source);
        source = source.Replace("===", "=="); // Replace triple equals

        source = unlock(source);
        source = ReplaceKeySelectors(source); // 'key', e => e.key

        source = deconstructStatement + "\n" + source;

        source = Regex.Replace(source, @"^\s+", "");
        return new TranslatedEndpoint(actionName, parameters, source, ctrParams, generatedRecords, controllerNamespaces, isAsync);
    }

    /// <summary>
    /// Gets parameters' names and types and provides type definitions of C# records if TS endpoints use object value parameters. 
    /// </summary>
    private static (List<(string Name, string Type)> parameters, string recordDefinition, string deconstructStatement) GetParameters(string sourceParamsPart, string actionName)
    {
        var paramRx = new Regex(@$"(?<name>\w+)\s*:\s*(?<type>{MatchBrackets(BracketType.CurlyBrackets)}(\[\])?|(?:\s*\w+\[?\]?))\s*(?:,|\))");
        var parameters = paramRx.Matches(sourceParamsPart)
            .Select(e => (Name: e.Groups["name"].Value, Type: e.Groups["type"].Value.Trim())).ToList(); // Get params

        var objectTypeParameters = parameters.Where(p => p.Type.StartsWith("{") || p.Type.EndsWith("[]")).ToList();
        string actionParamsDeconstruct = null;
        var fieldRx = new Regex(@$"(?<name>\w+)\s*:\s*(?<type>{MatchBrackets(BracketType.CurlyBrackets)}(\[\])?|(?:\s*\w+\[?\]?))\s*(?:,|\}})");

        List<(string RecordName, string RecordDefinition)> TranslateObjectValueTypes(IEnumerable<string> objectValueType, string namePrefix = "")
        {
            var translations = new List<(string RecordName, string RecordDefinition)>();
            var idx = 0;

            foreach (var type in objectValueType)
            {
                var fields = fieldRx.Matches(type)
                    .Select(e => (Name: e.Groups["name"].Value, Type: e.Groups["type"].Value.Trim())).ToList();

                var namedTypesFields = fields.Where(f => !f.Type.StartsWith("{"));
                var objectValueFields = fields.Where(f => f.Type.StartsWith("{")).ToList();

                var name = $"{namePrefix}Record{idx++}";
                var objectValueTypeTranslations = TranslateObjectValueTypes(objectValueFields.Select(e => e.Type), name);

                var namedFields = namedTypesFields.Select(f => $"{TranslateNamedTypeOrArray(f.Type)} {f.Name[..1].ToUpper() + f.Name[1..]}").ToList();
                var recordFields = objectValueFields.Select((f, idx) => $"{objectValueTypeTranslations[idx].RecordName}{(f.Type.EndsWith("[]") ? "[]" : null)} {f.Name[..1].ToUpper() + f.Name[1..]}").ToList();

                var record = CreateRecord(name, namedFields, recordFields, objectValueTypeTranslations.Select(e => e.RecordDefinition));
                if (idx == 1)
                    actionParamsDeconstruct = $"var ({string.Join(", ", namedTypesFields.Concat(objectValueFields).Select(e => e.Name))}{(namedTypesFields.Count() == 1 ? ", _" : null)}) = body;";
                
                translations.Add((name, record));
            }

            return translations;
        }

        var namedTypeParameters = parameters.Where(p => !p.Type.StartsWith("{") && !p.Type.EndsWith("[]")).Select(p => (p.Name, Type: TranslateNamedTypeOrArray(p.Type))).ToList();
        // var translations = TranslateObjectValueTypes(objectTypeParameters.Select(e => e.Type), actionName);
        
        var recordParameter = objectTypeParameters.Any() ? TranslateObjectValueTypes(new[] { @$"{{{string.Join(", ", objectTypeParameters.Select(e => $"{e.Name} : {e.Type}"))}}}" }, actionName)[0] : default;

        if (recordParameter.RecordName != default)
            namedTypeParameters.Add(("body", recordParameter.RecordName));
        
        return (
            namedTypeParameters,
            recordParameter.RecordDefinition,
            actionParamsDeconstruct);
    }


    /// <summary>
    /// Modifies the action body to declare controller variables before the query.
    /// </summary>
    private static (List<(string Name, string Type)> controllerInjections, string source, List<string> usingNamespaces, bool isAsync)
            ExtractControllers(string source, ref Compilation compilation)
    {
        // Controllers used in the composed query
        var controllerNames = Regex.Matches(source, @"(?<=this\.ctx\.)\w+").Select(m => m.Value + "Controller").Distinct().ToList();
        
        // Trees used to retrieve result types, injected services and their namespaces.
        // var syntaxTreesToInspect = compilation.SyntaxTrees.Where(e => controllerNames.Any(controllerName => e.GetText().ToString().Contains(controllerName))).ToList();
        var syntaxTreesToInspect = compilation.SyntaxTrees.Where(e => e.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Any(@class => controllerNames.Contains(@class.Identifier.Text))).ToList();

        var returnedValsAndCtrlInstancesVars = new List<string>();
        var areNullableRefEnabled = compilation.Options.NullableContextOptions == NullableContextOptions.Enable;
        var returnVarIdentifierCnt = 1;

        var isAsync = false;
        
        // Enclose action invocation into array ceation syntax.
        // This is needed because frontend uses collection type methods (map, filter...)
        // + Adds number casting
        // + Removal of conditional access in IQueryables
        var actionInvocationRx = new Regex($@"this\.ctx\.(?<controller>\w+)\s*\.\s*(?<action>\w+)\s*(?<parameters>{MatchBrackets(BracketType.Parenthasis)})");
        foreach (var (match, controller, action, parameters) in actionInvocationRx.Matches(source).Select(e => (e.Value, e.Groups["controller"].Value, e.Groups["action"].Value, e.Groups["parameters"].Value)).DistinctBy(e => e.Item1))
        {
            var (actionReturnTypeSymbol, isCollection, isTask, isActionResult) = GetActionReturnType(action, controller, syntaxTreesToInspect, ref compilation);

            if (isCollection == false)
            {
                var newVariableName = returnVarIdentifierCnt == 1 ? "returnVar" : $"returnVar{returnVarIdentifierCnt}";
                var newvariableType = isTask ? ((INamedTypeSymbol)actionReturnTypeSymbol).TypeArguments.First() : actionReturnTypeSymbol;
                newvariableType = isActionResult ? ((INamedTypeSymbol)newvariableType).TypeArguments.First() : newvariableType;
                returnedValsAndCtrlInstancesVars.Add($"{newvariableType}{(areNullableRefEnabled ? "?" : null)} {newVariableName};");

                var replacement = $"(({newVariableName} = {(isTask ? "await " : null)}{match}{(isActionResult ? ".Value" : null)}) != null ? new[] {{{newVariableName}}}.ToList() : null)";
                source = source.Replace(match, replacement);

                returnVarIdentifierCnt++;
            } 
            else if (isActionResult || isTask)
            {
                if (isActionResult)
                    source = source.Replace(match, match + ".Value");
                if (isTask)
                    source = source.Replace(match,  $"(await {match})");
            }

            // Removing conditional access in IQueryables
            if (isCollection && actionReturnTypeSymbol.Name == "IQueryable")
            {
                var queryableMethodChain = Regex.Match(source, @$"{action}\w*{parameters}(\s*\.*\w*{MatchBrackets(BracketType.Parenthasis)})+\s*\??\s*\.?").Value;
                source = source.Replace(queryableMethodChain, ConditionalAccessRx.Replace(queryableMethodChain, "$1"));
                source = Regex.Replace(source, @"\.\s*find\(", ".findWithoutDefault(");
            }

            if (isTask)
                isAsync = true;
            
            if (parameters == "()") continue;
            
            // Number casting
            var parameterTypes = GetActionParameterTypes(action, controller, syntaxTreesToInspect, compilation).ToList();
            if (parameterTypes.All(e => !NumericTypes.Contains(e))) continue;

            string numericParamType = null;
            var newParameters = parameters[1..^1].Split(",").Select((param, idx) =>
                (numericParamType = NumericTypes.FirstOrDefault(e => e == parameterTypes[idx])) != null ? $"({numericParamType}) {param.Trim()}" : param.Trim()).ToList();

            source = source.Replace(match, match[..^parameters.Length] + "(" + string.Join(", ", newParameters) + ")");
        }

        source = new Regex(@"\)\.Value(\s*)\.").Replace(source, ").Value$1?.", 1);
        source = new Regex(@": null\)(\s*)\.").Replace(source, ": null)$1?.");


        // Get injectedServices
        var (controllerInjections, distinctServiceInjections) = GetInjectedServices(syntaxTreesToInspect, controllerNames);
        
        // Initialize controllers as variables
        // and replace controller invocations
        foreach (var controllerName in controllerNames)
        {
            var injectedServices = controllerInjections.First(e => e.ControllerName == controllerName).Injected.Select(e => e.Name);
            var controllerNameNew = controllerName[..^"Controller".Length];
            var lowercaseVarName = controllerNameNew[0].ToString().ToLower() + controllerNameNew[1..];
            lowercaseVarName = ReplaceIfKeyword(lowercaseVarName);

            var leadingTrivia = new Regex(@"^\s*").Match(source).Value;
            returnedValsAndCtrlInstancesVars.Add($"{leadingTrivia}var {lowercaseVarName} = new {controllerNameNew}Controller({string.Join(", ", injectedServices)});\n");
            source = source.Replace("this.ctx." + controllerNameNew, $"{lowercaseVarName}");
        }

        // Get controller namespaces
        var compilationCopy = compilation;
        var usingNamespaces = syntaxTreesToInspect.SelectMany(tree =>
        {
            var semanticModel = compilationCopy.GetSemanticModel(tree);
            var constructorParameterTypes = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(e => controllerNames.Contains(e.Identifier.Text))
                .SelectMany(classSyntax => (RoslynUtilities.GetConstructorParametersNamespaces(classSyntax, semanticModel) ?? new List<string>()).Append(semanticModel.GetDeclaredSymbol(classSyntax)!.ContainingNamespace.ToString()));

            var nestedEnums = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().SelectMany(e => e.DescendantNodes().OfType<EnumDeclarationSyntax>())
                .Select(e => semanticModel.GetDeclaredSymbol(e)).Select(e => $"{e.Name} = {e}");
            
            return constructorParameterTypes.Concat(nestedEnums);
        });
        

        source = string.Join("\n\t\t", returnedValsAndCtrlInstancesVars) + source;

        // Apply conditional access after find()/FirstOrDefault()
        source = new Regex(@"find\(\)\.(\w+)").Replace(source, "find()?.$1");
        
        return (distinctServiceInjections, source, usingNamespaces.Distinct().ToList(), isAsync);
    }


    /// <summary>
    /// Adds "new" keyword in front of curly braces.
    /// </summary>
    private static string ReplaceObjectCreation(string source)
    {
        var objectCreationRx = new Regex(@$"(?<=(=>)|:)\s*\(?\s*(?<object>{MatchBrackets(BracketType.CurlyBrackets)}\s*)\)?");
        
        Match match;
        while ((match = objectCreationRx.Match(source)).Value != "")
            source = source.Replace(match.Value, " new " + match.Groups["object"].Value);

        return source;
    }


    /// <summary>
    /// Replaces colon token with equals token.
    /// </summary>
    private static string ReplaceAssignments(string source)
    {
        source = FieldAssignmentRx.Replace(source, "${leading}${name} =");
        return source;
    }

    /// <summary>
    /// Uppercases nominal assignments in object values.
    /// </summary>
    private static string UpperCaseNominalAssignment(string source)
    {
        // Lock arrays
        var lockValue = "@rrayL0ck";
        var lockedValues = new List<string>();

        var idx = 0;
        foreach (var match in Regex.Matches(source, MatchBrackets(BracketType.SquareBrackets)).Where(e => e.Length > 2).ToList())
        {
            lockedValues.Add(match.Value);
            source = source.Replace(match.Value, lockValue + idx++);
        }

        // Apply Nominal transformation
        source = NominalFieldAssignmentRx.Replace(source, m => $"{CapitalizeWord(m.Groups["name"])}: {m.Groups["name"]}");

        // Unlock arrays
        idx = 0;
        foreach (var lockedValue in lockedValues) source = source.Replace(lockValue + idx++, lockedValue);

        return source;
    }

    

    private static string ReplaceSpreadSyntax(string source)
    {
        source = Regex.Replace(source, @"\.\.\.(\w+)",  SpreadFunctionName + "($1)");
        return source;
    }
    
    /// <summary>
    /// Replaces LINQ method names.
    /// </summary>
    private static string ReplaceMethodNames(string source)
    {
        return source
            .Replace(".map(", ".Select(")
            .Replace(".filter(", ".Where(")
            .Replace(".orderBy(", ".OrderBy(")
            .Replace(".orderByDescending(", ".OrderByDescending(")
            .Replace(".join(", ".Join(")
            .Replace(".groupJoin(", ".GroupJoin(")
            .Replace(".flatMap(", ".SelectMany(")
            .Replace(".find(", ".FirstOrDefault(")
            .Replace(".findAsync(", ".FirstOrDefault(")
            .Replace(".findWithoutDefault(", ".First(")
            .Replace(".toList(", ".ToList(")
            // .Replace(".toListAsync(", ".ToList(")
            .Replace(".toListAsync()", "")
            
            .Replace(".some(", ".Any(")
            .Replace(".every(", ".All(")
            .Replace(".includes(", ".Contains(")
            
            .Replace(".getFullYear()", ".Year")
            .Replace(".getMonth()", ".Month")
            .Replace(".getDay()", ".Day")
            .Replace(".getMinutes()", ".Minute")
            .Replace(".getSeconds()", ".Second")
            .Replace(".getMilliseconds()", ".Millisecond")

            .Replace(".toLowerCase()", ".ToLower()")
            .Replace(".toUpperCase()", ".ToUpper()")
            .Replace(".sum()", ".Sum(e => e)")
            .Replace(".sum(", ".Sum(");
    }

    private static string RemoveInnerQueryableExcess(string source)
    {
        return Regex.Replace(source, @"(?<=Join\(\s*)\(\s*\)\s*=>\s*this\.", "").Replace(".asQueryable()", "");
    }
    
    private static string RemoveParameterType(string source)
    {
        var parametersRx = new Regex(@$",\s*{MatchBrackets(BracketType.Parenthasis)}");

        var parameterType = new Regex(@"(?<param>\w+)\s*:\s*\w+");
        // TODO: add support for object destructuring
        source = parametersRx.Replace(source, m => parameterType.Replace(m.Value, "${param}"));

        return source;
    }

    /// <summary>
    /// Replaces camelCase with PascalCase on all member accesses.
    /// </summary>
    private static string RestorePascalCaseForMembers(string source)
    {
        // Change case
        source = Regex.Replace(source, @"(?<!\.)\.\w", m => "." + m.Value[1].ToString().ToUpper());
        source = FieldAssignmentRx.Replace(source, m => $"{m.Groups["leading"]}{CapitalizeWord(m.Groups["name"].Value)} =");

        return source;
    }

    public static string RemoveCasting(string source)
    {
        return CastAndConversionRx.Replace(source, "");
    }
    

    private static (string source, Func<string, string> unlock) LockStrings(string source)
    {
        var lockValue = "R@ndomW8rrrd";
        var lockedValues = new List<string>();
        var idx = 0;

        // Lock strings so their contents are not affected by case change
        foreach (var match in Regex.Matches(source, MatchBalancedTokens("\"")).ToList())
        {
            lockedValues.Add(match.Value);
            source = source.Replace(match.Value, lockValue + idx++);
        }

        string UnLock(string source)
        {
            // Restore strings from lock values
            idx = 0;
            foreach (var lockedValue in lockedValues) source = source.Replace(lockValue + idx++, lockedValue);

            return source;
        }

        return (source, UnLock);
    }


    /// <summary>
    /// Slice method is replaced as a combination of Skip and Take.
    /// </summary>
    private static string ReplaceSlice(string source)
    {
        var sliceRx = new Regex(@"\s*\.slice\(\s*(?<arg1>\d+)(?:,\s*(?<arg2>\d+))?\s*\)");
        var leadingTriviaRx = new Regex(@"^\s*");

        foreach (var match in sliceRx.Matches(source).ToList())
        {
            var leadingTrivia = leadingTriviaRx.Match(match.Value).Value;
            var replacement = $"{leadingTrivia}.Skip({match.Groups["arg1"].Value})";

            if (match.Groups["arg2"].Value != "")
            {
                var arg1 = int.Parse(match.Groups["arg1"].Value);
                var arg2 = int.Parse(match.Groups["arg2"].Value);

                var takeNum = arg2 - arg1;
                if (takeNum < 0) takeNum = 0;

                replacement += $"{leadingTrivia}.Take({takeNum})";
            }

            source = source.Replace(match.Value, replacement);
        }

        return source;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    private static string NullCoalesceInnerQueryable(string source)
    {
        source = new Regex(@"(?<=(Join|GroupJoin)\s*\(\s*)(.+)(,\s*""([\w\.]+?)""\s*,\s*""([\w\.]+?)""\s*,\s*\(\s*\w+\s*,\s*\w+\s*\))").Replace(source, "$2 ?? new()$3");

        return source;
    }
    
    /// <summary>
    /// Replace string keys with appropriate LINQ lambda key selectors.
    /// </summary>
    private static string ReplaceKeySelectors(string source)
    {
        // OrderBy
        source = new Regex(@"\.(\s*)OrderBy\(""(?<key>[\w\.]+?)""\)").Replace(source, m => RestorePascalCaseForMembers($".OrderBy(e => e.{m.Groups["key"].Value})"));
        
        // Join / GroupJoin
        source = new Regex(@"""([\w\.]+?)""\s*,\s*""([\w\.]+?)""\s*,(\s*\(\s*\w+\s*,\s*\w+\s*\))").Replace(source, 
            m => RestorePascalCaseForMembers($"e => e.{m.Groups[1].Value}, e => e.{m.Groups[2].Value}, {m.Groups[3].Value}"));
        return source;
    }

    /// <summary>
    /// Replaces single-quoted strings with double-quoted strings.
    /// </summary>
    private static string ReplaceSingleWithDoubleQuotes(string source)
    {
        var lockValue = "R@ndomW8rrrd";
        
        // Lock escaped single quotes
        source = source.Replace("\\'", lockValue);

        // Extract strings to replace
        // var singleQuotedStrings = new Regex("(\\'(?>\\'(?<c>)|[^'']+|\\'(?<-c>))*(?(c)(?!))\\')").Matches(source).Select(e => e.Value);
        var singleQuotedStrings = new Regex(MatchBalancedTokens("'")).Matches(source).Select(e => e.Value);

        foreach (var singleQuotedString in singleQuotedStrings)
            source = source.Replace(singleQuotedString, "\"" + singleQuotedString[1..^1] + "\"");

        source = source.Replace(lockValue, "\\'");
        
        return source;
    }

    /// <summary>
    /// Translates js array initialization to c# array initialization.
    /// Also handles the spread syntax.
    /// </summary>
    private static string ReplaceArraySyntax(string source)
    {
        var matches = Regex.Matches(source, @"(?<!\w\s*)\[.+?\]").Select(e => e.Value);

        foreach (var match in matches)
        {
            var modifiableMatch = match;
            
            var spreadMatch = Regex.Match(match, @"\.\.\.\s*\w+").Value;
            if (spreadMatch != "")
            {
                source = RemoveSpread(source, spreadMatch);
                modifiableMatch = RemoveSpread(match, spreadMatch);
            }
            
            source = source.Replace(modifiableMatch, "new[] {" + modifiableMatch[1..^1] + "}" + (spreadMatch != "" ? $".Concat({spreadMatch[3..]})" : null));
        }
        
        return source;
    }


    public static string ReplaceBracketWithDotNotation(string source)
    {
        return new Regex(@"(?<=\w\s*)\[\s*('|"")(?<property>.+)('|"")\s*\]").Replace(source, ".${property}");
    }
    
    // done using: https://github.com/dotnet/efcore/issues/19930#issuecomment-625443593
    public static string ReplaceGroupJoinWithMagicalSelect(string source)
    {
        var groupJoinRx = new Regex(@$"groupJoin\s*{MatchBrackets(BracketType.Parenthasis)}");

        while (true)
        {
            Match groupJoinMatch = null!;
            var groupJoinMatches = new List<Match>();

            while ((groupJoinMatch = groupJoinRx.Match(source, (groupJoinMatch?.Index ?? 0) + 1)).Success)
                groupJoinMatches.Add(groupJoinMatch);

            if (!groupJoinMatches.Any()) return source;

            var groupJoinStatement = groupJoinMatches.OrderBy(e => e.Value.Length).First();

            var detailedGroupJoinRx = new Regex(@$"groupJoin\s*\((?<inner>.+)\s*,(?<outerKey>{KeySelectorRx}),(?<innerKey>{KeySelectorRx}),\s*\(\s*(?<outerParam>\w+)\s*,\s*(?<innerParam>\w+)\s*\)");
            var detailedMatch = detailedGroupJoinRx.Match(groupJoinStatement.Value);
            var (inner, outerKey, innerKey, outerParam, innerParam) = (detailedMatch.Groups["inner"], detailedMatch.Groups["outerKey"].Value.Trim().Replace("\"", ""),
                detailedMatch.Groups["innerKey"].Value.Trim().Replace("\"", ""), detailedMatch.Groups["outerParam"], detailedMatch.Groups["innerParam"]);
            
            var result = $"map(e => ({{ {CapitalizeWord(outerParam)} = e, {CapitalizeWord(innerParam)} = {inner}.filter(innerE => innerE.{innerKey} == e.{outerKey}).ToList()}})).map(e"
                         + Regex.Replace(detailedGroupJoinRx.Replace(groupJoinStatement.Value, ""),
                             @$"(?<=[^\w]|...)({outerParam}|{innerParam})(?=[^\w])", "e.$1");
            
            source = source.Replace(groupJoinStatement.Value, result);
        }

    }


    public static string RemoveSpread(string source, params string[] spreadMatches)
    {
        foreach (var match in spreadMatches.Where(e => e != ""))
            source = source.Replace(match, "");
        
        source = Regex.Replace(source, @"\[\s*,", "[");
        source = Regex.Replace(source, @",\s*]", "]");
        source = Regex.Replace(source, @",\s*,", ",");
        return source;
    }

    /// <summary>
    /// Adds exclamation mark to suppress nullability warnin of the retunr type.
    /// </summary>
    private static string AddNullForgiving(string source, Compilation compilation)
    {
        if (compilation.Options.NullableContextOptions == NullableContextOptions.Enable)
            return new Regex(@"(;\s*$)").Replace(source, "!$1");
        else
            return source;
    }

    private static string RemoveRemainingThisKeywords(string source)
    {
        return source.Replace("this.", "");
    }

    private static (string, string) GenerateFiles(List<TranslatedEndpoint> translations)
    {
        var allServices = translations.SelectMany(t => t.InjectedServices).DistinctBy(t => t.Name).ToList();
        var uniqueServices = allServices.DistinctBy(t => t.Type).ToList();
        var namespaces = translations.SelectMany(t => t.UsingNamespaces).Distinct();

        return ($@"using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
{string.Join("\n", namespaces.Select(n => $"using {n};"))}

namespace GeneratedControllers;

public class {EndpointGenInitializer.QueryImplementationsFile[..^3]} : ControllerBase
{{
    {string.Join("\n\t", allServices.Select(e => $"{e.Type} {e.Name};"))}

    public {EndpointGenInitializer.QueryImplementationsFile[..^3]}({string.Join(", ", uniqueServices.Select(e => $"{e.Type} {e.Name}"))})
    {{
        {string.Join("\n\t\t", allServices.Select(e => $"this.{e.Name} = {uniqueServices.First(s => s.Type == e.Type).Name};"))}
    }}
    
{string.Join("\n", translations.Select(t => $@"
    public virtual {(t.isAsync ? "async Task<object>" : "object")} {t.ActionName}({string.Join(", ", t.ActionParameters.Select(p => $"{p.Type} {p.Name}"))})
    {{
        {t.Body}
    }}
"))}
}}


{string.Join("\n", translations.Select(e => e.GeneratedRecordTypes))}
",
            $@"using Microsoft.AspNetCore.Mvc;
{string.Join("\n", namespaces.Select(n => $"using {n};"))}

namespace GeneratedControllers;

[ApiController]
[Route(""[controller]/[action]"")]
public class {EndpointGenInitializer.QueryControllerFile[..^3]} : {EndpointGenInitializer.QueryImplementationsFile[..^3]}
{{
    
    public {EndpointGenInitializer.QueryControllerFile[..^3]}({string.Join(", ", uniqueServices.Select(e => $"{e.Type} {e.Name}"))}) : base({string.Join(", ", uniqueServices.Select(e => e.Name))})
    {{
    }}

{string.Join("\n", translations.Select(t => $@"
    [HttpGet]
    public override {(t.isAsync ? "Task<object>" : "object")} {t.ActionName}({string.Join(", ", t.ActionParameters.Select(p => $"{p.Type} {p.Name}"))}) => base.{t.ActionName}({string.Join(", ", t.ActionParameters.Select(p => p.Name))});
"))}

}}
");
    }
    

    /// <summary>
    /// Translate TS primitive types to C# types.
    /// </summary>
    private static string TranslateNamedTypeOrArray(string type)
    {
        return type switch
        {
            { } x when x.EndsWith("[]") => $"{TranslateNamedTypeOrArray(x[..^2])}[]",
            "number" => "decimal",
            "string" => "string",
            "boolean" => "bool",
            "Date" => "DateTime",
            "any" => "object",
            _ => type
        };
    }

    /// <summary>
    /// Result of the translation of a single custom endpoint.
    /// </summary>
    /// <param name="ActionName">Name of a new action.</param>
    /// <param name="ActionParameters">A new action's parameters.</param>
    /// <param name="Body">Body of the action.</param>
    /// <param name="InjectedServices">Services that will have to be injected into controller class.</param>
    /// <param name="GeneratedRecordTypes">Generated record types used for parameter binding.</param>
    private record TranslatedEndpoint(string ActionName, List<(string Name, string Type)> ActionParameters, string Body,
        List<(string Name, string Type)> InjectedServices, string GeneratedRecordTypes, List<string> UsingNamespaces, bool isAsync);
}