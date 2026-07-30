# Local SDK package feed

Host 只消费 SDK merge commit `e7a746d22b49215344c2e5cdaca3b3b9e5536cb2` 从干净 `main` 单次 pack 产生并已提交的 `2.0.8` nupkg 原字节，不读取 SDK 源码路径，也不保留 DLL fallback。完整机器清单位于 `sdk-package-set.json`；Private Plugins 必须提交同一组原字节。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.8.nupkg` | `302e31a3bac227a6a9ccf7204973189178e345aa0446a75e90e18bbc6f396426` |
| `IIoT.Edge.Module.Contracts.2.0.8.nupkg` | `b25b3f7749e9d3d223cbd1b8e0dcd6350699bbbdb86f9f6c4f7194fd46b9e5a5` |
| `IIoT.Edge.Module.Sdk.2.0.8.nupkg` | `ba8400c3a7da50e145800cb0f686312c87d34e611929978d3ed59d84b5bc66fc` |
| `IIoT.Edge.UI.Shared.2.0.8.nupkg` | `39b68376ad5d80e86c01f9a463af44da7ce79be698959bfba933829834ef6b72` |
