#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IIoT.Edge.ContractLedger.Validation;

public sealed class ValidatorRoslynResult
{
    public List<ValidatorRoslynUsage> SymbolUsages { get; } = new();
    public List<string> CompilationErrors { get; } = new();
}

public sealed class ValidatorRoslynUsage
{
    public string SourcePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string SymbolKind { get; init; } = string.Empty;
    public string OwnerAssembly { get; init; } = string.Empty;
    public string OwnerAssemblyVersion { get; init; } = string.Empty;
    public string OwnerAssemblyCulture { get; init; } = string.Empty;
    public string OwnerAssemblyPublicKeyToken { get; init; } = string.Empty;
    public string ContainingNamespace { get; init; } = string.Empty;
    public string UsageKind { get; init; } = string.Empty;
}

// This implementation is intentionally independent of eng/EdgePluginContractLedger.Roslyn.cs.
// The production generator and this validator must agree only through their serialized facts.
public static class EdgePluginContractLedgerValidatorRoslyn
{
    public static ValidatorRoslynResult Analyze(
        string assemblyName,
        string repositoryRoot,
        string generatedRoot,
        string[] compileSourcePaths,
        string[] generatedSourcePaths,
        string[] referencePaths,
        string[] preprocessorSymbols)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        generatedRoot = Path.GetFullPath(generatedRoot);
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            DocumentationMode.None,
            SourceCodeKind.Regular,
            preprocessorSymbols.Where(static value => !string.IsNullOrWhiteSpace(value)));

        var inputs = compileSourcePaths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Select(path => new Input(path, RepositoryPath(repositoryRoot, path)))
            .Concat(generatedSourcePaths
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Select(path => new Input(path,
                    "generated/" + Path.GetRelativePath(generatedRoot, path).Replace('\\', '/'))))
            .GroupBy(static item => item.ActualPath, PathComparer.Instance)
            .Select(static group => group.First())
            .OrderBy(static item => item.VirtualPath, StringComparer.Ordinal)
            .ToArray();
        var trees = inputs.Select(input => CSharpSyntaxTree.ParseText(
                File.ReadAllText(input.ActualPath), parseOptions, input.VirtualPath, Encoding.UTF8))
            .ToArray();
        var references = referencePaths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(PathComparer.Instance)
            .OrderBy(static path => path, PathComparer.Instance)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));

        var result = new ValidatorRoslynResult();
        foreach (var diagnostic in compilation.GetDiagnostics()
                     .Where(static item => item.Severity == DiagnosticSeverity.Error)
                     .OrderBy(static item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                     .ThenBy(static item => item.Location.SourceSpan.Start)
                     .ThenBy(static item => item.Id, StringComparer.Ordinal))
        {
            var span = diagnostic.Location.GetLineSpan();
            result.CompilationErrors.Add(
                $"{diagnostic.Id}|{RepositoryPath(repositoryRoot, span.Path)}|" +
                $"{(span.IsValid ? span.StartLinePosition.Line + 1 : 0)}|" +
                $"{(span.IsValid ? span.StartLinePosition.Character + 1 : 0)}|{diagnostic.GetMessage()}");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in trees.OrderBy(static item => item.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
            {
                if (node is IdentifierNameSyntax identifier)
                {
                    Add(result, keys, assemblyName, tree.FilePath, node,
                        model.GetAliasInfo(identifier)?.Target, "alias-target");
                }
                if (node is SimpleNameSyntax or PredefinedTypeSyntax or AttributeSyntax or BaseTypeSyntax)
                {
                    var symbolInfo = model.GetSymbolInfo(node);
                    Add(result, keys, assemblyName, tree.FilePath, node, symbolInfo.Symbol, UsageKind(node));
                    foreach (var candidate in symbolInfo.CandidateSymbols)
                    {
                        Add(result, keys, assemblyName, tree.FilePath, node, candidate, "candidate-symbol");
                    }
                    var typeInfo = model.GetTypeInfo(node);
                    Add(result, keys, assemblyName, tree.FilePath, node, typeInfo.Type, "semantic-type");
                    if (!SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType))
                    {
                        Add(result, keys, assemblyName, tree.FilePath, node,
                            typeInfo.ConvertedType, "implicit-conversion-type");
                    }
                }

                var operation = model.GetOperation(node);
                if (operation is { Parent: null })
                {
                    foreach (var descendantOperation in operation.DescendantsAndSelf())
                    {
                        AddOperation(result, keys, assemblyName, tree.FilePath, descendantOperation);
                    }
                }
                if (node is AwaitExpressionSyntax awaitExpression)
                {
                    var info = model.GetAwaitExpressionInfo(awaitExpression);
                    Add(result, keys, assemblyName, tree.FilePath, node, info.GetAwaiterMethod, "awaiter-get-awaiter");
                    Add(result, keys, assemblyName, tree.FilePath, node, info.IsCompletedProperty, "awaiter-is-completed");
                    Add(result, keys, assemblyName, tree.FilePath, node, info.GetResultMethod, "awaiter-get-result");
                }
                if (node is CommonForEachStatementSyntax forEachStatement)
                {
                    var info = model.GetForEachStatementInfo(forEachStatement);
                    Add(result, keys, assemblyName, tree.FilePath, node, info.GetEnumeratorMethod, "foreach-get-enumerator");
                    Add(result, keys, assemblyName, tree.FilePath, node, info.MoveNextMethod, "foreach-move-next");
                    Add(result, keys, assemblyName, tree.FilePath, node, info.CurrentProperty, "foreach-current");
                    Add(result, keys, assemblyName, tree.FilePath, node, info.DisposeMethod, "foreach-dispose");
                    Add(result, keys, assemblyName, tree.FilePath, node, info.ElementType, "foreach-element-type");
                    Add(result, keys, assemblyName, tree.FilePath, node,
                        info.ElementConversion.MethodSymbol, "foreach-element-conversion");
                    Add(result, keys, assemblyName, tree.FilePath, node,
                        info.CurrentConversion.MethodSymbol, "foreach-current-conversion");
                }
            }
        }

        var ordered = result.SymbolUsages
            .OrderBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Line)
            .ThenBy(static item => item.Column)
            .ThenBy(static item => item.OwnerAssembly, StringComparer.Ordinal)
            .ThenBy(static item => item.Symbol, StringComparer.Ordinal)
            .ThenBy(static item => item.UsageKind, StringComparer.Ordinal)
            .ToArray();
        result.SymbolUsages.Clear();
        result.SymbolUsages.AddRange(ordered);
        return result;
    }

    private static void AddOperation(
        ValidatorRoslynResult result,
        HashSet<string> keys,
        string assemblyName,
        string sourcePath,
        IOperation operation)
    {
        ISymbol? symbol = operation switch
        {
            IInvocationOperation value => value.TargetMethod,
            IObjectCreationOperation value => value.Constructor,
            IConversionOperation value => value.OperatorMethod,
            IBinaryOperation value => value.OperatorMethod,
            IUnaryOperation value => value.OperatorMethod,
            IIncrementOrDecrementOperation value => value.OperatorMethod,
            IPropertyReferenceOperation value => value.Property,
            IFieldReferenceOperation value => value.Field,
            IEventReferenceOperation value => value.Event,
            IMethodReferenceOperation value => value.Method,
            ITypeOfOperation value => value.TypeOperand,
            IIsTypeOperation value => value.TypeOperand,
            IDeclarationPatternOperation value => value.MatchedType,
            ICatchClauseOperation value => value.ExceptionType,
            _ => null
        };
        Add(result, keys, assemblyName, sourcePath, operation.Syntax, symbol,
            "operation-symbol:" + operation.Kind);
        if (operation is IConversionOperation or IObjectCreationOperation or IArrayCreationOperation or
            IInvocationOperation or IAwaitOperation or IForEachLoopOperation)
        {
            Add(result, keys, assemblyName, sourcePath, operation.Syntax, operation.Type,
                "operation-type:" + operation.Kind);
        }
    }

    private static void Add(
        ValidatorRoslynResult result,
        HashSet<string> keys,
        string assemblyName,
        string sourcePath,
        SyntaxNode node,
        ISymbol? symbol,
        string usageKind)
    {
        if (symbol is null || symbol.Kind == SymbolKind.Namespace) return;
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        if (symbol is IMethodSymbol { ReducedFrom: not null } reduced) symbol = reduced.ReducedFrom;
        var identity = symbol.ContainingAssembly?.Identity;
        var ownerName = identity?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerName) || ownerName.Equals(assemblyName, StringComparison.Ordinal)) return;
        var span = node.GetLocation().GetLineSpan();
        var normalized = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        var version = identity?.Version.ToString() ?? string.Empty;
        var culture = string.IsNullOrWhiteSpace(identity?.CultureName) ? "neutral" : identity!.CultureName;
        var token = identity is null || identity.PublicKeyToken.IsDefaultOrEmpty
            ? "none"
            : Convert.ToHexString(identity.PublicKeyToken.ToArray()).ToLowerInvariant();
        var line = span.StartLinePosition.Line + 1;
        var column = span.StartLinePosition.Character + 1;
        if (!keys.Add(string.Join("\u001f", sourcePath, line, column, ownerName, version, culture, token, normalized, usageKind))) return;
        result.SymbolUsages.Add(new ValidatorRoslynUsage
        {
            SourcePath = sourcePath,
            Line = line,
            Column = column,
            Symbol = normalized,
            SymbolKind = symbol.Kind.ToString(),
            OwnerAssembly = ownerName,
            OwnerAssemblyVersion = version,
            OwnerAssemblyCulture = culture,
            OwnerAssemblyPublicKeyToken = token,
            ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            UsageKind = usageKind
        });
    }

    private static string UsageKind(SyntaxNode node)
    {
        if (node.AncestorsAndSelf().Any(static item => item is AttributeSyntax))
        {
            return "attribute";
        }
        if (node.AncestorsAndSelf().Any(static item => item is BaseTypeSyntax))
        {
            return "base-type";
        }
        if (node.AncestorsAndSelf().Any(static item => item is ObjectCreationExpressionSyntax))
        {
            return "object-creation";
        }
        if (node.AncestorsAndSelf().Any(static item => item is InvocationExpressionSyntax))
        {
            return "invocation";
        }
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
        {
            return "member-access";
        }
        if (node.AncestorsAndSelf().Any(static item => item is TypeOfExpressionSyntax))
        {
            return "typeof";
        }
        if (IsTypePosition(node))
        {
            return "type-reference";
        }
        return "symbol-reference";
    }

    private static bool IsTypePosition(SyntaxNode node)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            var parent = current.Parent;
            if (parent is VariableDeclarationSyntax declaration && declaration.Type == current ||
                parent is ParameterSyntax parameter && parameter.Type == current ||
                parent is PropertyDeclarationSyntax property && property.Type == current ||
                parent is MethodDeclarationSyntax method && method.ReturnType == current ||
                parent is DelegateDeclarationSyntax @delegate && @delegate.ReturnType == current ||
                parent is CastExpressionSyntax cast && cast.Type == current ||
                parent is TypeArgumentListSyntax ||
                parent is ArrayTypeSyntax ||
                parent is NullableTypeSyntax)
            {
                return true;
            }
            if (parent is ExpressionSyntax or StatementSyntax or MemberDeclarationSyntax)
            {
                break;
            }
        }
        return false;
    }

    private static string RepositoryPath(string root, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var full = Path.GetFullPath(value);
        var relative = Path.GetRelativePath(root, full);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full.Replace('\\', '/')
            : relative.Replace('\\', '/');
    }

    private sealed record Input(string ActualPath, string VirtualPath);

    private sealed class PathComparer : IEqualityComparer<string>, IComparer<string>
    {
        public static PathComparer Instance { get; } = new();
        private static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        public bool Equals(string? x, string? y) => Comparer.Equals(x, y);
        public int GetHashCode(string value) => Comparer.GetHashCode(value);
        public int Compare(string? x, string? y) => Comparer.Compare(x, y);
    }
}
