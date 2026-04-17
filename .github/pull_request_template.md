## Change Scope

- [ ] Host
- [ ] Injection
- [ ] Stacking
- [ ] Tools
- [ ] Shared

## Module Contract Impact

- [ ] No module contract change
- [ ] Module contract changed and compatibility impact is described below

Compatibility notes:

## Verification

- [ ] `dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj -p:BuildInParallel=false`
- [ ] `dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj -p:BuildInParallel=false --disable-build-servers`
- [ ] `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -p:BuildInParallel=false --disable-build-servers`
- [ ] Other verification described below

Additional verification:

## Release Impact

- [ ] No production release impact
- [ ] Affects production release packaging or runtime behavior

Release notes:
