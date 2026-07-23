## Change Scope

- [ ] Host
- [ ] Plugin module
- [ ] Shared

## Module Contract Impact

- [ ] No module contract change
- [ ] Module contract changed and compatibility impact is described below

Compatibility notes:

## Verification

- [ ] Default selector ran Architecture/Security and affected Business for this diff
- [ ] Selector/workflow changed: `./scripts/tests/Test-EdgeCiTestSelection.ps1` ran; otherwise not applicable
- [ ] `./scripts/tests/Select-EdgeCiTests.ps1 -Mode Default -BaseRef <base> -HeadRef HEAD -OutputPath artifacts/ci-selection.json`
- [ ] `./scripts/tests/Invoke-EdgeCiSelectedTests.ps1 -SelectionPath artifacts/ci-selection.json -ResultsDirectory artifacts/ci-test-results -Configuration Release`
- [ ] Quality/Full/CrossProject was not run, or its explicit user authorization is described below
- [ ] Other verification described below

Additional verification:

## Release Impact

- [ ] No production release impact
- [ ] Affects production release packaging or runtime behavior

Release notes:
