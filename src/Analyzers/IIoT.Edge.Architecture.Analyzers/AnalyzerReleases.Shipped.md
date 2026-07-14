## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
WSARCH003 | IIoT.Architecture | Error | Production assemblies cannot reference test assets.
WSARCH004 | IIoT.Architecture | Error | Project-role dependency registry is enforced.
DDD004 | IIoT.Architecture | Error | Generic repositories are limited to five approved roots.
DDD001 | IIoT.Architecture | Error | Domain/Core cannot depend on upper layers or provider frameworks.
DDD007 | IIoT.Architecture | Error | Application cannot own concrete provider clients.
DATA001 | IIoT.Architecture | Error | Presentation and host code cannot own database APIs.
DATA002 | IIoT.Architecture | Error | Application and domain code cannot own provider APIs.
DATA005 | IIoT.Architecture | Error | Provider commit is limited to the EF owner.
DATA006 | IIoT.Architecture | Error | Dapper writes are limited to the Dapper owner.
PLUG001 | IIoT.Architecture | Error | Plugins cannot use forbidden host implementation symbols.
PLUG002 | IIoT.Architecture | Error | Concrete plugins cannot use another concrete plugin.
PLUG003 | IIoT.Architecture | Error | Host/common projects cannot use concrete plugin symbols.
PLUG004 | IIoT.Architecture | Error | Plugin entry and SDK role metadata is mandatory.
EDGEOUT001 | IIoT.Architecture | Error | Production PLC tasks can emit outbound work only through DataPipeline.
EDGEPLCOWN001 | IIoT.Architecture | Error | Concrete PLC transports have one registered owner.
EDGEASYNC001 | IIoT.Architecture | Error | Task sync-over-async is forbidden.
EDGEASYNC002 | IIoT.Architecture | Error | async void is limited to event handlers.
