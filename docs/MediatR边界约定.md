# MediatR 边界约定

本文档固定当前 EdgeClient 对 MediatR 的使用边界。MediatR 不是本轮删除目标；本轮只清理真实重复点和 Presentation ViewModel 直连请求总线的问题。

## 允许的用法

- Application 内部可以通过 `ISender.Send(...)` 编排命令和查询，也可以继续保留现有 `IRequest` / `IRequestHandler` 用例实现。
- Runtime 和 Infrastructure 可以通过 `IPublisher.Publish(...)` 发布运行时事件或数据管道通知。
- Presentation 可以实现 `INotificationHandler<T>` 接收 UI 刷新通知，例如产能更新后刷新页面状态。
- SharedKernel 里的 `ICommand` / `IQuery` 包装暂时保留，不作为 P0 拆分目标。

## 禁止的用法

- Presentation ViewModel 不得直接注入 `ISender`，也不得直接 `using MediatR`。
- Presentation 不得重新定义页面级 `IRequest` / `IRequestHandler` 用例副本。
- 不得为了 DDD 形式统一，把稳定的 `Send` 命令查询和 `Publish` 发布订阅链路硬改成另一套抽象。
- 不得把插件业务、Runtime task、PLC/MES/Cloud 链路改造混入 MediatR 边界修复。

## 当前结论

第二十三批删除了 RecipeView Presentation 内重复的 MediatR 查询/命令副本。第二十四批将 `IoViewViewModel` 的 `ISender` 直连收敛到 Application facade。当前剩余的 Presentation MediatR 使用应只限通知订阅；Application、Runtime、Infrastructure 的 MediatR 使用按各自边界保留。
