# Local SDK package feed

本目录当前保存统一复审候选 SDK `2.0.13` 的实际 nupkg 原字节，Host 不读取 SDK 源码路径，也不保留 DLL fallback。四个包来自 SDK 基础提交 `fe8b4c46c48197eecbeaf74a8463cd347edf9129` 之上的未提交联合实施候选，只用于本轮整体审核；用户审核通过并形成干净 SDK 提交后，正式发布前必须重新单次 pack 并更新清单。Private Plugins 与 Host 必须消费同一组原字节。已有 `2.0.12` 候选包仍保留为旧候选证据，本次未覆盖其字节。

| Package | SHA-256 |
|---|---|
| `IIoT.Edge.Module.Analyzers.2.0.13.nupkg` | `c8166d172e2c7feb8ec63b66e686d6c122bd8aa9fb605960d8ca98304158251b` |
| `IIoT.Edge.Module.Contracts.2.0.13.nupkg` | `9bcfd803a92870fbac19605b6e82851f2a1be39a89eb43d99c9d508fc442194c` |
| `IIoT.Edge.Module.Sdk.2.0.13.nupkg` | `bc8b866a020a779b87357bc7dd6d15e2e5179835d91932ebc5423453d36ea083` |
| `IIoT.Edge.UI.Shared.2.0.13.nupkg` | `5d250b882897ba2de89c8e095a643119604ca50ed0d2c156e077f4dfce3e0205` |
