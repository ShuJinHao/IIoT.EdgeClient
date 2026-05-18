# 04. 本地管理员登录基线

本文记录 Launcher 本地账号目录、登录、改密和安全边界。该链路不依赖云端设备 bootstrap。

## 账号目录初始化

Launcher 启动时调用 `LauncherAccountCatalogInitializer.EnsureCatalogExists()`。对应 `App.xaml.cs:18-19`。

当前规则：

- 目标账号文件为 `baseDirectory\launcher.accounts.json`。
- 如果目标文件存在，初始化直接返回。
- 如果目标文件不存在，则从 `baseDirectory\launcher.accounts.sample.json` 复制一份。
- 如果 sample 文件不存在，启动抛出 `FileNotFoundException`。

对应 `LauncherAccountCatalogInitializer.cs:16-35`。

`launcher.accounts.sample.json` 包含字段：

- `UserName`
- `DisplayName`
- `PasswordHash`
- `IsEnabled`

本文不记录任何实际密码哈希值。

## 账号读取和写回

`LauncherAccountCatalog` 的行为：

- 读取 `launcher.accounts.json`，缺失或空文件会抛异常。对应 `LauncherAccountCatalog.cs:23-37`。
- 账号以大小写不敏感的 `UserName` 建立字典。对应 `LauncherAccountCatalog.cs:29-36`。
- 写回密码时更新指定账号的 `PasswordHash`，然后以缩进 JSON 保存整个账号列表。对应 `LauncherAccountCatalog.cs:39-56`。
- 映射时要求 `UserName`、`DisplayName`、`PasswordHash` 非空，`IsEnabled` 默认 true。对应 `LauncherAccountCatalog.cs:58-80`。

## 登录校验

`LocalLauncherAuthService.Authenticate` 的当前规则：

- 用户名为空返回失败。
- 密码为空返回失败。
- 账号不存在或禁用返回失败。
- 使用 `LauncherPasswordHasher.Verify` 对比输入密码和存储哈希。
- 成功时返回 `DisplayName`。

对应 `LocalLauncherAuthService.cs:14-39`。

密码哈希实现：

- 使用 UTF-8 文本的 SHA256。
- 输出为十六进制字符串。
- 校验时大小写不敏感比较。

对应 `LauncherPasswordHasher.cs:8-26`。

## 改密流程

改密入口有两层：

- `ChangePasswordWindow` 做 UI 输入校验：用户名、旧密码、新密码不能为空，新密码长度至少 6，确认密码必须一致。对应 `ChangePasswordWindow.xaml.cs:80-118`。
- `LauncherMainViewModel.ChangePasswordAsync` 调用 `LocalLauncherAuthService.ChangePassword` 并把结果反馈给 UI。对应 `LauncherMainViewModel.cs:136-166`。

服务层规则：

- 新密码不能为空。
- 新密码长度至少 6。
- 先用旧密码调用登录校验。
- 旧密码通过后，使用 SHA256 计算新哈希并写回账号目录。

对应 `LocalLauncherAuthService.cs:41-63`。

## 状态和错误显示

登录失败会重置认证状态、清空 Profile 列表、显示错误，并把状态消息设为登录失败。对应 `LauncherMainViewModel.cs:105-114`、`LauncherMainViewModel.cs:214-222`。

登录成功会加载 Profile、设置欢迎消息、清空过滤条件并更新状态。对应 `LauncherMainViewModel.cs:116-134`。

## 安全边界记录

- 该登录只保护本地 Launcher 入口，不是云端身份认证。
- 密码哈希当前为无盐 SHA256。
- 当前代码未实现失败次数锁定、账号审计、会话过期或云端同步。
- `launcher.accounts.json` 位于应用基目录，由本地文件权限承担主要保护。
- 登录成功后仍只是允许用户选择 Profile 并启动 Shell；设备 bootstrap、上传 token 和云端上传门控由 Shell 内的设备链路独立处理。

Avalonia 迁移时应保持这些行为事实，不在 UI 迁移阶段混入账号体系重构。
