# Local SDK package feed

本目录保存 SDK `2.0.13` 的正式兼容输入原字节，Host 不读取 SDK 源码路径，也不保留 DLL fallback。四个包由 SDK 干净 `main` 提交 `419d281026f0db4064c9af1049eae272e8ab33f4` 单次打包生成，来源、包内文件清单和依赖闭包以 `sdk-package-set.json` 为准；该清单 SHA-256 为 `ad56a8831ab7bfc00c5bdc8fc0c2a675a8ac8c46b65e2760af24ff9a4d47884c`。Private Plugins 与 Host 必须消费同一组原字节，禁止分别重新打包。已有 `2.0.12` 包继续作为既有证据保留，本次未覆盖其字节。本目录内容尚未发布或部署。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.13.nupkg` | `7e3ccb6906b6b04b341bcfc52217487c5603c1ef6c2c39f380fa3b841a2347ce` |
| `IIoT.Edge.Module.Contracts.2.0.13.nupkg` | `65d0f6b0aef82e7beaf16ac8f03053b3b54eaa63e07eaff044e4f0c64ad9697d` |
| `IIoT.Edge.Module.Sdk.2.0.13.nupkg` | `295e78c8312545829aa9dc69be425f5367252f87da9afb74b4eda6066d2658be` |
| `IIoT.Edge.UI.Shared.2.0.13.nupkg` | `80bbe13c4f00d31958646c132e46300edb5ce49541b9c10ca0f6cecaace98c47` |
