# Local SDK package feed

Host 只消费 SDK merge commit `7b63b15956c732cf4e05e33d57c66f3bde99f43b` 从干净 `main` 单次 pack 产生并已提交的 `2.0.7` nupkg 原字节，不读取 SDK 源码路径，也不保留 DLL fallback。完整机器清单位于 `sdk-package-set.json`；Private Plugins 必须提交同一组原字节。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.7.nupkg` | `207da8d41d2bbf0c026f518331a15d4a1ab9579d7eddec01948672dc03fd5f69` |
| `IIoT.Edge.Module.Contracts.2.0.7.nupkg` | `de40f370ebdbeb96e8bf8a0fffbaa77cb447359108d9573ff2218cb23117a431` |
| `IIoT.Edge.Module.Sdk.2.0.7.nupkg` | `a95f0f1686e38026566028c83b142ff9132a0221d7efdd23c3785ca1a74ee79b` |
| `IIoT.Edge.UI.Shared.2.0.7.nupkg` | `94c9c0d89f1d9bbdd860283ac03986b2bb9541c157d3a002e9f654d468e6dd45` |
