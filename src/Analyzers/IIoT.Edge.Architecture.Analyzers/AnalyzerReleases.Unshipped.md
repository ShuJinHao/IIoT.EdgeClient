; Unshipped analyzer release tracking

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
PLUG005 | IIoT.Architecture | Error | Plugin channel, hardware, signal, and sample contracts must use registered module seams.
EDGEOUT002 | IIoT.Architecture | Error | Production PLC tasks must handle DataPipeline enqueue exceptions at the task boundary.
EDGECOMP001 | IIoT.Architecture | Error | Removed compatibility contracts cannot be reintroduced.
EDGECLOUDCFG001 | IIoT.Architecture | Error | Production C# Cloud API routes must come from configuration.
EDGEPRES001 | IIoT.Architecture | Error | Presentation cannot own MediatR requests, handlers, or sender-driven use cases.
EDGEPRES002 | IIoT.Architecture | Error | Presentation ValidationIssue messages must use localized resources instead of direct Chinese literals.
