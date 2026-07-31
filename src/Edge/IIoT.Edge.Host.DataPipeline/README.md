# DataPipeline 上传补偿链路说明

DataPipeline 的补偿表只保存完整电芯记录 JSON，不拆分插件业务字段。业务字段、payload 映射、MES code/options 仍在插件内，Runtime 只负责把失败记录放到正确链路并在条件恢复后补传。

## Cloud 链路

- 主补传表：`pipeline_cloud.failed_cloud_records`
- fallback 表：`pipeline_cloud.cloud_fallback_records`
- deadletter 表：`pipeline_cloud.dead_cloud_records`
- 补传任务：`CloudRetryTask`
- 失败来源：Cloud consumer 上传失败、fallback 恢复失败、反序列化失败、容量阻塞。

Cloud gate 阻断时只影响 Cloud consumer 和 Cloud retry，不会写入或领取 MES 表。

## MES 链路

- 主补传表：`pipeline_mes.failed_mes_records`
- fallback 表：`pipeline_mes.mes_fallback_records`
- deadletter 表：`pipeline_mes.dead_mes_records`
- 补传任务：`MesRetryTask`
- 失败来源：MES consumer 上传失败、fallback 恢复失败、反序列化失败、容量阻塞。

MES 总开关关闭或 MES 心跳未恢复时，`MesRetryTask` 不领取本地记录，也不调用 MES 接口。心跳恢复后，任务先把 `mes_fallback_records` 中可恢复的记录搬回 `failed_mes_records`，再从主补传表领取批次补传。

## 失败写入顺序

1. `DataPipelineService` 先将完整完工信封写入独立的 `pipeline_ingress.db`；内存队列只负责唤醒，队列满或进程重启不会裁剪 consumer。
2. `ProcessQueueTask` 从持久入口恢复未完成 consumer，并在 durable consumer 失败时进入对应通道补偿链。
3. 根据 consumer 的 `DataPipelineRetryChannel` 选择 Cloud 或 MES。
4. 优先写对应链路的 retry 表，记录 `ProcessType`、`CellDataJson`、`FailedTarget`、`ErrorMessage` 和下次重试时间。
5. retry 表不可写时，写对应链路的 fallback 表。
6. 容量阻塞、fallback 也不可写、或 retry/fallback 中的 JSON 无法反序列化时，写对应链路的 deadletter 表。
7. deadletter 也失败时，才写 critical fallback 文件；入口 consumer 回执保持未完成，等待后续恢复。

Cloud/MES 的数据库、表名、诊断状态、容量状态和重试任务都保持独立；共享 helper 只能复用无业务语义的写入外壳，不能合并两条运行时链路。
