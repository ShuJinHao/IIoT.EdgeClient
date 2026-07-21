#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace IIoT.Edge.ContractLedger;

public sealed class ContractLedgerAnalysis
{
    public List<ContractLedgerSymbolUsage> SymbolUsages { get; } = new();
    public List<ContractLedgerDiagnostic> CompilationErrors { get; } = new();
}

public sealed class ContractLedgerAssemblyReference
{
    public string SourcePath { get; init; } = string.Empty;
    public string SourceAssembly { get; init; } = string.Empty;
    public string ReferencedAssembly { get; init; } = string.Empty;
    public string ReferencedVersion { get; init; } = string.Empty;
    public string ReferencedCulture { get; init; } = string.Empty;
    public string ReferencedPublicKeyToken { get; init; } = string.Empty;
}

public sealed class ContractLedgerAssemblyInput
{
    public string SourcePath { get; init; } = string.Empty;
    public string AssemblyName { get; init; } = string.Empty;
    public string AssemblyVersion { get; init; } = string.Empty;
    public string Culture { get; init; } = string.Empty;
    public string PublicKeyToken { get; init; } = string.Empty;
    public string Mvid { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed class ContractLedgerSymbolUsage
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

public sealed class ContractLedgerDiagnostic
{
    public string Id { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
}

public static class EdgePluginContractLedgerRoslyn
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static ContractLedgerAnalysis Analyze(
        string assemblyName,
        string repositoryRoot,
        string pluginRoot,
        string generatedRoot,
        string[] compileSourcePaths,
        string[] generatedSourcePaths,
        string[] referencePaths,
        string[] preprocessorSymbols)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        pluginRoot = Path.GetFullPath(pluginRoot);
        generatedRoot = Path.GetFullPath(generatedRoot);

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            DocumentationMode.None,
            SourceCodeKind.Regular,
            preprocessorSymbols.Where(static value => !string.IsNullOrWhiteSpace(value)));

        var compileInputs = compileSourcePaths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Select(path => new SourceInput(path, GetRepositoryPath(repositoryRoot, path)));
        var generatedInputs = generatedSourcePaths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Select(path => new SourceInput(
                path,
                "generated/" + Path.GetRelativePath(generatedRoot, path).Replace('\\', '/')));
        var sourceInputs = compileInputs
            .Concat(generatedInputs)
            .GroupBy(static input => input.ActualPath, PathComparer.Instance)
            .Select(static group => group.First())
            .OrderBy(static input => input.VirtualPath, StringComparer.Ordinal)
            .ToArray();

        var syntaxTrees = sourceInputs
            .Select(input => CSharpSyntaxTree.ParseText(
                File.ReadAllText(input.ActualPath),
                parseOptions,
                input.VirtualPath,
                Encoding.UTF8))
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
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));

        var result = new ContractLedgerAnalysis();
        foreach (var diagnostic in compilation.GetDiagnostics()
                     .Where(static item => item.Severity == DiagnosticSeverity.Error)
                     .OrderBy(static item => item.Location.SourceTree?.FilePath, PathComparer.Instance)
                     .ThenBy(static item => item.Location.SourceSpan.Start)
                     .ThenBy(static item => item.Id, StringComparer.Ordinal))
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            result.CompilationErrors.Add(new ContractLedgerDiagnostic
            {
                Id = diagnostic.Id,
                Message = diagnostic.GetMessage(),
                SourcePath = GetRepositoryPath(repositoryRoot, lineSpan.Path),
                Line = lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : 0,
                Column = lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : 0
            });
        }

        var usageKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in syntaxTrees.OrderBy(static tree => tree.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (node is IdentifierNameSyntax identifier)
                {
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node,
                        model.GetAliasInfo(identifier)?.Target, "alias-target");
                }
                if (node is SimpleNameSyntax or PredefinedTypeSyntax or AttributeSyntax or BaseTypeSyntax)
                {
                    var symbolInfo = model.GetSymbolInfo(node);
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node,
                        symbolInfo.Symbol, GetUsageKind(node));
                    foreach (var candidate in symbolInfo.CandidateSymbols)
                    {
                        AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node,
                            candidate, "candidate-symbol");
                    }
                    var typeInfo = model.GetTypeInfo(node);
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node,
                        typeInfo.Type, "semantic-type");
                    if (!SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType))
                    {
                        AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node,
                            typeInfo.ConvertedType, "implicit-conversion-type");
                    }
                }

                var operation = model.GetOperation(node);
                if (operation is { Parent: null })
                {
                    foreach (var descendantOperation in operation.DescendantsAndSelf())
                    {
                        AddOperationSymbols(result, usageKeys, assemblyName, tree.FilePath, descendantOperation);
                    }
                }

                if (node is AwaitExpressionSyntax awaitExpression)
                {
                    var awaitInfo = model.GetAwaitExpressionInfo(awaitExpression);
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, awaitInfo.GetAwaiterMethod, "awaiter-get-awaiter");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, awaitInfo.IsCompletedProperty, "awaiter-is-completed");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, awaitInfo.GetResultMethod, "awaiter-get-result");
                }
                if (node is CommonForEachStatementSyntax forEachStatement)
                {
                    var forEachInfo = model.GetForEachStatementInfo(forEachStatement);
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.GetEnumeratorMethod, "foreach-get-enumerator");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.MoveNextMethod, "foreach-move-next");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.CurrentProperty, "foreach-current");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.DisposeMethod, "foreach-dispose");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.ElementType, "foreach-element-type");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.ElementConversion.MethodSymbol, "foreach-element-conversion");
                    AddSymbolUsage(result, usageKeys, assemblyName, tree.FilePath, node, forEachInfo.CurrentConversion.MethodSymbol, "foreach-current-conversion");
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

    public static ContractLedgerAssemblyReference[] ReadAssemblyReferences(
        string repositoryRoot,
        string[] assemblyPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(assemblyPaths);

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var references = new List<ContractLedgerAssemblyReference>();
        foreach (var assemblyPath in assemblyPaths
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(PathComparer.Instance)
                     .OrderBy(static path => path, PathComparer.Instance))
        {
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("Assembly input does not exist.", assemblyPath);
            }

            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException($"Assembly input has no metadata: {assemblyPath}");
            }

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                throw new BadImageFormatException($"PE input is not an assembly: {assemblyPath}");
            }

            var definition = reader.GetAssemblyDefinition();
            var sourceAssembly = reader.GetString(definition.Name);
            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                var culture = reference.Culture.IsNil ? string.Empty : reader.GetString(reference.Culture);
                var publicKeyOrToken = reference.PublicKeyOrToken.IsNil
                    ? Array.Empty<byte>()
                    : reader.GetBlobBytes(reference.PublicKeyOrToken);
                references.Add(new ContractLedgerAssemblyReference
                {
                    SourcePath = GetRepositoryPath(repositoryRoot, assemblyPath),
                    SourceAssembly = sourceAssembly,
                    ReferencedAssembly = reader.GetString(reference.Name),
                    ReferencedVersion = reference.Version.ToString(),
                    ReferencedCulture = string.IsNullOrWhiteSpace(culture) ? "neutral" : culture,
                    ReferencedPublicKeyToken = publicKeyOrToken.Length == 0
                        ? "none"
                        : Convert.ToHexString(publicKeyOrToken).ToLowerInvariant()
                });
            }
        }

        return references
            .OrderBy(static item => item.SourceAssembly, StringComparer.Ordinal)
            .ThenBy(static item => item.ReferencedAssembly, StringComparer.Ordinal)
            .ThenBy(static item => item.ReferencedVersion, StringComparer.Ordinal)
            .ThenBy(static item => item.ReferencedCulture, StringComparer.Ordinal)
            .ThenBy(static item => item.ReferencedPublicKeyToken, StringComparer.Ordinal)
            .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ToArray();
    }

    public static ContractLedgerAssemblyInput[] ReadAssemblyInputs(
        string repositoryRoot,
        string[] assemblyPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var results = new List<ContractLedgerAssemblyInput>();
        foreach (var assemblyPath in assemblyPaths
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(PathComparer.Instance)
                     .OrderBy(static path => path, PathComparer.Instance))
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata || !peReader.GetMetadataReader().IsAssembly)
            {
                throw new BadImageFormatException($"PE input is not a managed assembly: {assemblyPath}");
            }
            var reader = peReader.GetMetadataReader();
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var publicKeyToken = assemblyName.GetPublicKeyToken() ?? Array.Empty<byte>();
            var fileInfo = new FileInfo(assemblyPath);
            stream.Position = 0;
            results.Add(new ContractLedgerAssemblyInput
            {
                SourcePath = GetRepositoryPath(repositoryRoot, assemblyPath),
                AssemblyName = assemblyName.Name ?? string.Empty,
                AssemblyVersion = assemblyName.Version?.ToString() ?? string.Empty,
                Culture = string.IsNullOrWhiteSpace(assemblyName.CultureName) ? "neutral" : assemblyName.CultureName,
                PublicKeyToken = publicKeyToken.Length == 0 ? "none" : Convert.ToHexString(publicKeyToken).ToLowerInvariant(),
                Mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D"),
                Size = fileInfo.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            });
        }
        return results
            .OrderBy(static item => item.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static item => item.AssemblyVersion, StringComparer.Ordinal)
            .ThenBy(static item => item.Culture, StringComparer.Ordinal)
            .ThenBy(static item => item.PublicKeyToken, StringComparer.Ordinal)
            .ThenBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddOperationSymbols(
        ContractLedgerAnalysis result,
        HashSet<string> usageKeys,
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
        AddSymbolUsage(result, usageKeys, assemblyName, sourcePath, operation.Syntax, symbol,
            "operation-symbol:" + operation.Kind);
        if (operation is IConversionOperation or IObjectCreationOperation or IArrayCreationOperation or
            IInvocationOperation or IAwaitOperation or IForEachLoopOperation)
        {
            AddSymbolUsage(result, usageKeys, assemblyName, sourcePath, operation.Syntax, operation.Type,
                "operation-type:" + operation.Kind);
        }
    }

    private static void AddSymbolUsage(
        ContractLedgerAnalysis result,
        HashSet<string> usageKeys,
        string assemblyName,
        string sourcePath,
        SyntaxNode node,
        ISymbol? symbol,
        string usageKind)
    {
        if (symbol is null || symbol.Kind == SymbolKind.Namespace)
        {
            return;
        }
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }
        if (symbol is IMethodSymbol { ReducedFrom: not null } reducedMethod)
        {
            symbol = reducedMethod.ReducedFrom;
        }
        var ownerIdentity = symbol.ContainingAssembly?.Identity;
        var ownerAssembly = ownerIdentity?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerAssembly) || ownerAssembly.Equals(assemblyName, StringComparison.Ordinal))
        {
            return;
        }
        var lineSpan = node.GetLocation().GetLineSpan();
        var normalizedSymbol = NormalizeSymbol(symbol);
        var line = lineSpan.StartLinePosition.Line + 1;
        var column = lineSpan.StartLinePosition.Character + 1;
        var ownerVersion = ownerIdentity?.Version.ToString() ?? string.Empty;
        var ownerCulture = string.IsNullOrWhiteSpace(ownerIdentity?.CultureName) ? "neutral" : ownerIdentity!.CultureName;
        var ownerPublicKeyToken = ownerIdentity is null || ownerIdentity.PublicKeyToken.IsDefaultOrEmpty
            ? "none"
            : Convert.ToHexString(ownerIdentity.PublicKeyToken.ToArray()).ToLowerInvariant();
        var key = string.Join("\u001f", sourcePath, line, column, ownerAssembly, ownerVersion,
            ownerCulture, ownerPublicKeyToken, normalizedSymbol, usageKind);
        if (!usageKeys.Add(key))
        {
            return;
        }
        result.SymbolUsages.Add(new ContractLedgerSymbolUsage
        {
            SourcePath = sourcePath,
            Line = line,
            Column = column,
            Symbol = normalizedSymbol,
            SymbolKind = symbol.Kind.ToString(),
            OwnerAssembly = ownerAssembly,
            OwnerAssemblyVersion = ownerVersion,
            OwnerAssemblyCulture = ownerCulture,
            OwnerAssemblyPublicKeyToken = ownerPublicKeyToken,
            ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            UsageKind = usageKind
        });
    }

    private static string NormalizeSymbol(ISymbol symbol)
    {
        var value = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return value.Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string GetUsageKind(SyntaxNode node)
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

    private static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) &&
               !Path.IsPathRooted(relative);
    }

    private static string GetRepositoryPath(string repositoryRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        var fullPath = Path.GetFullPath(path);
        return IsInside(repositoryRoot, fullPath)
            ? Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/')
            : Path.GetFileName(fullPath);
    }

    private sealed record SourceInput(string ActualPath, string VirtualPath);

    private sealed class PathComparer : IComparer<string>, IEqualityComparer<string>
    {
        public static PathComparer Instance { get; } = new();

        public int Compare(string? x, string? y) =>
            string.Compare(x, y, PathComparison);

        public bool Equals(string? x, string? y) =>
            string.Equals(x, y, PathComparison);

        public int GetHashCode(string value) =>
            (OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value).GetHashCode(StringComparison.Ordinal);
    }
}
