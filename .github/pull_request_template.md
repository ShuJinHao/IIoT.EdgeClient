## Change Scope

- [ ] Host
- [ ] Plugin module
- [ ] Shared

## Module Contract Impact

- [ ] No module contract change
- [ ] Module contract changed and compatibility impact is described below

Compatibility notes:

## Verification

- [ ] `dotnet build IIoT.EdgeClient.slnx -c Release -p:BuildInParallel=false --disable-build-servers`
- [ ] `./scripts/tests/Get-EdgeTestInventory.ps1 && ./scripts/tests/Test-EdgeArchitectureProjectGraph.ps1 -RepositoryRoot . -SolutionPath IIoT.EdgeClient.slnx -Configuration Release`
- [ ] `./scripts/tests/Invoke-EdgeRequiredTests.ps1 -ResultsDirectory artifacts/test-results -Configuration Release`
- [ ] `./scripts/tests/Confirm-EdgeRequiredTestResults.ps1 -ResultsDirectory artifacts/test-results`
- [ ] Other verification described below

Additional verification:

## Release Impact

- [ ] No production release impact
- [ ] Affects production release packaging or runtime behavior

Release notes:
