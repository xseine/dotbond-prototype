using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotBond.Generators;
using DotBond.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBond.Workspace;

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
                "DateTime" or "DateTimeOffset" or "DateOnly" => "Date",
                "Guid" or "String" or "string" or "char" => "string",
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
            if (type.IsKind(SyntaxKind.PredefinedType))
                return GetPrimitiveTsType(type.ToString()) ?? type.ToString();
            
            return ParseType(semanticModel.SyntaxTree.GetRoot().Contains(type) ? semanticModel.GetTypeInfo(type).Type ?? semanticModel.GetSymbolInfo(type).Symbol as ITypeSymbol : null);
        }

        /// <summary>
        /// Converts lists, dictionaries and primitive types to their TS counterparts.
        /// </summary>
        /// <param name="typeSymbol"></param>
        /// <returns></returns>
        public static string ParseType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null) return "";
            if (typeSymbol.Kind == SymbolKind.ErrorType) return "any";
            
            var result = typeSymbol switch
            {
                IArrayTypeSymbol array => ParseType(array.ElementType) + "[]",
                INamedTypeSymbol
                {
                    IsGenericType: true,
                    Name: "List" or "IList" or "IEnumerable" or "ICollection" or "IQueryable"
                    or "HashSet" or "IReadOnlyCollection" or "IReadOnlyList"
                } collection => ParseType(collection.TypeArguments.First()) + "[]",

                INamedTypeSymbol
                {
                    IsGenericType: true,
                    Name: "IDictionary" or "Dictionary" or "KeyValuePair"
                    or "SortedDictionary" or "IReadOnlyDictionary" or "ReadOnlyDictionary"
                } dictionary => $"{{[key: {ParseType(dictionary.TypeArguments[0])}]" + 
                                $": {ParseType(dictionary.TypeArguments[1])}}}",
                
                INamedTypeSymbol {IsGenericType: true, Name: "Nullable"} nullable => $"{ParseType(nullable.TypeArguments.First())} | null",
                
                INamedTypeSymbol {IsGenericType: true} generic =>
                    $"{generic.Name}<{string.Join(", ", generic.TypeArguments.Select((arg, _) => ParseType(arg)))}>",
                // INamedTypeSymbol nonGeneric => nonGeneric.Name,
                _ => GetPrimitiveTsType(typeSymbol.Name) ?? (!typeSymbol.DeclaringSyntaxReferences.Any() ? "any" : typeSymbol.Name)
            };
            
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
            if (RoslynUtilities.InheritsFromController(typeSymbol.ContainingType))
                result += ApiGenerator.ControllerImportSuffix;
            
            return result[1..];
        }
    }
}