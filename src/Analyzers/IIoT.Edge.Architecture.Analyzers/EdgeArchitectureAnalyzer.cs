using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IIoT.Edge.Architecture.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EdgeArchitectureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            EdgeArchitectureDiagnostics.ProductionTestReference,
            EdgeArchitectureDiagnostics.ProjectRoleReference,
            EdgeArchitectureDiagnostics.DomainDependency,
            EdgeArchitectureDiagnostics.RepositoryRoot,
            EdgeArchitectureDiagnostics.ApplicationProvider,
            EdgeArchitectureDiagnostics.PresentationDatabaseAccess,
            EdgeArchitectureDiagnostics.InnerLayerDatabaseAccess,
            EdgeArchitectureDiagnostics.ProviderCommitOwner,
            EdgeArchitectureDiagnostics.DapperWriteOwner,
            EdgeArchitectureDiagnostics.PluginForbiddenReference,
            EdgeArchitectureDiagnostics.PluginCrossReference,
            EdgeArchitectureDiagnostics.HostPluginReference,
            EdgeArchitectureDiagnostics.PluginRoleMetadata,
            EdgeArchitectureDiagnostics.ProductionTaskOutbound,
            EdgeArchitectureDiagnostics.PlcTransportOwner,
            EdgeArchitectureDiagnostics.SyncOverAsync,
            EdgeArchitectureDiagnostics.AsyncVoid);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilationAnalysis);
    }

    private static void StartCompilationAnalysis(CompilationStartAnalysisContext context)
    {
        var state = new CompilationState(context.Compilation);

        context.RegisterSymbolAction(state.AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSymbolAction(state.AnalyzeParameter, SymbolKind.Parameter);
        context.RegisterSymbolAction(state.AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(state.AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(state.AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(state.AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(state.AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(state.AnalyzePropertyReference, OperationKind.PropertyReference);
        context.RegisterOperationAction(state.AnalyzeVariableDeclarator, OperationKind.VariableDeclarator);
        context.RegisterCompilationEndAction(state.AnalyzeCompilationEnd);
    }

    private sealed class CompilationState
    {
        private static readonly ImmutableHashSet<string> DapperWriteMethods =
            ImmutableHashSet.Create(
                StringComparer.Ordinal,
                "Execute",
                "ExecuteAsync",
                "ExecuteScalar",
                "ExecuteScalarAsync",
                "ExecuteReader",
                "ExecuteReaderAsync");

        private readonly Compilation _compilation;
        private readonly string _assemblyName;
        private readonly EdgeProjectRole _role;
        private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<InvocationEdge>> _callGraph =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _productionTaskRoots =
            new(SymbolEqualityComparer.Default);

        internal CompilationState(Compilation compilation)
        {
            _compilation = compilation;
            _assemblyName = compilation.AssemblyName ?? string.Empty;
            _role = EdgeArchitectureRegistry.ClassifyAssembly(_assemblyName);
        }

        internal void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (!IsSourceSymbol(type))
                return;

            AnalyzeTypeUse(context, type, type.BaseType);
            foreach (var @interface in type.Interfaces)
                AnalyzeTypeUse(context, type, @interface);

            CaptureProductionTaskRoots(type);
            CaptureInterfaceAndOverrideDispatch(type);
        }

        internal void AnalyzeParameter(SymbolAnalysisContext context)
        {
            var parameter = (IParameterSymbol)context.Symbol;
            AnalyzeTypeUse(context, parameter, parameter.Type);
        }

        internal void AnalyzeField(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            AnalyzeTypeUse(context, field, field.Type);
        }

        internal void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;
            AnalyzeTypeUse(context, property, property.Type);
        }

        internal void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            if (!IsSourceSymbol(method))
                return;

            AnalyzeTypeUse(context, method, method.ReturnType);
            AnalyzeAsyncVoid(context, method);
        }

        internal void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var caller = NormalizeMethod(context.ContainingSymbol as IMethodSymbol);
            var target = NormalizeMethod(invocation.TargetMethod);
            if (caller is not null && target is not null)
            {
                _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>())
                    .Add(new InvocationEdge(
                        target,
                        invocation.Syntax.GetLocation(),
                        IsOutboundSink(invocation.TargetMethod),
                        invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            AnalyzeRepositoryOperation(context, invocation);
            AnalyzeRoleUse(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());

            if (AnalyzeSyncWait(context, invocation))
                return;

            if (AnalyzeProviderCommit(context, invocation))
                return;

            if (AnalyzeDapperWrite(context, invocation))
                return;

            AnalyzeDatabaseOperation(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            AnalyzeTransportOperation(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
        }

        internal void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            AnalyzeRepositoryOperation(context, creation);
            AnalyzeRoleUse(context, creation.Type, creation.Syntax.GetLocation());
            AnalyzeDatabaseOperation(context, creation.Type, creation.Syntax.GetLocation());
            AnalyzeTransportOperation(context, creation.Type, creation.Syntax.GetLocation());
        }

        internal void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var property = (IPropertyReferenceOperation)context.Operation;
            AnalyzeRoleUse(context, property.Property.ContainingType, property.Syntax.GetLocation());
            AnalyzeRepositoryOperation(context, property);

            if (property.Property.Name.Equals("Result", StringComparison.Ordinal) &&
                IsTaskType(property.Property.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EdgeArchitectureDiagnostics.SyncOverAsync,
                    property.Syntax.GetLocation(),
                    Display(context.ContainingSymbol),
                    property.Property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }

        internal void AnalyzeVariableDeclarator(OperationAnalysisContext context)
        {
            var variable = (IVariableDeclaratorOperation)context.Operation;
            AnalyzeRepositoryOperation(context, variable);
            if (variable.Symbol is ILocalSymbol local)
            {
                AnalyzeRoleUse(context, local.Type, variable.Syntax.GetLocation());
                AnalyzeDatabaseOperation(context, local.Type, variable.Syntax.GetLocation());
                AnalyzeTransportOperation(context, local.Type, variable.Syntax.GetLocation());
            }
        }

        internal void AnalyzeCompilationEnd(CompilationAnalysisContext context)
        {
            AnalyzeProductionTestReferences(context);
            AnalyzePluginRoleMetadata(context);
            AnalyzeProductionTaskOutboundPaths(context);
        }

        private void AnalyzeTypeUse(SymbolAnalysisContext context, ISymbol owner, ITypeSymbol? type)
        {
            if (type is null)
                return;

            var location = GetSourceLocation(owner);
            AnalyzeRepositoryType(context, owner, type, location);
            AnalyzeRoleUse(context, type, location);
            AnalyzeDatabaseType(context, owner, type, location);
            AnalyzeTransportType(context, owner, type, location);
        }

        private void AnalyzeRepositoryType(
            SymbolAnalysisContext context,
            ISymbol owner,
            ITypeSymbol type,
            Location location)
        {
            if (owner is INamedTypeSymbol namedOwner && IsRepositoryDefinition(namedOwner.OriginalDefinition))
                return;

            if (!TryFindRepositoryEntity(type, out var entity))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.RepositoryRoot,
                location,
                Display(owner),
                Display(entity),
                EdgeArchitectureRegistry.ApprovedRootSummary));
        }

        private void AnalyzeRepositoryOperation(OperationAnalysisContext context, IOperation operation)
        {
            if (operation.Type is null || !TryFindRepositoryEntity(operation.Type, out var entity))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.RepositoryRoot,
                operation.Syntax.GetLocation(),
                Display(context.ContainingSymbol),
                Display(entity),
                EdgeArchitectureRegistry.ApprovedRootSummary));
        }

        private bool TryFindRepositoryEntity(ITypeSymbol type, out ITypeSymbol entity)
        {
            entity = type;
            if (type is IArrayTypeSymbol array)
                return TryFindRepositoryEntity(array.ElementType, out entity);

            if (type is not INamedTypeSymbol named)
                return false;

            if (IsRepositoryDefinition(named.OriginalDefinition))
            {
                entity = named.TypeArguments[0];
                return entity.TypeKind != TypeKind.TypeParameter && !IsApprovedRepositoryRoot(entity);
            }

            foreach (var @interface in named.AllInterfaces)
            {
                if (!IsRepositoryDefinition(@interface.OriginalDefinition))
                    continue;

                entity = @interface.TypeArguments[0];
                if (entity.TypeKind != TypeKind.TypeParameter && !IsApprovedRepositoryRoot(entity))
                    return true;
            }

            foreach (var argument in named.TypeArguments)
            {
                if (TryFindRepositoryEntity(argument, out entity))
                    return true;
            }

            return false;
        }

        private static bool IsRepositoryDefinition(INamedTypeSymbol type)
        {
            var name = GetFullMetadataName(type);
            return name.Equals("IIoT.Edge.SharedKernel.Repository.IRepository`1", StringComparison.Ordinal) ||
                   name.Equals("IIoT.Edge.SharedKernel.Repository.IReadRepository`1", StringComparison.Ordinal);
        }

        private static bool IsApprovedRepositoryRoot(ITypeSymbol type)
            => type is INamedTypeSymbol named &&
               EdgeArchitectureRegistry.ApprovedRepositoryRoots.Contains(GetFullMetadataName(named));

        private void AnalyzeRoleUse(SymbolAnalysisContext context, ITypeSymbol? referencedType, Location location)
        {
            var diagnostic = CreateRoleDiagnostic(referencedType, location, context.Symbol);
            if (diagnostic is not null)
                context.ReportDiagnostic(diagnostic);
        }

        private void AnalyzeRoleUse(OperationAnalysisContext context, ITypeSymbol? referencedType, Location location)
        {
            var diagnostic = CreateRoleDiagnostic(referencedType, location, context.ContainingSymbol);
            if (diagnostic is not null)
                context.ReportDiagnostic(diagnostic);
        }

        private Diagnostic? CreateRoleDiagnostic(ITypeSymbol? referencedType, Location location, ISymbol owner)
        {
            var named = UnwrapNamedType(referencedType);
            if (named is null)
                return null;

            var referencedAssembly = named.ContainingAssembly?.Name ?? string.Empty;
            if (referencedAssembly.Length == 0 || referencedAssembly.Equals(_assemblyName, StringComparison.Ordinal))
                return null;

            var referencedRole = EdgeArchitectureRegistry.ClassifyAssembly(referencedAssembly);
            if (referencedRole is EdgeProjectRole.Unknown or EdgeProjectRole.Test or EdgeProjectRole.TestFixture or EdgeProjectRole.Analyzer)
                return null;

            if (_role == EdgeProjectRole.ConcretePlugin)
            {
                if (referencedRole == EdgeProjectRole.ConcretePlugin)
                {
                    return Diagnostic.Create(
                        EdgeArchitectureDiagnostics.PluginCrossReference,
                        location,
                        _assemblyName,
                        Display(named));
                }

                if (IsForbiddenPluginRole(referencedRole, referencedAssembly))
                {
                    return Diagnostic.Create(
                        EdgeArchitectureDiagnostics.PluginForbiddenReference,
                        location,
                        Display(owner),
                        Display(named));
                }
            }
            else if (EdgeArchitectureRegistry.IsHostOrCommonRole(_role) &&
                     referencedRole == EdgeProjectRole.ConcretePlugin)
            {
                return Diagnostic.Create(
                    EdgeArchitectureDiagnostics.HostPluginReference,
                    location,
                    Display(owner),
                    Display(named));
            }

            if (_role == EdgeProjectRole.Domain &&
                referencedRole != EdgeProjectRole.SharedKernel)
            {
                return Diagnostic.Create(
                    EdgeArchitectureDiagnostics.DomainDependency,
                    location,
                    Display(owner),
                    Display(named));
            }

            if (ViolatesInnerRoleMatrix(_role, referencedRole))
            {
                return Diagnostic.Create(
                    EdgeArchitectureDiagnostics.ProjectRoleReference,
                    location,
                    _assemblyName,
                    _role,
                    referencedAssembly,
                    referencedRole);
            }

            return null;
        }

        private static bool IsForbiddenPluginRole(EdgeProjectRole role, string referencedAssembly)
        {
            if (role == EdgeProjectRole.Presentation &&
                referencedAssembly.Equals("IIoT.Edge.Presentation.Navigation", StringComparison.Ordinal))
                return false;

            return role is EdgeProjectRole.Infrastructure or
                EdgeProjectRole.Presentation or
                EdgeProjectRole.VisualTestData or
                EdgeProjectRole.Host or
                EdgeProjectRole.Tool;
        }

        private static bool ViolatesInnerRoleMatrix(EdgeProjectRole source, EdgeProjectRole target)
            => source switch
            {
                EdgeProjectRole.SharedKernel => target != EdgeProjectRole.SharedKernel,
                EdgeProjectRole.Application => target is EdgeProjectRole.Infrastructure or
                    EdgeProjectRole.Presentation or
                    EdgeProjectRole.VisualTestData or
                    EdgeProjectRole.Host or
                    EdgeProjectRole.Tool or
                    EdgeProjectRole.ConcretePlugin,
                EdgeProjectRole.UiShared => target is not EdgeProjectRole.SharedKernel and not EdgeProjectRole.UiShared,
                EdgeProjectRole.ModuleSdk => target is EdgeProjectRole.Infrastructure or
                    EdgeProjectRole.Presentation or
                    EdgeProjectRole.VisualTestData or
                    EdgeProjectRole.Host or
                    EdgeProjectRole.Tool or
                    EdgeProjectRole.ConcretePlugin,
                _ => false
            };

        private void AnalyzeDatabaseType(SymbolAnalysisContext context, ISymbol owner, ITypeSymbol type, Location location)
        {
            if (!TryFindDatabaseType(type, out var databaseType) || IsDatabaseOwner())
                return;

            var descriptor = _role == EdgeProjectRole.Application
                ? EdgeArchitectureDiagnostics.ApplicationProvider
                : _role == EdgeProjectRole.Domain
                    ? EdgeArchitectureDiagnostics.DomainDependency
                    : EdgeArchitectureRegistry.IsPresentationLike(_role)
                        ? EdgeArchitectureDiagnostics.PresentationDatabaseAccess
                        : EdgeArchitectureDiagnostics.InnerLayerDatabaseAccess;

            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                location,
                Display(owner),
                Display(databaseType)));
        }

        private void AnalyzeDatabaseOperation(OperationAnalysisContext context, ITypeSymbol? type, Location location)
        {
            if (!TryFindDatabaseType(type, out var databaseType) || IsDatabaseOwner())
                return;

            var descriptor = EdgeArchitectureRegistry.IsPresentationLike(_role)
                ? EdgeArchitectureDiagnostics.PresentationDatabaseAccess
                : EdgeArchitectureDiagnostics.InnerLayerDatabaseAccess;
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                location,
                Display(context.ContainingSymbol),
                Display(databaseType)));
        }

        private bool AnalyzeProviderCommit(OperationAnalysisContext context, IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if (!method.Name.StartsWith("SaveChanges", StringComparison.Ordinal) ||
                !IsOrDerivesFromMetadataName(method.ContainingType, "Microsoft.EntityFrameworkCore.DbContext"))
                return false;

            if (!_assemblyName.Equals(EdgeArchitectureRegistry.EfOwnerAssembly, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EdgeArchitectureDiagnostics.ProviderCommitOwner,
                    invocation.Syntax.GetLocation(),
                    Display(context.ContainingSymbol),
                    method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            return true;
        }

        private bool AnalyzeDapperWrite(OperationAnalysisContext context, IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if (!DapperWriteMethods.Contains(method.Name) ||
                !GetNamespace(method.ContainingType).Equals("Dapper", StringComparison.Ordinal))
                return false;

            if (!_assemblyName.Equals(EdgeArchitectureRegistry.DapperOwnerAssembly, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EdgeArchitectureDiagnostics.DapperWriteOwner,
                    invocation.Syntax.GetLocation(),
                    Display(context.ContainingSymbol),
                    method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            return true;
        }

        private bool IsDatabaseOwner()
            => _assemblyName.Equals(EdgeArchitectureRegistry.EfOwnerAssembly, StringComparison.Ordinal) ||
               _assemblyName.Equals(EdgeArchitectureRegistry.DapperOwnerAssembly, StringComparison.Ordinal);

        private static bool TryFindDatabaseType(ITypeSymbol? type, out ITypeSymbol databaseType)
        {
            databaseType = type!;
            if (type is null)
                return false;
            if (type is IArrayTypeSymbol array)
                return TryFindDatabaseType(array.ElementType, out databaseType);
            if (type is not INamedTypeSymbol named)
                return false;

            var ns = GetNamespace(named);
            var metadataName = GetFullMetadataName(named.OriginalDefinition);
            if (ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal) ||
                ns.Equals("Dapper", StringComparison.Ordinal) ||
                ns.StartsWith("Npgsql", StringComparison.Ordinal) ||
                ns.StartsWith("SQLitePCL", StringComparison.Ordinal) ||
                ns.StartsWith("System.Data.Common", StringComparison.Ordinal) ||
                metadataName.Equals("System.Data.IDbConnection", StringComparison.Ordinal) ||
                metadataName.Equals("System.Data.IDbTransaction", StringComparison.Ordinal))
            {
                databaseType = named;
                return true;
            }

            foreach (var argument in named.TypeArguments)
            {
                if (TryFindDatabaseType(argument, out databaseType))
                    return true;
            }

            return false;
        }

        private void AnalyzeTransportType(SymbolAnalysisContext context, ISymbol owner, ITypeSymbol type, Location location)
        {
            if (!TryFindTransportType(type, out var transport) || IsApprovedTransportOwner(owner))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PlcTransportOwner,
                location,
                Display(owner),
                Display(transport)));
        }

        private void AnalyzeTransportOperation(OperationAnalysisContext context, ITypeSymbol? type, Location location)
        {
            if (!TryFindTransportType(type, out var transport) || IsApprovedTransportOwner(context.ContainingSymbol))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PlcTransportOwner,
                location,
                Display(context.ContainingSymbol),
                Display(transport)));
        }

        private bool IsApprovedTransportOwner(ISymbol owner)
        {
            if (!_assemblyName.Equals(EdgeArchitectureRegistry.DeviceCommAssembly, StringComparison.Ordinal))
                return false;

            var containingType = owner as INamedTypeSymbol ?? owner.ContainingType;
            if (containingType is null)
                return false;

            var ns = GetNamespace(containingType);
            return ns.StartsWith("IIoT.Edge.Infrastructure.DeviceComm.Plc.Services", StringComparison.Ordinal) ||
                   containingType.Name.Equals("PlcServiceFactory", StringComparison.Ordinal) ||
                   containingType.Name.StartsWith("PlcTransportOwner", StringComparison.Ordinal);
        }

        private static bool TryFindTransportType(ITypeSymbol? type, out ITypeSymbol transport)
        {
            transport = type!;
            if (type is null)
                return false;
            if (type is IArrayTypeSymbol array)
                return TryFindTransportType(array.ElementType, out transport);
            if (type is not INamedTypeSymbol named)
                return false;

            var name = GetFullMetadataName(named.OriginalDefinition);
            if (name.Equals("McpXLib.McpX", StringComparison.Ordinal) ||
                name.Equals("S7.Net.Plc", StringComparison.Ordinal) ||
                name.Equals("NModbus.IModbusMaster", StringComparison.Ordinal) ||
                name.Equals("System.Net.Sockets.TcpClient", StringComparison.Ordinal) ||
                name.Equals("System.IO.Ports.SerialPort", StringComparison.Ordinal))
            {
                transport = named;
                return true;
            }

            foreach (var argument in named.TypeArguments)
            {
                if (TryFindTransportType(argument, out transport))
                    return true;
            }

            return false;
        }

        private bool AnalyzeSyncWait(OperationAnalysisContext context, IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            var isWait = method.Name.Equals("Wait", StringComparison.Ordinal) && IsTaskType(method.ContainingType);
            var isGetResult = method.Name.Equals("GetResult", StringComparison.Ordinal) &&
                              GetNamespace(method.ContainingType).Equals("System.Runtime.CompilerServices", StringComparison.Ordinal) &&
                              (method.ContainingType.Name.IndexOf("TaskAwaiter", StringComparison.Ordinal) >= 0 ||
                               method.ContainingType.Name.IndexOf("ValueTaskAwaiter", StringComparison.Ordinal) >= 0);
            if (!isWait && !isGetResult)
                return false;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.SyncOverAsync,
                invocation.Syntax.GetLocation(),
                Display(context.ContainingSymbol),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return true;
        }

        private static bool IsTaskType(INamedTypeSymbol? type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var name = GetFullMetadataName(current.OriginalDefinition);
                if (name.Equals("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                    name.Equals("System.Threading.Tasks.Task`1", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void AnalyzeAsyncVoid(SymbolAnalysisContext context, IMethodSymbol method)
        {
            if (!method.IsAsync || !method.ReturnsVoid || method.MethodKind != MethodKind.Ordinary)
                return;
            if (IsEventHandler(method))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.AsyncVoid,
                GetSourceLocation(method),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private static bool IsEventHandler(IMethodSymbol method)
        {
            if (method.Parameters.Length != 2)
                return false;

            var eventArgs = method.Parameters[1].Type as INamedTypeSymbol;
            for (var current = eventArgs; current is not null; current = current.BaseType)
            {
                if (GetFullMetadataName(current).Equals("System.EventArgs", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void AnalyzeProductionTestReferences(CompilationAnalysisContext context)
        {
            if (_role is EdgeProjectRole.Test or EdgeProjectRole.TestFixture or EdgeProjectRole.Analyzer)
                return;

            foreach (var reference in _compilation.ReferencedAssemblyNames)
            {
                if (!EdgeArchitectureRegistry.IsTestAssembly(reference.Name))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    EdgeArchitectureDiagnostics.ProductionTestReference,
                    Location.None,
                    _assemblyName,
                    reference.Name));
            }
        }

        private void AnalyzePluginRoleMetadata(CompilationAnalysisContext context)
        {
            if (_role is not EdgeProjectRole.ConcretePlugin and not EdgeProjectRole.ModuleSdk)
                return;

            var metadata = _compilation.Assembly.GetAttributes()
                .Where(static attribute =>
                    GetFullMetadataName(attribute.AttributeClass).Equals(
                        "System.Reflection.AssemblyMetadataAttribute",
                        StringComparison.Ordinal) &&
                    attribute.ConstructorArguments.Length == 2)
                .Select(static attribute => new
                {
                    Key = attribute.ConstructorArguments[0].Value as string ?? string.Empty,
                    Value = attribute.ConstructorArguments[1].Value as string ?? string.Empty
                })
                .Where(static pair => pair.Key.Length > 0)
                .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.Ordinal);

            var expectedRole = _role == EdgeProjectRole.ModuleSdk ? "Sdk" : "Entry";
            var expectedPlugin = _role == EdgeProjectRole.ModuleSdk ? "false" : "true";
            var expectedPackable = _role == EdgeProjectRole.ModuleSdk ? "false" : "true";
            var problems = new List<string>();
            RequireMetadata(metadata, "EdgeModuleRole", expectedRole, problems);
            RequireMetadata(metadata, "IsEdgePluginModule", expectedPlugin, problems);
            RequireMetadata(metadata, "IsPackable", expectedPackable, problems);
            if (_role == EdgeProjectRole.ConcretePlugin &&
                (!metadata.TryGetValue("PluginModuleId", out var moduleId) || string.IsNullOrWhiteSpace(moduleId)))
                problems.Add("PluginModuleId 缺失");

            if (problems.Count == 0)
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PluginRoleMetadata,
                Location.None,
                _assemblyName,
                string.Join("；", problems)));
        }

        private static void RequireMetadata(
            IReadOnlyDictionary<string, string> metadata,
            string key,
            string expected,
            ICollection<string> problems)
        {
            if (!metadata.TryGetValue(key, out var actual) ||
                !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{key} 应为 {expected}，实际为 {(actual ?? "<missing>")}");
            }
        }

        private void CaptureProductionTaskRoots(INamedTypeSymbol type)
        {
            if (_role != EdgeProjectRole.ConcretePlugin || !IsProductionTaskType(type))
                return;

            for (var current = type; current is not null &&
                 current.ContainingAssembly?.Name.Equals(_assemblyName, StringComparison.Ordinal) == true;
                 current = current.BaseType)
            {
                foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared)
                        _productionTaskRoots.TryAdd(NormalizeMethod(method)!, 0);
                }
            }
        }

        private static bool IsProductionTaskType(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (GetFullMetadataName(current.OriginalDefinition)
                    .Equals("IIoT.Edge.Module.Sdk.Base.PlcTaskBase", StringComparison.Ordinal))
                    return true;
            }

            return type.AllInterfaces.Any(static @interface =>
                GetFullMetadataName(@interface.OriginalDefinition)
                    .Equals("IIoT.Edge.Application.Abstractions.Plc.IPlcTask", StringComparison.Ordinal));
        }

        private void CaptureInterfaceAndOverrideDispatch(INamedTypeSymbol type)
        {
            foreach (var @interface in type.AllInterfaces)
            {
                foreach (var member in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation)
                        continue;

                    AddDispatchEdge(member, implementation, GetSourceLocation(type));
                }
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.OverriddenMethod is not null)
                    AddDispatchEdge(method.OverriddenMethod, method, GetSourceLocation(method));
            }
        }

        private void AddDispatchEdge(IMethodSymbol source, IMethodSymbol target, Location location)
        {
            var normalizedSource = NormalizeMethod(source);
            var normalizedTarget = NormalizeMethod(target);
            if (normalizedSource is null || normalizedTarget is null ||
                SymbolEqualityComparer.Default.Equals(normalizedSource, normalizedTarget))
                return;

            _callGraph.GetOrAdd(normalizedSource, static _ => new ConcurrentBag<InvocationEdge>())
                .Add(new InvocationEdge(
                    normalizedTarget,
                    location,
                    IsOutboundSink(target),
                    target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private void AnalyzeProductionTaskOutboundPaths(CompilationAnalysisContext context)
        {
            foreach (var root in _productionTaskRoots.Keys
                         .OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal))
            {
                if (!TryFindOutboundPath(root, out var firstEdge, out var sink))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    EdgeArchitectureDiagnostics.ProductionTaskOutbound,
                    firstEdge.Location,
                    root.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    firstEdge.Target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    sink.Display));
            }
        }

        private bool TryFindOutboundPath(
            IMethodSymbol root,
            out InvocationEdge firstEdge,
            out InvocationEdge sink)
        {
            firstEdge = null!;
            sink = null!;
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { root };
            var queue = new Queue<PathNode>();
            if (_callGraph.TryGetValue(root, out var rootEdges))
            {
                foreach (var edge in rootEdges.OrderBy(static item => item.Display, StringComparer.Ordinal))
                    queue.Enqueue(new PathNode(edge.Target, edge, edge));
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Edge.IsOutboundSink)
                {
                    firstEdge = current.FirstEdge;
                    sink = current.Edge;
                    return true;
                }

                if (!visited.Add(current.Method) || !_callGraph.TryGetValue(current.Method, out var edges))
                    continue;

                foreach (var edge in edges.OrderBy(static item => item.Display, StringComparer.Ordinal))
                    queue.Enqueue(new PathNode(edge.Target, current.FirstEdge, edge));
            }

            return false;
        }

        private static bool IsOutboundSink(IMethodSymbol method)
        {
            if (GetFullMetadataName(method.ContainingType.OriginalDefinition)
                .Equals("IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService", StringComparison.Ordinal) &&
                method.Name.Equals("EnqueueAsync", StringComparison.Ordinal))
                return false;

            if (IsForbiddenOutboundType(method.ContainingType))
                return true;

            return method.ContainingType.AllInterfaces.Any(IsForbiddenOutboundType);
        }

        private static bool IsForbiddenOutboundType(INamedTypeSymbol type)
        {
            var name = GetFullMetadataName(type.OriginalDefinition);
            if (name.Equals("System.Net.Http.HttpClient", StringComparison.Ordinal) ||
                name.Equals("IIoT.Edge.Application.Modules.MesRequestExecutor", StringComparison.Ordinal))
                return true;

            if (type.Name is "IProcessMesUploader" or
                "IProcessCloudUploader" or
                "IMesHttpClient" or
                "ICloudHttpClient" or
                "MesRequestExecutor")
                return true;

            var ns = GetNamespace(type);
            return ns.StartsWith("System.Net.Http", StringComparison.Ordinal) &&
                   (type.Name.IndexOf("HttpClient", StringComparison.Ordinal) >= 0 ||
                    type.Name.IndexOf("HttpContent", StringComparison.Ordinal) >= 0);
        }

        private void AnalyzeTransportOperation(OperationAnalysisContext context, INamedTypeSymbol type, Location location)
            => AnalyzeTransportOperation(context, (ITypeSymbol)type, location);

        private static INamedTypeSymbol? UnwrapNamedType(ITypeSymbol? type)
        {
            if (type is IArrayTypeSymbol array)
                return UnwrapNamedType(array.ElementType);
            return type as INamedTypeSymbol;
        }

        private static bool IsOrDerivesFromMetadataName(INamedTypeSymbol? type, string metadataName)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (GetFullMetadataName(current.OriginalDefinition).Equals(metadataName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static IMethodSymbol? NormalizeMethod(IMethodSymbol? method)
            => method?.ReducedFrom?.OriginalDefinition ?? method?.OriginalDefinition;

        private static bool IsSourceSymbol(ISymbol symbol)
            => symbol.Locations.Any(static location => location.IsInSource);

        private static Location GetSourceLocation(ISymbol symbol)
            => symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;

        private static string Display(ISymbol? symbol)
            => symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "<unknown>";

        private static string GetNamespace(ISymbol symbol)
            => symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        private static string GetFullMetadataName(INamedTypeSymbol? type)
        {
            if (type is null)
                return string.Empty;

            var parts = new Stack<string>();
            ISymbol? current = type;
            while (current is not null &&
                   !(current is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsGlobalNamespace))
            {
                parts.Push(current.MetadataName);
                current = current.ContainingSymbol;
            }
            return string.Join(".", parts);
        }

        private sealed class InvocationEdge
        {
            internal InvocationEdge(IMethodSymbol target, Location location, bool isOutboundSink, string display)
            {
                Target = target;
                Location = location;
                IsOutboundSink = isOutboundSink;
                Display = display;
            }

            internal IMethodSymbol Target { get; }
            internal Location Location { get; }
            internal bool IsOutboundSink { get; }
            internal string Display { get; }
        }

        private sealed class PathNode
        {
            internal PathNode(IMethodSymbol method, InvocationEdge firstEdge, InvocationEdge edge)
            {
                Method = method;
                FirstEdge = firstEdge;
                Edge = edge;
            }

            internal IMethodSymbol Method { get; }
            internal InvocationEdge FirstEdge { get; }
            internal InvocationEdge Edge { get; }
        }
    }
}
