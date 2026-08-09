# Local SDK package feed

本目录保存 SDK `2.0.14` 的正式兼容输入原字节，Host 不读取 SDK 源码路径，也不保留 DLL fallback。四个包由 SDK 干净 `main` 提交 `a7995c473c59ebea19283f0cb11f5f99af354726` 单次打包生成，来源、包内文件清单和依赖闭包以 `sdk-package-set.json` 为准；该清单 SHA-256 为 `57dca9eb07f82d051f8c4346d1f332bd18e8abe288881f12b0ec893c3505115a`。Private Plugins 与 Host 必须消费同一组原字节，禁止分别重新打包。已有 `2.0.12`、`2.0.13` 包继续作为既有证据保留，本次未覆盖其字节。本目录内容尚未发布或部署。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.14.nupkg` | `c8bc5dc67adb0b676faf4f9ddf3753e42d06bfb913de2f4ab320b207018828e2` |
| `IIoT.Edge.Module.Contracts.2.0.14.nupkg` | `a4a2542c62d8356cfb9d6822dd9a2f6cf2b3110e9285e61cafa7dc563603eff5` |
| `IIoT.Edge.Module.Sdk.2.0.14.nupkg` | `03cc4e1113595a661c56c9e81a7298d5015862f959ef81a5e4ada9039ce48db7` |
| `IIoT.Edge.UI.Shared.2.0.14.nupkg` | `d1d6e47621f8a4ff14ece70d43219d986d6824bee7b0cda856ffc9f3473a6b81` |
