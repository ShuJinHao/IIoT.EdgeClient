using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
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
            EdgeArchitectureDiagnostics.PresentationMediatRUseCase,
            EdgeArchitectureDiagnostics.DirectVisibleValidationText,
            EdgeArchitectureDiagnostics.PluginForbiddenReference,
            EdgeArchitectureDiagnostics.PluginCrossReference,
            EdgeArchitectureDiagnostics.HostPluginReference,
            EdgeArchitectureDiagnostics.PluginRoleMetadata,
            EdgeArchitectureDiagnostics.PluginChannelRegistration,
            EdgeArchitectureDiagnostics.ProductionTaskOutbound,
            EdgeArchitectureDiagnostics.ProductionTaskEnqueueGuard,
            EdgeArchitectureDiagnostics.RemovedCompatibilityContract,
            EdgeArchitectureDiagnostics.CloudRouteLiteral,
            EdgeArchitectureDiagnostics.PlcTransportOwner,
            EdgeArchitectureDiagnostics.SyncOverAsync,
            EdgeArchitectureDiagnostics.AsyncVoid);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
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
        context.RegisterOperationAction(state.AnalyzeSimpleAssignment, OperationKind.SimpleAssignment);
        context.RegisterOperationAction(state.AnalyzeCompoundAssignment, OperationKind.CompoundAssignment);
        context.RegisterOperationAction(state.AnalyzeLiteral, OperationKind.Literal);
        context.RegisterOperationAction(state.AnalyzeBinary, OperationKind.Binary);
        context.RegisterOperationAction(state.AnalyzeInterpolatedString, OperationKind.InterpolatedString);
        context.RegisterOperationAction(state.AnalyzeEventAssignment, OperationKind.EventAssignment);
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
        private readonly ConcurrentDictionary<ISymbol, ConcurrentBag<DelegateValue>> _delegateAssignments =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _asyncVoidCandidates =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IMethodSymbol, byte> _registeredEventHandlers =
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

            AnalyzeRemovedCompatibilityType(context, type, type);
            AnalyzePluginCloudUploader(context, type);

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
            CaptureAsyncVoidCandidate(method);
        }

        internal void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var caller = ResolveCaller(context.ContainingSymbol);
            var target = NormalizeMethod(invocation.TargetMethod);
            if (caller is not null && target is not null && IsDelegateInvoke(invocation.TargetMethod))
            {
                AddDelegateInvocationEdge(
                    caller,
                    target,
                    CreateDelegateValue(invocation.Instance),
                    invocation.Syntax.GetLocation(),
                    IsProtectedByExceptionCatch(invocation));
            }
            else if (caller is not null && target is not null)
            {
                AddCallEdge(
                    caller,
                    target,
                    invocation,
                    CreateDelegateArgumentBindings(
                        invocation.TargetMethod,
                        target,
                        invocation.Arguments));
                CaptureApprovedExternalDelegateCallbacks(
                    caller,
                    target,
                    invocation.Arguments,
                    invocation.Syntax.GetLocation(),
                    IsProtectedByExceptionCatch(invocation));
            }

            AnalyzeRepositoryOperation(context, invocation);
            AnalyzeConstructedMethodTypeArguments(context, invocation);
            AnalyzeRoleUse(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            AnalyzePresentationMediatROperation(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            AnalyzeRemovedCompatibilityOperation(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            AnalyzePluginModuleRegistration(context, invocation);
            if (AnalyzeSyncWait(context, invocation))
                return;

            if (AnalyzeProviderCommit(context, invocation))
                return;

            if (AnalyzeDapperWrite(context, invocation))
                return;

            AnalyzeDatabaseOperation(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            AnalyzeTransportOperation(context, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            AnalyzeCloudRouteOperation(context, invocation);
        }

        internal void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            var caller = ResolveCaller(context.ContainingSymbol);
            var constructor = NormalizeMethod(creation.Constructor);
            if (caller is not null && constructor is not null && creation.Constructor is not null)
            {
                AddCallEdge(
                    caller,
                    constructor,
                    creation,
                    CreateDelegateArgumentBindings(
                        creation.Constructor,
                        constructor,
                        creation.Arguments));
                CaptureApprovedExternalDelegateCallbacks(
                    caller,
                    constructor,
                    creation.Arguments,
                    creation.Syntax.GetLocation(),
                    IsProtectedByExceptionCatch(creation));
            }
            AnalyzeRepositoryOperation(context, creation);
            AnalyzeRoleUse(context, creation.Type, creation.Syntax.GetLocation());
            AnalyzePresentationMediatROperation(context, creation.Type, creation.Syntax.GetLocation());
            AnalyzeDirectVisibleValidationText(context, creation);
            AnalyzeRemovedCompatibilityOperation(context, creation.Type, creation.Syntax.GetLocation());
            AnalyzeDatabaseOperation(context, creation.Type, creation.Syntax.GetLocation());
            AnalyzeTransportOperation(context, creation.Type, creation.Syntax.GetLocation());
        }

        internal void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var property = (IPropertyReferenceOperation)context.Operation;
            var caller = ResolveCaller(context.ContainingSymbol);
            if (IsPropertyWrite(property))
                AddCallEdge(caller, NormalizeMethod(property.Property.SetMethod), property);
            if (!IsPropertyWrite(property) || IsPropertyReadWrite(property))
                AddCallEdge(caller, NormalizeMethod(property.Property.GetMethod), property);

            AnalyzeRoleUse(context, property.Property.ContainingType, property.Syntax.GetLocation());
            AnalyzePresentationMediatROperation(context, property.Property.ContainingType, property.Syntax.GetLocation());
            AnalyzeRemovedCompatibilityOperation(context, property.Property.ContainingType, property.Syntax.GetLocation());
            AnalyzeRepositoryOperation(context, property);

            if (property.Property.Name.Equals("Result", StringComparison.Ordinal) &&
                IsTaskLikeType(property.Property.ContainingType))
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
            CaptureDelegateAssignment(variable.Symbol, variable.Initializer?.Value);
            if (variable.Symbol is ILocalSymbol local)
            {
                AnalyzeRoleUse(context, local.Type, variable.Syntax.GetLocation());
                AnalyzeRemovedCompatibilityOperation(context, local.Type, variable.Syntax.GetLocation());
                AnalyzeDatabaseOperation(context, local.Type, variable.Syntax.GetLocation());
                AnalyzeTransportOperation(context, local.Type, variable.Syntax.GetLocation());
            }
        }

        internal void AnalyzeSimpleAssignment(OperationAnalysisContext context)
        {
            var assignment = (ISimpleAssignmentOperation)context.Operation;
            var symbol = assignment.Target switch
            {
                ILocalReferenceOperation local => (ISymbol)local.Local,
                IFieldReferenceOperation field => field.Field,
                IPropertyReferenceOperation property => property.Property,
                IParameterReferenceOperation parameter => parameter.Parameter.OriginalDefinition,
                _ => null
            };
            if (symbol is not null)
                CaptureDelegateAssignment(symbol, assignment.Value);
        }

        internal void AnalyzeCompoundAssignment(OperationAnalysisContext context)
        {
            var assignment = (ICompoundAssignmentOperation)context.Operation;
            var symbol = assignment.Target switch
            {
                ILocalReferenceOperation local => (ISymbol)local.Local,
                IFieldReferenceOperation field => field.Field,
                IPropertyReferenceOperation property => property.Property,
                IParameterReferenceOperation parameter => parameter.Parameter.OriginalDefinition,
                _ => null
            };
            if (symbol is not null)
                CaptureDelegateAssignment(symbol, assignment.Value);
        }

        internal void AnalyzeLiteral(OperationAnalysisContext context)
        {
            if (context.Operation is ILiteralOperation literal &&
                literal.ConstantValue.HasValue &&
                literal.ConstantValue.Value is string value)
            {
                AnalyzeCloudRouteConstant(context, value, literal.Syntax.GetLocation());
            }
        }

        internal void AnalyzeBinary(OperationAnalysisContext context)
        {
            if (context.Operation is not IBinaryOperation binary ||
                !binary.ConstantValue.HasValue ||
                binary.ConstantValue.Value is not string value ||
                (binary.LeftOperand.ConstantValue.HasValue &&
                 binary.LeftOperand.ConstantValue.Value is string left &&
                 left.Contains("/api/v1/", StringComparison.OrdinalIgnoreCase)) ||
                (binary.RightOperand.ConstantValue.HasValue &&
                 binary.RightOperand.ConstantValue.Value is string right &&
                 right.Contains("/api/v1/", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            AnalyzeCloudRouteConstant(context, value, binary.Syntax.GetLocation());
        }

        internal void AnalyzeInterpolatedString(OperationAnalysisContext context)
        {
            if (TryEvaluateString(context.Operation, out var value) &&
                !HasChildCloudRouteConstant(context.Operation))
                AnalyzeCloudRouteConstant(context, value, context.Operation.Syntax.GetLocation());
        }

        internal void AnalyzeEventAssignment(OperationAnalysisContext context)
        {
            var assignment = (IEventAssignmentOperation)context.Operation;
            foreach (var target in GetDelegateTargets(assignment.HandlerValue))
                _registeredEventHandlers.TryAdd(target, 0);
        }

        internal void AnalyzeCompilationEnd(CompilationAnalysisContext context)
        {
            AnalyzeProductionTestReferences(context);
            AnalyzePluginRoleMetadata(context);
            AnalyzeProductionTaskOutboundPaths(context);
            AnalyzeProductionTaskEnqueueGuards(context);
            AnalyzeAsyncVoidCandidates(context);
        }

        private void CaptureDelegateAssignment(ISymbol source, IOperation? value)
        {
            var sourceType = source switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null
            };
            if (value is null || sourceType?.TypeKind != TypeKind.Delegate)
                return;

            _delegateAssignments.GetOrAdd(source, static _ => new ConcurrentBag<DelegateValue>())
                .Add(CreateDelegateValue(value));
        }

        private static ImmutableArray<IMethodSymbol> GetDelegateTargets(IOperation? operation)
            => CreateDelegateValue(operation).Targets;

        private static DelegateValue CreateDelegateValue(IOperation? operation)
        {
            var targets = ImmutableArray.CreateBuilder<IMethodSymbol>();
            var sources = ImmutableArray.CreateBuilder<ISymbol>();
            var hasUnknownTarget = false;
            CollectDelegateValue(operation, targets, sources, ref hasUnknownTarget);

            var normalized = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var target in targets)
            {
                if (NormalizeMethod(target) is { } method)
                    normalized.Add(method);
            }

            var distinctSources = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var source in sources)
                distinctSources.Add(source);

            return new DelegateValue(
                normalized.ToImmutableArray(),
                distinctSources.ToImmutableArray(),
                hasUnknownTarget);
        }

        private static void CollectDelegateValue(
            IOperation? operation,
            ImmutableArray<IMethodSymbol>.Builder targets,
            ImmutableArray<ISymbol>.Builder sources,
            ref bool hasUnknownTarget)
        {
            switch (operation)
            {
                case null:
                    hasUnknownTarget = true;
                    return;
                case IDelegateCreationOperation creation:
                    CollectDelegateValue(creation.Target, targets, sources, ref hasUnknownTarget);
                    return;
                case IMethodReferenceOperation methodReference:
                    targets.Add(methodReference.Method);
                    return;
                case IAnonymousFunctionOperation anonymousFunction:
                    targets.Add(anonymousFunction.Symbol);
                    return;
                case IConversionOperation conversion:
                    CollectDelegateValue(conversion.Operand, targets, sources, ref hasUnknownTarget);
                    return;
                case IParenthesizedOperation parenthesized:
                    CollectDelegateValue(parenthesized.Operand, targets, sources, ref hasUnknownTarget);
                    return;
                case IConditionalOperation conditional:
                    CollectDelegateValue(conditional.WhenTrue, targets, sources, ref hasUnknownTarget);
                    CollectDelegateValue(conditional.WhenFalse, targets, sources, ref hasUnknownTarget);
                    return;
                case ILocalReferenceOperation local:
                    sources.Add(local.Local);
                    return;
                case IFieldReferenceOperation field:
                    sources.Add(field.Field);
                    return;
                case IPropertyReferenceOperation property:
                    sources.Add(property.Property);
                    return;
                case IParameterReferenceOperation parameter:
                    sources.Add(parameter.Parameter.OriginalDefinition);
                    return;
                default:
                    hasUnknownTarget = true;
                    return;
            }
        }

        private static ImmutableArray<DelegateArgumentBinding> CreateDelegateArgumentBindings(
            IMethodSymbol invokedMethod,
            IMethodSymbol normalizedTarget,
            ImmutableArray<IArgumentOperation> arguments)
        {
            var bindings = ImmutableArray.CreateBuilder<DelegateArgumentBinding>();
            foreach (var argument in arguments)
            {
                if (argument.Parameter is not IParameterSymbol parameter ||
                    parameter.Type.TypeKind != TypeKind.Delegate)
                    continue;

                var ordinal = parameter.Ordinal + (invokedMethod.ReducedFrom is null ? 0 : 1);
                var normalizedParameter = ordinal >= 0 && ordinal < normalizedTarget.Parameters.Length
                    ? normalizedTarget.Parameters[ordinal].OriginalDefinition
                    : parameter.OriginalDefinition;
                bindings.Add(new DelegateArgumentBinding(
                    normalizedParameter,
                    CreateDelegateValue(argument.Value)));
            }

            return bindings.ToImmutable();
        }

        private void CaptureApprovedExternalDelegateCallbacks(
            IMethodSymbol caller,
            IMethodSymbol target,
            ImmutableArray<IArgumentOperation> arguments,
            Location location,
            bool isExceptionHandled)
        {
            if (!IsApprovedExternalProductionTaskBoundary(target))
                return;

            foreach (var argument in arguments)
            {
                if (argument.Parameter?.Type is not INamedTypeSymbol delegateType ||
                    delegateType.TypeKind != TypeKind.Delegate ||
                    delegateType.DelegateInvokeMethod is not { } invokeMethod)
                {
                    continue;
                }

                AddDelegateInvocationEdge(
                    caller,
                    NormalizeMethod(invokeMethod)!,
                    CreateDelegateValue(argument.Value),
                    argument.Syntax.GetLocation(),
                    isExceptionHandled);
            }
        }

        private static bool IsDelegateInvoke(IMethodSymbol method)
            => method.MethodKind == MethodKind.DelegateInvoke;

        private IMethodSymbol? ResolveCaller(ISymbol? containingSymbol)
        {
            if (containingSymbol is IMethodSymbol method)
                return NormalizeMethod(method);

            if (containingSymbol is not IFieldSymbol and not IPropertySymbol)
                return null;

            var containingType = containingSymbol.ContainingType;
            if (containingType is null)
                return null;

            var constructor = containingSymbol.IsStatic
                ? containingType.GetMembers().OfType<IMethodSymbol>()
                    .FirstOrDefault(static candidate => candidate.MethodKind == MethodKind.StaticConstructor)
                : containingType.InstanceConstructors.FirstOrDefault();
            return NormalizeMethod(constructor);
        }

        private void AddDelegateInvocationEdge(
            IMethodSymbol caller,
            IMethodSymbol delegateInvokeTarget,
            DelegateValue delegateValue,
            Location location,
            bool isExceptionHandled)
        {
            _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>())
                .Add(new InvocationEdge(
                    delegateInvokeTarget,
                    location,
                    isOutboundSink: false,
                    isDataPipelineEnqueue: false,
                    isExceptionHandled,
                    delegateInvokeTarget.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    delegateInvocation: delegateValue));
        }

        private void AddCallEdge(
            IMethodSymbol? caller,
            IMethodSymbol? target,
            IOperation operation,
            ImmutableArray<DelegateArgumentBinding> delegateArguments = default)
        {
            if (caller is null || target is null ||
                SymbolEqualityComparer.Default.Equals(caller, target))
            {
                return;
            }

            _callGraph.GetOrAdd(caller, static _ => new ConcurrentBag<InvocationEdge>())
                .Add(CreateInvocationEdge(target, operation, delegateArguments));
        }

        private InvocationEdge CreateInvocationEdge(
            IMethodSymbol target,
            IOperation operation,
            ImmutableArray<DelegateArgumentBinding> delegateArguments = default)
        {
            var unverifiedExternalBoundary = IsUnverifiedExternalProductionTaskBoundary(target);
            return new InvocationEdge(
                target,
                operation.Syntax.GetLocation(),
                IsOutboundSink(target) || unverifiedExternalBoundary,
                IsDataPipelineEnqueue(target) || unverifiedExternalBoundary,
                IsProtectedByExceptionCatch(operation),
                unverifiedExternalBoundary
                    ? $"unverified external boundary {target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}"
                    : target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                delegateArguments);
        }

        private static bool IsPropertyWrite(IPropertyReferenceOperation property)
            => property.Parent is ISimpleAssignmentOperation simple &&
                   ReferenceEquals(simple.Target, property) ||
               property.Parent is ICompoundAssignmentOperation compound &&
                   ReferenceEquals(compound.Target, property) ||
               property.Parent is IIncrementOrDecrementOperation increment &&
                   ReferenceEquals(increment.Target, property);

        private static bool IsPropertyReadWrite(IPropertyReferenceOperation property)
            => property.Parent is ICompoundAssignmentOperation compound &&
                   ReferenceEquals(compound.Target, property) ||
               property.Parent is IIncrementOrDecrementOperation increment &&
                   ReferenceEquals(increment.Target, property);

        private void AnalyzeTypeUse(SymbolAnalysisContext context, ISymbol owner, ITypeSymbol? type)
        {
            if (type is null)
                return;

            var location = GetSourceLocation(owner);
            AnalyzeRepositoryType(context, owner, type, location);
            AnalyzeRoleUse(context, type, location);
            AnalyzePresentationMediatRUse(context, owner, type, location);
            AnalyzeRemovedCompatibilityType(context, owner, type);
            AnalyzeDatabaseType(context, owner, type, location);
            AnalyzeTransportType(context, owner, type, location);
        }

        private void AnalyzeConstructedMethodTypeArguments(
            OperationAnalysisContext context,
            IInvocationOperation invocation)
        {
            foreach (var typeArgument in invocation.TargetMethod.TypeArguments)
            {
                if (ContainsType(invocation.Type, typeArgument))
                    continue;

                var location = invocation.Syntax.GetLocation();
                if (TryFindRepositoryEntity(typeArgument, out var entity))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        EdgeArchitectureDiagnostics.RepositoryRoot,
                        location,
                        Display(context.ContainingSymbol),
                        Display(entity),
                        EdgeArchitectureRegistry.ApprovedRootSummary));
                }

                AnalyzeRoleUse(context, typeArgument, location);
                AnalyzePresentationMediatROperation(context, typeArgument, location);
                AnalyzeRemovedCompatibilityOperation(context, typeArgument, location);
                AnalyzeDatabaseOperation(context, typeArgument, location);
                AnalyzeTransportOperation(context, typeArgument, location);
            }
        }

        private static bool ContainsType(ITypeSymbol? container, ITypeSymbol candidate)
        {
            if (container is null)
                return false;
            if (SymbolEqualityComparer.Default.Equals(container, candidate))
                return true;
            if (container is IArrayTypeSymbol array)
                return ContainsType(array.ElementType, candidate);
            if (container is IPointerTypeSymbol pointer)
                return ContainsType(pointer.PointedAtType, candidate);
            if (container is IFunctionPointerTypeSymbol functionPointer)
            {
                return ContainsType(functionPointer.Signature.ReturnType, candidate) ||
                    functionPointer.Signature.Parameters.Any(parameter =>
                        ContainsType(parameter.Type, candidate));
            }
            if (container is not INamedTypeSymbol named)
                return false;

            return named.TypeArguments.Any(argument => ContainsType(argument, candidate));
        }

        private void AnalyzeRemovedCompatibilityType(
            SymbolAnalysisContext context,
            ISymbol owner,
            ITypeSymbol? type)
        {
            if (!TryFindRemovedCompatibilityType(type, out var removedType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.RemovedCompatibilityContract,
                GetSourceLocation(owner),
                Display(owner),
                Display(removedType)));
        }

        private void AnalyzeRemovedCompatibilityOperation(
            OperationAnalysisContext context,
            ITypeSymbol? type,
            Location location)
        {
            if (!TryFindRemovedCompatibilityType(type, out var removedType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.RemovedCompatibilityContract,
                location,
                Display(context.ContainingSymbol),
                Display(removedType)));
        }

        private static bool TryFindRemovedCompatibilityType(
            ITypeSymbol? type,
            out ITypeSymbol removedType)
        {
            removedType = type!;
            if (type is null)
                return false;
            if (type is IArrayTypeSymbol array)
                return TryFindRemovedCompatibilityType(array.ElementType, out removedType);
            if (type is not INamedTypeSymbol named)
                return false;

            var metadataName = GetFullMetadataName(named.OriginalDefinition);
            if (metadataName.Equals(
                    "IIoT.Edge.Application.Modules.ProcessCloudUploaderBase`2",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Application.Modules.ProcessMesUploaderBase`1",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Application.Modules.Mes.MesUploadChannelBase`1",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Application.Abstractions.Plc.ISignalInteraction",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Infrastructure.DeviceComm.Signals.SignalInteraction",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Application.Abstractions.Plc.Signals.ILogicalSignalAccessor",
                    StringComparison.Ordinal))
            {
                removedType = named;
                return true;
            }

            foreach (var argument in named.TypeArguments)
            {
                if (TryFindRemovedCompatibilityType(argument, out removedType))
                    return true;
            }

            return false;
        }

        private void AnalyzePluginCloudUploader(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            if (_role != EdgeProjectRole.ConcretePlugin ||
                type.TypeKind != TypeKind.Class ||
                !type.AllInterfaces.Any(static @interface =>
                    GetFullMetadataName(@interface.OriginalDefinition).Equals(
                        "IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader",
                        StringComparison.Ordinal)) ||
                IsOrDerivesFromMetadataName(type, "IIoT.Edge.Application.Modules.Cloud.CloudUploadChannelBase`2"))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PluginChannelRegistration,
                GetSourceLocation(type),
                Display(type),
                "CloudUploadChannelBase<TCellData, TPayload>"));
        }

        private void AnalyzePluginModuleRegistration(
            OperationAnalysisContext context,
            IInvocationOperation invocation)
        {
            if (_role != EdgeProjectRole.ConcretePlugin ||
                !IsServiceCollectionMutation(invocation))
            {
                return;
            }

            var foundForbiddenContract =
                TryFindForbiddenDirectModuleRegistrationType(invocation, out var contractType);
            if (!foundForbiddenContract)
            {
                var hasOpaqueDescriptor = invocation.Arguments.Any(argument =>
                    IsServiceDescriptorType(argument.Value.Type));
                if (!hasOpaqueDescriptor)
                    return;

                contractType = invocation.Arguments
                    .Select(argument => argument.Value.Type)
                    .First(type => IsServiceDescriptorType(type))!;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PluginChannelRegistration,
                invocation.Syntax.GetLocation(),
                Display(context.ContainingSymbol),
                Display(contractType)));
        }

        private static bool IsServiceCollectionMutation(IInvocationOperation invocation)
        {
            var methodName = invocation.TargetMethod.Name;
            if (!methodName.StartsWith("Add", StringComparison.Ordinal) &&
                !methodName.StartsWith("TryAdd", StringComparison.Ordinal) &&
                !methodName.Equals("Insert", StringComparison.Ordinal) &&
                !methodName.Equals("Replace", StringComparison.Ordinal))
            {
                return false;
            }

            if (GetNamespace(invocation.TargetMethod.ContainingType)
                .StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
            {
                return true;
            }

            if (IsServiceCollectionType(invocation.Instance?.Type))
                return true;

            return invocation.Arguments.Any(argument =>
                IsServiceCollectionType(argument.Value.Type));
        }

        private static bool IsServiceCollectionType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol named)
                return false;

            return GetFullMetadataName(named.OriginalDefinition)
                       .Equals(
                           "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                           StringComparison.Ordinal) ||
                   named.AllInterfaces.Any(@interface =>
                       GetFullMetadataName(@interface.OriginalDefinition)
                           .Equals(
                               "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                               StringComparison.Ordinal));
        }

        private static bool IsServiceDescriptorType(ITypeSymbol? type)
            => type is INamedTypeSymbol named &&
               GetFullMetadataName(named.OriginalDefinition)
                   .Equals(
                       "Microsoft.Extensions.DependencyInjection.ServiceDescriptor",
                       StringComparison.Ordinal);

        private static bool TryFindForbiddenDirectModuleRegistrationType(
            IInvocationOperation invocation,
            out ITypeSymbol contractType)
        {
            foreach (var typeArgument in invocation.TargetMethod.TypeArguments)
            {
                if (TryFindForbiddenModuleContract(typeArgument, out contractType))
                    return true;
            }

            var stack = new Stack<IOperation>();
            foreach (var argument in invocation.Arguments)
                stack.Push(argument.Value);
            while (stack.Count > 0)
            {
                var operation = stack.Pop();
                if (TryFindForbiddenModuleContract(operation.Type, out contractType))
                    return true;
                if (operation is ITypeOfOperation typeOfOperation &&
                    TryFindForbiddenModuleContract(typeOfOperation.TypeOperand, out contractType))
                {
                    return true;
                }
                if (operation is IInvocationOperation nestedInvocation)
                {
                    foreach (var typeArgument in nestedInvocation.TargetMethod.TypeArguments)
                    {
                        if (TryFindForbiddenModuleContract(typeArgument, out contractType))
                            return true;
                    }
                }
                if (operation is IObjectCreationOperation objectCreation)
                {
                    foreach (var typeArgument in objectCreation.Constructor?.TypeArguments ??
                             ImmutableArray<ITypeSymbol>.Empty)
                    {
                        if (TryFindForbiddenModuleContract(typeArgument, out contractType))
                            return true;
                    }
                }

                foreach (var child in operation.ChildOperations)
                    stack.Push(child);
            }

            contractType = null!;
            return false;
        }

        private static bool TryFindForbiddenModuleContract(
            ITypeSymbol? type,
            out ITypeSymbol contractType)
        {
            contractType = type!;
            if (type is not INamedTypeSymbol named)
                return false;

            var metadataName = GetFullMetadataName(named.OriginalDefinition);
            if (metadataName.Equals(
                    "IIoT.Edge.Application.Abstractions.Modules.IModulePlcSignalProfile`1",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Application.Abstractions.Modules.IModuleHardwareProfileProvider",
                    StringComparison.Ordinal) ||
                metadataName.Equals(
                    "IIoT.Edge.Application.Abstractions.Modules.IDevelopmentSampleContributor",
                    StringComparison.Ordinal))
            {
                contractType = named;
                return true;
            }

            foreach (var argument in named.TypeArguments)
            {
                if (TryFindForbiddenModuleContract(argument, out contractType))
                    return true;
            }

            return false;
        }

        private static bool IsProtectedByExceptionCatch(IOperation operation)
        {
            for (var current = operation.Parent; current is not null; current = current.Parent)
            {
                if (current is not ITryOperation tryOperation ||
                    !tryOperation.Body.Syntax.Span.Contains(operation.Syntax.Span))
                {
                    continue;
                }

                if (IsTaskLikeType(operation.Type) &&
                    !IsAwaitedInsideTry(operation, tryOperation))
                {
                    continue;
                }

                if (tryOperation.Catches.Any(IsGenuineGeneralExceptionHandler))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAwaitedInsideTry(IOperation operation, ITryOperation tryOperation)
        {
            for (var current = operation.Parent;
                 current is not null && !ReferenceEquals(current, tryOperation);
                 current = current.Parent)
            {
                if (current is IAwaitOperation awaitOperation &&
                    tryOperation.Body.Syntax.Span.Contains(awaitOperation.Syntax.Span))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGenuineGeneralExceptionHandler(ICatchClauseOperation clause)
        {
            var catchesGeneralException = clause.ExceptionType is null ||
                (clause.ExceptionType is INamedTypeSymbol exceptionType &&
                 GetFullMetadataName(exceptionType).Equals("System.Exception", StringComparison.Ordinal));
            if (!catchesGeneralException)
                return false;

            if (clause.Filter is not null &&
                (!clause.Filter.ConstantValue.HasValue ||
                 clause.Filter.ConstantValue.Value is not true))
            {
                return false;
            }

            var stack = new Stack<IOperation>();
            stack.Push(clause.Handler);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current is IThrowOperation || IsExceptionDispatchRethrow(current))
                    return false;

                foreach (var child in current.ChildOperations)
                    stack.Push(child);
            }

            return true;
        }

        private static bool IsExceptionDispatchRethrow(IOperation operation)
            => operation is IInvocationOperation invocation &&
               invocation.TargetMethod.Name.Equals("Throw", StringComparison.Ordinal) &&
               GetFullMetadataName(invocation.TargetMethod.ContainingType.OriginalDefinition)
                   .Equals(
                       "System.Runtime.ExceptionServices.ExceptionDispatchInfo",
                       StringComparison.Ordinal);

        private void AnalyzeCloudRouteConstant(
            OperationAnalysisContext context,
            string value,
            Location location)
        {
            if (_role is EdgeProjectRole.Test or EdgeProjectRole.TestFixture or EdgeProjectRole.Analyzer ||
                value.IndexOf("/api/v1/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.CloudRouteLiteral,
                location,
                Display(context.ContainingSymbol),
                value));
        }

        private void AnalyzeCloudRouteOperation(
            OperationAnalysisContext context,
            IInvocationOperation invocation)
        {
            if (!TryEvaluateString(invocation, out var value) ||
                HasChildCloudRouteConstant(invocation))
            {
                return;
            }

            AnalyzeCloudRouteConstant(context, value, invocation.Syntax.GetLocation());
        }

        private static bool HasChildCloudRouteConstant(IOperation operation)
        {
            foreach (var child in operation.ChildOperations)
            {
                if (child.ConstantValue.HasValue &&
                    child.ConstantValue.Value is string value &&
                    value.IndexOf("/api/v1/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (HasChildCloudRouteConstant(child))
                    return true;
            }

            return false;
        }

        private static bool TryEvaluateString(IOperation? operation, out string value)
        {
            value = string.Empty;
            if (operation is null)
                return false;
            if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string constant)
            {
                value = constant;
                return true;
            }

            switch (operation)
            {
                case IConversionOperation conversion:
                    return TryEvaluateString(conversion.Operand, out value);
                case IParenthesizedOperation parenthesized:
                    return TryEvaluateString(parenthesized.Operand, out value);
                case IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add:
                    if (TryEvaluateString(binary.LeftOperand, out var left) &&
                        TryEvaluateString(binary.RightOperand, out var right))
                    {
                        value = left + right;
                        return true;
                    }
                    return false;
                case IInterpolatedStringOperation interpolated:
                {
                    var builder = new System.Text.StringBuilder();
                    foreach (var part in interpolated.Parts)
                    {
                        if (part is IInterpolatedStringTextOperation text &&
                            text.Text.ConstantValue.HasValue &&
                            text.Text.ConstantValue.Value is string textValue)
                        {
                            builder.Append(textValue);
                        }
                        else if (part is IInterpolationOperation interpolation &&
                                 interpolation.Expression.ConstantValue.HasValue)
                        {
                            builder.Append(Convert.ToString(
                                interpolation.Expression.ConstantValue.Value,
                                CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append('\uFFFD');
                        }
                    }

                    value = builder.ToString();
                    return true;
                }
                case IInvocationOperation invocation
                    when invocation.TargetMethod.Name.Equals("Format", StringComparison.Ordinal) &&
                         GetFullMetadataName(invocation.TargetMethod.ContainingType.OriginalDefinition)
                             .Equals("System.String", StringComparison.Ordinal):
                    return TryEvaluateStringFormat(invocation, out value);
                default:
                    return false;
            }
        }

        private static bool TryEvaluateStringFormat(
            IInvocationOperation invocation,
            out string value)
        {
            value = string.Empty;
            var formatArgument = invocation.Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Name.Equals("format", StringComparison.Ordinal) == true);
            if (formatArgument is null ||
                !TryEvaluateString(formatArgument.Value, out var format))
            {
                return false;
            }

            var values = new List<object?>();
            foreach (var argument in invocation.Arguments)
            {
                if (ReferenceEquals(argument, formatArgument) ||
                    argument.Parameter?.Type is INamedTypeSymbol parameterType &&
                    GetFullMetadataName(parameterType.OriginalDefinition)
                        .Equals("System.IFormatProvider", StringComparison.Ordinal))
                {
                    continue;
                }

                if (argument.ArgumentKind == ArgumentKind.ParamArray &&
                    argument.Value is IArrayCreationOperation arrayCreation &&
                    arrayCreation.Initializer is not null)
                {
                    foreach (var element in arrayCreation.Initializer.ElementValues)
                        values.Add(GetStringFormatValue(element));
                    continue;
                }

                values.Add(GetStringFormatValue(argument.Value));
            }

            try
            {
                value = string.Format(CultureInfo.InvariantCulture, format, values.ToArray());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static object? GetStringFormatValue(IOperation operation)
        {
            if (operation.ConstantValue.HasValue)
                return operation.ConstantValue.Value;
            if (TryEvaluateString(operation, out var value))
                return value;
            return "\uFFFD";
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
            foreach (var named in EnumerateNamedTypes(referencedType))
            {
                var diagnostic = CreateRoleDiagnostic(named, location, owner);
                if (diagnostic is not null)
                    return diagnostic;
            }

            return null;
        }

        private Diagnostic? CreateRoleDiagnostic(INamedTypeSymbol named, Location location, ISymbol owner)
        {

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

        private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol? type)
        {
            if (type is null)
                yield break;
            if (type is IArrayTypeSymbol array)
            {
                foreach (var nested in EnumerateNamedTypes(array.ElementType))
                    yield return nested;
                yield break;
            }
            if (type is IPointerTypeSymbol pointer)
            {
                foreach (var nested in EnumerateNamedTypes(pointer.PointedAtType))
                    yield return nested;
                yield break;
            }
            if (type is IFunctionPointerTypeSymbol functionPointer)
            {
                foreach (var nested in EnumerateNamedTypes(functionPointer.Signature.ReturnType))
                    yield return nested;
                foreach (var parameter in functionPointer.Signature.Parameters)
                {
                    foreach (var nested in EnumerateNamedTypes(parameter.Type))
                        yield return nested;
                }
                yield break;
            }
            if (type is not INamedTypeSymbol named)
                yield break;

            yield return named;
            foreach (var argument in named.TypeArguments)
            {
                foreach (var nested in EnumerateNamedTypes(argument))
                    yield return nested;
            }
        }

        private void AnalyzePresentationMediatRUse(
            SymbolAnalysisContext context,
            ISymbol owner,
            ITypeSymbol? referencedType,
            Location location)
        {
            if (!TryFindForbiddenPresentationMediatRType(referencedType, out var forbiddenType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PresentationMediatRUseCase,
                location,
                Display(owner),
                Display(forbiddenType)));
        }

        private void AnalyzePresentationMediatROperation(
            OperationAnalysisContext context,
            ITypeSymbol? referencedType,
            Location location)
        {
            if (!TryFindForbiddenPresentationMediatRType(referencedType, out var forbiddenType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.PresentationMediatRUseCase,
                location,
                Display(context.ContainingSymbol),
                Display(forbiddenType)));
        }

        private bool TryFindForbiddenPresentationMediatRType(
            ITypeSymbol? type,
            out INamedTypeSymbol forbiddenType)
        {
            forbiddenType = null!;
            if (_role != EdgeProjectRole.Presentation)
                return false;

            foreach (var named in EnumerateNamedTypes(type))
            {
                var metadataName = GetFullMetadataName(named.OriginalDefinition);
                if (metadataName is "MediatR.IRequest" or
                    "MediatR.IRequest`1" or
                    "MediatR.IRequestHandler`1" or
                    "MediatR.IRequestHandler`2" or
                    "MediatR.ISender")
                {
                    forbiddenType = named;
                    return true;
                }
            }

            return false;
        }

        private void AnalyzeDirectVisibleValidationText(
            OperationAnalysisContext context,
            IObjectCreationOperation creation)
        {
            if (_role != EdgeProjectRole.Presentation ||
                GetFullMetadataName(creation.Type as INamedTypeSymbol).Equals(
                    "IIoT.Edge.Application.Common.Crud.ValidationIssue",
                    StringComparison.Ordinal) is false)
            {
                return;
            }

            var message = creation.Arguments
                .Select(argument => argument.Value.ConstantValue)
                .FirstOrDefault(value => value.HasValue && value.Value is string);
            if (!message.HasValue || message.Value is not string text ||
                !text.Any(character => character is >= '\u4e00' and <= '\u9fff'))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                EdgeArchitectureDiagnostics.DirectVisibleValidationText,
                creation.Syntax.GetLocation(),
                Display(context.ContainingSymbol),
                text));
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
            var isWait = (method.Name.Equals("Wait", StringComparison.Ordinal) ||
                          method.Name.Equals("WaitAll", StringComparison.Ordinal) ||
                          method.Name.Equals("WaitAny", StringComparison.Ordinal)) &&
                         IsTaskType(method.ContainingType);
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

        private static bool IsTaskLikeType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol named)
                return false;

            var name = GetFullMetadataName(named.OriginalDefinition);
            return name.Equals("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                   name.Equals("System.Threading.Tasks.Task`1", StringComparison.Ordinal) ||
                   name.Equals("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) ||
                   name.Equals("System.Threading.Tasks.ValueTask`1", StringComparison.Ordinal);
        }

        private void CaptureAsyncVoidCandidate(IMethodSymbol method)
        {
            if (!method.IsAsync || !method.ReturnsVoid || method.MethodKind != MethodKind.Ordinary)
                return;

            if (NormalizeMethod(method) is { } normalized)
                _asyncVoidCandidates.TryAdd(normalized, 0);
        }

        private void AnalyzeAsyncVoidCandidates(CompilationAnalysisContext context)
        {
            foreach (var method in _asyncVoidCandidates.Keys)
            {
                if (IsEventHandlerSignature(method) &&
                    (_registeredEventHandlers.ContainsKey(method) || IsUiCodeBehindEventHandler(method)))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    EdgeArchitectureDiagnostics.AsyncVoid,
                    GetSourceLocation(method),
                    method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }

        private static bool IsEventHandlerSignature(IMethodSymbol method)
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

        private static bool IsUiCodeBehindEventHandler(IMethodSymbol method)
            => IsOrDerivesFromMetadataName(method.ContainingType, "Avalonia.Controls.Control") ||
               IsOrDerivesFromMetadataName(method.ContainingType, "Avalonia.Controls.TopLevel") ||
               IsOrDerivesFromMetadataName(method.ContainingType, "Avalonia.Application");

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
                    var isConstructor = method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor;
                    if ((!method.IsImplicitlyDeclared || isConstructor) &&
                        (IsSourceSymbol(method) || isConstructor && IsSourceSymbol(current)))
                    {
                        _productionTaskRoots.TryAdd(NormalizeMethod(method)!, 0);
                    }
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
                    IsDataPipelineEnqueue(target),
                    isExceptionHandled: false,
                    target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        private void AnalyzeProductionTaskOutboundPaths(CompilationAnalysisContext context)
        {
            foreach (var root in _productionTaskRoots.Keys
                         .Where(ShouldAnalyzeAsIndependentProductionTaskRoot)
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
            var rootEnvironment = DelegateFlowEnvironment.Empty;
            var visited = new HashSet<string>(StringComparer.Ordinal)
            {
                CreateDelegateFlowVisitKey(root, rootEnvironment)
            };
            var queue = new Queue<PathNode>();
            if (_callGraph.TryGetValue(root, out var rootEdges))
            {
                foreach (var edge in rootEdges
                             .OrderBy(static item => item.Display, StringComparer.Ordinal)
                             .ThenBy(static item => LocationKey(item.Location), StringComparer.Ordinal))
                {
                    foreach (var transition in ResolveInvocationTransitions(edge, rootEnvironment))
                    {
                        queue.Enqueue(new PathNode(
                            transition.Edge.Target,
                            transition.Edge,
                            transition.Edge,
                            transition.Environment));
                    }
                }
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

                if (!visited.Add(CreateDelegateFlowVisitKey(current.Method, current.Environment)) ||
                    !_callGraph.TryGetValue(current.Method, out var edges))
                    continue;

                foreach (var edge in edges
                             .OrderBy(static item => item.Display, StringComparer.Ordinal)
                             .ThenBy(static item => LocationKey(item.Location), StringComparer.Ordinal))
                {
                    foreach (var transition in ResolveInvocationTransitions(edge, current.Environment))
                    {
                        queue.Enqueue(new PathNode(
                            transition.Edge.Target,
                            current.FirstEdge,
                            transition.Edge,
                            transition.Environment));
                    }
                }
            }

            return false;
        }

        private void AnalyzeProductionTaskEnqueueGuards(CompilationAnalysisContext context)
        {
            var reportedLocations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in _productionTaskRoots.Keys
                         .Where(ShouldAnalyzeAsIndependentProductionTaskRoot)
                         .OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal))
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var queue = new Queue<EnqueuePathNode>();
                queue.Enqueue(new EnqueuePathNode(
                    root,
                    isExceptionHandled: false,
                    DelegateFlowEnvironment.Empty));

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var visitKey = $"{current.IsExceptionHandled}:{CreateDelegateFlowVisitKey(current.Method, current.Environment)}";
                    if (!visited.Add(visitKey) ||
                        !_callGraph.TryGetValue(current.Method, out var edges))
                    {
                        continue;
                    }

                    foreach (var edge in edges
                                 .OrderBy(static item => item.Display, StringComparer.Ordinal)
                                 .ThenBy(static item => LocationKey(item.Location), StringComparer.Ordinal))
                    {
                        foreach (var transition in ResolveInvocationTransitions(edge, current.Environment))
                        {
                            var resolvedEdge = transition.Edge;
                            var isExceptionHandled = current.IsExceptionHandled || resolvedEdge.IsExceptionHandled;
                            if (resolvedEdge.IsDataPipelineEnqueue)
                            {
                                if (!isExceptionHandled && reportedLocations.Add(LocationKey(resolvedEdge.Location)))
                                {
                                    context.ReportDiagnostic(Diagnostic.Create(
                                        EdgeArchitectureDiagnostics.ProductionTaskEnqueueGuard,
                                        resolvedEdge.Location,
                                        root.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                                }

                                continue;
                            }

                            queue.Enqueue(new EnqueuePathNode(
                                resolvedEdge.Target,
                                isExceptionHandled,
                                transition.Environment));
                        }
                    }
                }
            }
        }

        private bool ShouldAnalyzeAsIndependentProductionTaskRoot(IMethodSymbol method)
        {
            var delegateParameters = method.Parameters
                .Where(static parameter => parameter.Type.TypeKind == TypeKind.Delegate)
                .Select(static parameter => parameter.OriginalDefinition)
                .ToArray();
            if (delegateParameters.Length == 0)
                return true;

            // Only closed, ordinary source helpers can inherit delegate bindings from every
            // source call site. Public/virtual/override/interface/constructor entry points
            // remain independent fail-closed roots because callers can exist outside this
            // compilation and therefore cannot be proven by the local call graph.
            if (!IsClosedSourceHelper(method))
                return true;

            var hasIncomingCall = false;
            foreach (var pair in _callGraph)
            {
                foreach (var edge in pair.Value)
                {
                    if (!SymbolEqualityComparer.Default.Equals(edge.Target, method))
                        continue;

                    if (!IsSourceSymbol(pair.Key))
                        return true;

                    hasIncomingCall = true;
                    foreach (var parameter in delegateParameters)
                    {
                        if (!edge.DelegateArguments.Any(binding =>
                                SymbolEqualityComparer.Default.Equals(binding.Parameter, parameter)))
                        {
                            return true;
                        }
                    }
                }
            }

            return !hasIncomingCall;
        }

        private static bool IsClosedSourceHelper(IMethodSymbol method)
        {
            if (!IsSourceSymbol(method) ||
                method.MethodKind != MethodKind.Ordinary ||
                method.IsAbstract ||
                method.IsVirtual ||
                method.IsOverride ||
                IsInterfaceImplementation(method))
            {
                return false;
            }

            if (method.DeclaredAccessibility == Accessibility.Private)
                return true;

            if (method.DeclaredAccessibility is not Accessibility.Internal and
                not Accessibility.Protected and
                not Accessibility.ProtectedAndInternal and
                not Accessibility.ProtectedOrInternal)
            {
                return false;
            }

            return method.ContainingType.IsSealed || !IsExternallyVisible(method.ContainingType);
        }

        private static bool IsInterfaceImplementation(IMethodSymbol method)
        {
            if (!method.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
                return true;

            var containingType = method.ContainingType;
            foreach (var @interface in containingType.AllInterfaces)
            {
                foreach (var member in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (containingType.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation &&
                        SymbolEqualityComparer.Default.Equals(
                            NormalizeMethod(implementation),
                            NormalizeMethod(method)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsExternallyVisible(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility is not Accessibility.Public and
                    not Accessibility.Protected and
                    not Accessibility.ProtectedOrInternal)
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<ResolvedInvocationTransition> ResolveInvocationTransitions(
            InvocationEdge edge,
            DelegateFlowEnvironment environment)
        {
            if (edge.DelegateInvocation is null)
            {
                yield return new ResolvedInvocationTransition(
                    edge,
                    ApplyDelegateArgumentBindings(environment, edge.DelegateArguments));
                yield break;
            }

            var resolved = ResolveDelegateValue(edge.DelegateInvocation, environment);
            if (resolved.HasUnknownTarget || resolved.Targets.IsDefaultOrEmpty)
            {
                yield return new ResolvedInvocationTransition(
                    new InvocationEdge(
                        edge.Target,
                        edge.Location,
                        isOutboundSink: true,
                        isDataPipelineEnqueue: false,
                        edge.IsExceptionHandled,
                        $"unresolved delegate {edge.Target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}"),
                    environment);
            }

            foreach (var target in resolved.Targets
                         .OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal)
                         .ThenBy(static method => CreateDelegateFlowSymbolKey(method), StringComparer.Ordinal))
            {
                var unverifiedExternalBoundary = IsUnverifiedExternalProductionTaskBoundary(target);
                yield return new ResolvedInvocationTransition(
                    new InvocationEdge(
                        target,
                        edge.Location,
                        IsOutboundSink(target) || unverifiedExternalBoundary,
                        IsDataPipelineEnqueue(target) || unverifiedExternalBoundary,
                        edge.IsExceptionHandled,
                        unverifiedExternalBoundary
                            ? $"unverified external boundary {target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}"
                            : target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)),
                    environment);
            }
        }

        private DelegateFlowEnvironment ApplyDelegateArgumentBindings(
            DelegateFlowEnvironment environment,
            ImmutableArray<DelegateArgumentBinding> bindings)
        {
            if (bindings.IsDefaultOrEmpty)
                return environment;

            var resolvedBindings = new List<KeyValuePair<ISymbol, ResolvedDelegateValue>>(bindings.Length);
            foreach (var binding in bindings)
            {
                resolvedBindings.Add(new KeyValuePair<ISymbol, ResolvedDelegateValue>(
                    binding.Parameter,
                    ResolveDelegateValue(binding.Value, environment)));
            }

            return environment.WithBindings(resolvedBindings);
        }

        private ResolvedDelegateValue ResolveDelegateValue(
            DelegateValue value,
            DelegateFlowEnvironment environment)
        {
            var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var visitingSources = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var hasUnknownTarget = false;
            ResolveDelegateValueCore(
                value,
                environment,
                targets,
                visitingSources,
                ref hasUnknownTarget);
            return new ResolvedDelegateValue(targets.ToImmutableArray(), hasUnknownTarget);
        }

        private void ResolveDelegateValueCore(
            DelegateValue value,
            DelegateFlowEnvironment environment,
            HashSet<IMethodSymbol> targets,
            HashSet<ISymbol> visitingSources,
            ref bool hasUnknownTarget)
        {
            foreach (var target in value.Targets)
                targets.Add(target);
            hasUnknownTarget |= value.HasUnknownTarget;

            foreach (var source in value.Sources)
            {
                if (!visitingSources.Add(source))
                {
                    hasUnknownTarget = true;
                    continue;
                }

                var resolvedSource = false;
                if (environment.TryGetValue(source, out var environmentValue))
                {
                    resolvedSource = true;
                    foreach (var target in environmentValue.Targets)
                        targets.Add(target);
                    hasUnknownTarget |= environmentValue.HasUnknownTarget;
                }

                if (_delegateAssignments.TryGetValue(source, out var assignments))
                {
                    resolvedSource = true;
                    foreach (var assignment in assignments)
                    {
                        ResolveDelegateValueCore(
                            assignment,
                            environment,
                            targets,
                            visitingSources,
                            ref hasUnknownTarget);
                    }
                }

                if (!resolvedSource)
                    hasUnknownTarget = true;
                visitingSources.Remove(source);
            }
        }

        private static string CreateDelegateFlowVisitKey(
            IMethodSymbol method,
            DelegateFlowEnvironment environment)
            => $"{CreateDelegateFlowSymbolKey(method)}|{environment.Fingerprint}";

        private static string CreateDelegateFlowSymbolKey(ISymbol symbol)
        {
            var sourceLocation = symbol.Locations
                .Where(static location => location.IsInSource)
                .OrderBy(static location => location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static location => location.SourceSpan.Start)
                .ThenBy(static location => location.SourceSpan.Length)
                .FirstOrDefault();
            if (sourceLocation is not null)
            {
                var sourcePath = (sourceLocation.SourceTree?.FilePath ?? string.Empty)
                    .Replace('\\', '/');
                return $"source:{symbol.ContainingAssembly?.Name}:{sourcePath}:{sourceLocation.SourceSpan.Start}:{sourceLocation.SourceSpan.Length}:{symbol.Kind}:{symbol.MetadataName}";
            }

            var qualifiedIdentity = symbol switch
            {
                IMethodSymbol method =>
                    $"{GetFullMetadataName(method.ContainingType.OriginalDefinition)}:{method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}",
                _ =>
                    $"{symbol.ContainingSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"
            };
            return $"metadata:{symbol.ContainingAssembly?.Name}:{symbol.Kind}:{qualifiedIdentity}";
        }

        private static string LocationKey(Location location)
            => $"{location.SourceTree?.FilePath ?? string.Empty}:{location.SourceSpan.Start}:{location.SourceSpan.Length}";

        private static bool IsDataPipelineEnqueue(IMethodSymbol method)
            => GetFullMetadataName(method.ContainingType.OriginalDefinition)
                   .Equals(
                       "IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService",
                       StringComparison.Ordinal) &&
               method.Name.Equals("EnqueueAsync", StringComparison.Ordinal);

        private static bool IsOutboundSink(IMethodSymbol method)
        {
            if (IsDataPipelineEnqueue(method))
                return false;

            if (IsForbiddenOutboundType(method.ContainingType))
                return true;

            return method.ContainingType.AllInterfaces.Any(IsForbiddenOutboundType);
        }

        private bool IsUnverifiedExternalProductionTaskBoundary(IMethodSymbol method)
        {
            var assemblyName = method.ContainingAssembly?.Name ?? string.Empty;
            if (assemblyName.Length == 0 ||
                assemblyName.Equals(_assemblyName, StringComparison.Ordinal) ||
                IsOutboundSink(method) ||
                IsDataPipelineEnqueue(method))
            {
                return false;
            }

            var role = EdgeArchitectureRegistry.ClassifyAssembly(assemblyName);
            if (role is not EdgeProjectRole.Application and
                not EdgeProjectRole.ModuleSdk and
                not EdgeProjectRole.SharedKernel)
            {
                return false;
            }

            return !IsApprovedExternalProductionTaskCall(method);
        }

        private bool IsApprovedExternalProductionTaskBoundary(IMethodSymbol method)
        {
            var assemblyName = method.ContainingAssembly?.Name ?? string.Empty;
            if (assemblyName.Length == 0 ||
                assemblyName.Equals(_assemblyName, StringComparison.Ordinal) ||
                IsOutboundSink(method) ||
                IsDataPipelineEnqueue(method))
            {
                return false;
            }

            var role = EdgeArchitectureRegistry.ClassifyAssembly(assemblyName);
            return (role is EdgeProjectRole.Application or
                    EdgeProjectRole.ModuleSdk or
                    EdgeProjectRole.SharedKernel) &&
                IsApprovedExternalProductionTaskCall(method);
        }

        private static bool IsApprovedExternalProductionTaskCall(IMethodSymbol method)
        {
            var type = method.ContainingType;
            var typeName = GetFullMetadataName(type.OriginalDefinition);
            var ns = GetNamespace(type);
            if ((typeName.Equals(
                     "IIoT.Edge.Application.Abstractions.Device.IDeviceService",
                     StringComparison.Ordinal) &&
                 method.Name.Equals("get_CurrentDevice", StringComparison.Ordinal)) ||
                (typeName.Equals(
                     "IIoT.Edge.Module.Sdk.DataPipeline.ModuleDataPipelineEnqueueResultMapper",
                     StringComparison.Ordinal) &&
                 method.Name.Equals("ToQueuedUploadResult", StringComparison.Ordinal)) ||
                (typeName.Equals(
                     "IIoT.Edge.SharedKernel.Collections.BoundedRecordQueue`1",
                     StringComparison.Ordinal) &&
                 method.Name.Equals("Enqueue", StringComparison.Ordinal)))
            {
                return true;
            }

            if (typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.DataPipeline.DataPipelineEnqueueResult",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Logging.ILogService",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Cloud.ICloudExecutionPolicy",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Device.DeviceSession",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Mes.MesCallResult",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Plc.Store.IPlcBuffer",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Plc.Store.IPlcBufferTransport",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Abstractions.Time.IProductionTimeProvider",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Common.DataPipeline.DataPipelineUploadTargetPolicy",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Features.Production.Planning.ProductionPlanOption",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Features.Production.Planning.ProductionPlanSelectionState",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.Application.Features.Production.Planning.IProductionPlanSelectionService",
                    StringComparison.Ordinal) ||
                typeName.Equals(
                    "IIoT.Edge.SharedKernel.Context.ProductionContext",
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (ns.StartsWith("IIoT.Edge.Application.Abstractions.Config", StringComparison.Ordinal) ||
                ns.StartsWith("IIoT.Edge.Application.Abstractions.Diagnostics", StringComparison.Ordinal) ||
                ns.StartsWith("IIoT.Edge.Application.Abstractions.Plc.Signals", StringComparison.Ordinal) ||
                ns.StartsWith("IIoT.Edge.Module.Sdk.Diagnostics", StringComparison.Ordinal) ||
                ns.StartsWith("IIoT.Edge.SharedKernel.DataPipeline", StringComparison.Ordinal))
            {
                return true;
            }

            return ns.Equals("IIoT.Edge.Module.Sdk.Base", StringComparison.Ordinal) &&
                   (type.Name.Equals("PlcTaskBase", StringComparison.Ordinal) ||
                    type.Name.Equals("ScheduledTaskBase", StringComparison.Ordinal) ||
                    type.Name.StartsWith("HeartbeatMirrorPlcTaskBase", StringComparison.Ordinal) ||
                    type.Name.StartsWith("PeriodicSnapshotUploadTaskBase", StringComparison.Ordinal));
        }

        private static bool IsForbiddenOutboundType(INamedTypeSymbol type)
        {
            var name = GetFullMetadataName(type.OriginalDefinition);
            if (name.Equals("System.Net.Http.HttpClient", StringComparison.Ordinal) ||
                name.Equals("System.Net.Http.HttpMessageInvoker", StringComparison.Ordinal) ||
                name.Equals("System.Net.Sockets.Socket", StringComparison.Ordinal) ||
                name.Equals("System.Net.Sockets.TcpClient", StringComparison.Ordinal) ||
                name.Equals("System.Net.Sockets.TcpListener", StringComparison.Ordinal) ||
                name.Equals("System.Net.Sockets.UdpClient", StringComparison.Ordinal) ||
                name.Equals("System.Net.Sockets.NetworkStream", StringComparison.Ordinal) ||
                name.Equals("IIoT.Edge.Application.Modules.MesRequestExecutor", StringComparison.Ordinal) ||
                name.Equals("IIoT.Edge.Application.Abstractions.Mes.IProcessMesUploader", StringComparison.Ordinal) ||
                name.Equals("IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader", StringComparison.Ordinal) ||
                name.Equals("IIoT.Edge.Application.Abstractions.Mes.IMesHttpClient", StringComparison.Ordinal) ||
                name.Equals("IIoT.Edge.Application.Abstractions.Cloud.ICloudHttpClient", StringComparison.Ordinal))
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
            internal InvocationEdge(
                IMethodSymbol target,
                Location location,
                bool isOutboundSink,
                bool isDataPipelineEnqueue,
                bool isExceptionHandled,
                string display,
                ImmutableArray<DelegateArgumentBinding> delegateArguments = default,
                DelegateValue? delegateInvocation = null)
            {
                Target = target;
                Location = location;
                IsOutboundSink = isOutboundSink;
                IsDataPipelineEnqueue = isDataPipelineEnqueue;
                IsExceptionHandled = isExceptionHandled;
                Display = display;
                DelegateArguments = delegateArguments.IsDefault
                    ? ImmutableArray<DelegateArgumentBinding>.Empty
                    : delegateArguments;
                DelegateInvocation = delegateInvocation;
            }

            internal IMethodSymbol Target { get; }
            internal Location Location { get; }
            internal bool IsOutboundSink { get; }
            internal bool IsDataPipelineEnqueue { get; }
            internal bool IsExceptionHandled { get; }
            internal string Display { get; }
            internal ImmutableArray<DelegateArgumentBinding> DelegateArguments { get; }
            internal DelegateValue? DelegateInvocation { get; }
        }

        private sealed class DelegateArgumentBinding
        {
            internal DelegateArgumentBinding(IParameterSymbol parameter, DelegateValue value)
            {
                Parameter = parameter;
                Value = value;
            }

            internal IParameterSymbol Parameter { get; }
            internal DelegateValue Value { get; }
        }

        private sealed class DelegateValue
        {
            internal DelegateValue(
                ImmutableArray<IMethodSymbol> targets,
                ImmutableArray<ISymbol> sources,
                bool hasUnknownTarget)
            {
                Targets = targets;
                Sources = sources;
                HasUnknownTarget = hasUnknownTarget;
            }

            internal ImmutableArray<IMethodSymbol> Targets { get; }
            internal ImmutableArray<ISymbol> Sources { get; }
            internal bool HasUnknownTarget { get; }
        }

        private sealed class ResolvedDelegateValue
        {
            internal ResolvedDelegateValue(
                ImmutableArray<IMethodSymbol> targets,
                bool hasUnknownTarget)
            {
                Targets = targets;
                HasUnknownTarget = hasUnknownTarget;
            }

            internal ImmutableArray<IMethodSymbol> Targets { get; }
            internal bool HasUnknownTarget { get; }
        }

        private sealed class DelegateFlowEnvironment
        {
            private readonly Dictionary<ISymbol, ResolvedDelegateValue> _bindings;
            private string? _fingerprint;

            private DelegateFlowEnvironment(Dictionary<ISymbol, ResolvedDelegateValue> bindings)
            {
                _bindings = bindings;
            }

            internal static DelegateFlowEnvironment Empty { get; } = new(
                new Dictionary<ISymbol, ResolvedDelegateValue>(SymbolEqualityComparer.Default));

            internal string Fingerprint => _fingerprint ??= string.Join(
                ";",
                _bindings.Select(static pair =>
                        $"{FormatSymbol(pair.Key)}={FormatResolvedValue(pair.Value)}")
                    .OrderBy(static value => value, StringComparer.Ordinal));

            internal bool TryGetValue(ISymbol symbol, out ResolvedDelegateValue value)
                => _bindings.TryGetValue(symbol, out value!);

            internal DelegateFlowEnvironment WithBindings(
                IEnumerable<KeyValuePair<ISymbol, ResolvedDelegateValue>> bindings)
            {
                var merged = new Dictionary<ISymbol, ResolvedDelegateValue>(
                    _bindings,
                    SymbolEqualityComparer.Default);
                foreach (var binding in bindings)
                    merged[binding.Key] = binding.Value;
                return new DelegateFlowEnvironment(merged);
            }

            private static string FormatResolvedValue(ResolvedDelegateValue value)
                => $"{value.HasUnknownTarget}:{string.Join(",", value.Targets
                    .Select(static target => CreateDelegateFlowSymbolKey(target))
                    .OrderBy(static target => target, StringComparer.Ordinal))}";

            private static string FormatSymbol(ISymbol symbol)
                => CreateDelegateFlowSymbolKey(symbol);
        }

        private sealed class ResolvedInvocationTransition
        {
            internal ResolvedInvocationTransition(
                InvocationEdge edge,
                DelegateFlowEnvironment environment)
            {
                Edge = edge;
                Environment = environment;
            }

            internal InvocationEdge Edge { get; }
            internal DelegateFlowEnvironment Environment { get; }
        }

        private sealed class EnqueuePathNode
        {
            internal EnqueuePathNode(
                IMethodSymbol method,
                bool isExceptionHandled,
                DelegateFlowEnvironment environment)
            {
                Method = method;
                IsExceptionHandled = isExceptionHandled;
                Environment = environment;
            }

            internal IMethodSymbol Method { get; }
            internal bool IsExceptionHandled { get; }
            internal DelegateFlowEnvironment Environment { get; }
        }

        private sealed class PathNode
        {
            internal PathNode(
                IMethodSymbol method,
                InvocationEdge firstEdge,
                InvocationEdge edge,
                DelegateFlowEnvironment environment)
            {
                Method = method;
                FirstEdge = firstEdge;
                Edge = edge;
                Environment = environment;
            }

            internal IMethodSymbol Method { get; }
            internal InvocationEdge FirstEdge { get; }
            internal InvocationEdge Edge { get; }
            internal DelegateFlowEnvironment Environment { get; }
        }
    }
}
