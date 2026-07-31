# Local SDK package feed

Host 只消费 SDK merge commit `45094fe13513b32b7c3ebe27931c559531789701` 从干净 `main` 单次 pack 产生并已提交的 `2.0.10` nupkg 原字节，不读取 SDK 源码路径，也不保留 DLL fallback。完整机器清单位于 `sdk-package-set.json`；Private Plugins 必须提交同一组原字节。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.10.nupkg` | `764bd5e897743fe45a34963c815d5e610c5bd4bda2c8d8248cb94a66bd09f519` |
| `IIoT.Edge.Module.Contracts.2.0.10.nupkg` | `f0d1de5122591be61e9e5c20a60bb66ce4d909fdd07eb4db06956bbef1554b70` |
| `IIoT.Edge.Module.Sdk.2.0.10.nupkg` | `c1f0d281d339d01f3602a1522c5b337def8accabd79977064e1a4783ac69f3c3` |
| `IIoT.Edge.UI.Shared.2.0.10.nupkg` | `4e2f1734c8012a562cfcfe6fd186877cca65c08533c693f34e8e2cdbcdb38661` |
