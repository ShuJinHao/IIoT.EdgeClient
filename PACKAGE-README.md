# IIoT.EdgeClient Internal Package

This package is part of the internal single-repo module architecture for `IIoT.EdgeClient`.

## Usage rules

- Treat host packages as the only allowed extension boundary for modules.
- Keep module-specific behavior inside `src/Modules`.
- Use the package feed and integration validation flow before promoting a release.
- Follow the repository collaboration guide in `docs/single-repo-collaboration.md`.
