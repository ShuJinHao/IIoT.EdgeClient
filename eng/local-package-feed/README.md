# Local SDK package feed

Host 只消费 SDK merge commit `a0505c809c2bae7d85afc27b099ce59848c590a0` 从干净 `main` 单次 pack 产生并已提交的 `2.0.9` nupkg 原字节，不读取 SDK 源码路径，也不保留 DLL fallback。完整机器清单位于 `sdk-package-set.json`；Private Plugins 必须提交同一组原字节。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.9.nupkg` | `2cff01d53dccd2942c9b95f022278b32018ea59f45b88162193032c75ee1b015` |
| `IIoT.Edge.Module.Contracts.2.0.9.nupkg` | `5289b4c38bbd28891944eb0cb890706ea1d8249773b570995ddb7d327606dec0` |
| `IIoT.Edge.Module.Sdk.2.0.9.nupkg` | `54500d302212b1b800172171e5dc2f5a993ca303ad79a66d8f20bde27698d75e` |
| `IIoT.Edge.UI.Shared.2.0.9.nupkg` | `fac88f29ce588b7de2ca4dfb6a2abee20bbaf3803a262d379280a238eacb48ad` |
