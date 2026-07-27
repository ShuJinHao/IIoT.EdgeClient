# Local SDK package feed

Host 只消费 SDK clean commit `451cdf863d9fa9008810d031b2741521944987ad` 单次 pack 产生并已提交的 `2.0.6` nupkg 原字节，不读取 SDK 源码路径，也不保留 DLL fallback。完整机器清单位于 `sdk-package-set.json`；Private Plugins 必须提交同一组原字节。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.6.nupkg` | `89955c0fea7aeb31f7ba222abbfd1ff345ce3c0a66568d5f078a2ea49fcca45b` |
| `IIoT.Edge.Module.Contracts.2.0.6.nupkg` | `c2a21ec5f43ecb05b20df3cecb86e002f8dfa900f540708874fd9f0af4bb8405` |
| `IIoT.Edge.Module.Sdk.2.0.6.nupkg` | `c413ea39443d6c6561381eb52e125fe10bc0a61e4ce52f3c29c0557509f6e264` |
| `IIoT.Edge.UI.Shared.2.0.6.nupkg` | `0f938cddf6675405f16f495251fde4979388c4d8f221b8765c532b22e029d2b2` |
