using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.Workspace;

namespace ConsoleApp1.Common
{
    internal static class TypeTranslation
    {
        internal static FileObservable FileObservable { get; set; }

        public static string GetPrimitiveTsType(string type)
        {
            return type switch
            {
                "int" or "double" or "float" or "Int32"
                    or "Int64" or "UInt32" or "UInt64" or "short" or "byte" or "long" or "decimal" => "number",
                "bool" => "boolean",
                "DateTime" or "DateTimeOffset" => "Date",
                "Guid" or "String" or "char" => "string",
                "dynamic" or "object" => "any",
                _ => null
            };
        }

        /// <summary>
        /// Converts lists, dictionaries and primitive types to their TS counterparts.
        /// </summary>
        /// <param name="type">String representation of the type to convert.</param>
        /// <param name="semanticModel"></param>
        /// <returns></returns>
        public static string ParseType(TypeSyntax type, SemanticModel semanticModel)
        {
            return ParseType(type, semanticModel.SyntaxTree.GetRoot().Contains(type) ? semanticModel.GetTypeInfo(type).Type ?? semanticModel.GetSymbolInfo(type).Symbol as ITypeSymbol : null);
        }

        /// <summary>
        /// Converts lists, dictionaries and primitive types to their TS counterparts.
        /// </summary>
        /// <param name="type">String representation of the type to convert.</param>
        /// <param name="typeSymbol"></param>
        /// <returns></returns>
        public static string ParseType(TypeSyntax type, ITypeSymbol typeSymbol)
        {
            if (type == null) return "";

            var isNullable = type.IsKind(SyntaxKind.NullableType);
            if (isNullable)
                type = ((NullableTypeSyntax)type).ElementType;

            var result = type switch
            {
                ArrayTypeSyntax array => ParseType(array.ElementType, ((IArrayTypeSymbol)typeSymbol).ElementType) + "[]",
                GenericNameSyntax
                {
                    Identifier.Text: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable"
                    or "HashSet" or "IReadOnlyCollection" or "IReadOnlyList"
                } collection when typeSymbol is INamedTypeSymbol or null => ParseType(collection.TypeArgumentList.Arguments.First(), (typeSymbol as INamedTypeSymbol)?.TypeArguments.First()) + "[]",

                GenericNameSyntax
                {
                    Identifier.Text: "IDictionary" or "Dictionary" or "KeyValuePair"
                    or "SortedDictionary" or "IReadOnlyDictionary" or "ReadOnlyDictionary"
                } dictionary => $"{{[key: {ParseType(dictionary.TypeArgumentList.Arguments[0], (typeSymbol as INamedTypeSymbol)?.TypeArguments[0])}]" + 
                                $": {ParseType(dictionary.TypeArgumentList.Arguments[1], (typeSymbol as INamedTypeSymbol)?.TypeArguments[1])}}}",

                GenericNameSyntax generic when typeSymbol is INamedTypeSymbol or null =>
                    $"{generic.Identifier.Text}<{string.Join(", ", generic.TypeArgumentList.Arguments.Select((arg, idx) => ParseType(arg, (typeSymbol as INamedTypeSymbol)?.TypeArguments[idx])))}>",
                _ => GetPrimitiveTsType(type.ToString()) ?? type.ToString()
            };

            // Handle nullable
            result += isNullable ? " | null" : null;
            // Handle nested types
            var containingPath = GetContainingTypesPath(typeSymbol);
            if (containingPath != null) result = containingPath + "." + result;

            return result;
        }

        public static string GetContainingTypesPath(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null) return null;
            if (typeSymbol.ContainingType == null) return null;
            var result = GetContainingTypesPath(typeSymbol.ContainingType) + "." + typeSymbol.ContainingType.Name;
            return result[1..];
        }
    }
}