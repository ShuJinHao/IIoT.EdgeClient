using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;

/// <summary>
/// 设备日志的 SQLite 离线缓冲记录。
/// 
/// 当 `/api/v1/edge/device-logs` 提交失败或设备离线时写入。
/// RetryTask 的 Cloud 通道补传时会批量读取并重新提交。
/// </summary>
public class DeviceLogRecord
{
    public long Id { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string LogTime { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

