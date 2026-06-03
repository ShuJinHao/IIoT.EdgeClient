# Edge Converter 契约

Shared converter 只用于 UI 投影：把字符串、枚举或轻量显示值映射为共享视觉状态、共享图标或 Avalonia 视觉资源。

当前 converter：

- `LogLevelToEdgeVisualStatusConverter`：日志等级字符串映射为 `EdgeVisualStatus`。
- `ProfileIconPathConverter`：Launcher profile 图标 key 映射为 `Edge.Icon.Profile.*` 几何资源。

## 新增规则

- 资源 key 统一放在 `Avalonia/Resources/EdgeConverters.axaml`，命名使用 `Edge.Converter.*`。
- Converter 不允许调用 service、访问数据库、读取权限、改变路由、改变认证状态或制造假数据。
- 已有 Avalonia 内置 binding 能表达时，优先使用内置能力，不新增 converter。
- 新增 converter 前必须先确认它是跨页面复用的显示层映射；页面私有 converter 不允许进入业务项目。
- 枚举列表、权限判断、业务状态聚合应由 ViewModel 或业务服务提供，不由 converter 临时推导。
