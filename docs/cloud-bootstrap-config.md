# Cloud Bootstrap 配置约定

EdgeClient 只允许通过 Gateway 公共入口访问云端 bootstrap。API 路径只在 `CloudApi:Paths` 配置中维护，生产代码不提供默认路径兜底：

- `CloudApi:BaseUrl` 必须填写 Gateway 地址，不能填写 HttpApi 内部地址。
- `CloudApi:Paths:*` 必须在配置中显式填写相对 API 路径，以 `/` 开头，不能填写完整地址。
- `CloudApi:Paths:RecipeByDeviceTemplate` 必须包含 `{deviceId}`。
- `CloudApi:ClientCode` 只用于设备寻址。
- `CloudApi:BootstrapSecret` 必须填写云端设备注册或轮换时返回的启动密钥。

客户端不生成启动密钥，不支持无密钥 bootstrap，也不支持绕过 Gateway 直连 HttpApi。
