using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using System.Data;

namespace IIoT.Edge.TestSimulator.Services;

/// <summary>
/// 测试专用数据库辅助工具
/// 直接操作 SQLite，用于测试前后清理和状态准备
/// </summary>
public sealed class SimDataHelper
{
    private readonly SqliteConnectionFactory _factory;

    public SimDataHelper(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 将 failed_cell_records 的 NextRetryTime 设置为可立即重试的时间点
    /// </summary>
    public async Task ResetRetryTimesAsync()
    {
        await ExecuteAsync("pipeline",
            "UPDATE failed_cell_records SET NextRetryTime = @t",
            cmd =>
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@t";
                p.Value = DateTime.UtcNow.AddSeconds(-5).ToString("O");
                cmd.Parameters.Add(p);
            });
    }

    /// <summary>
    /// 清空所有测试数据（重置按钮用）
    /// </summary>
    public async Task ClearAllAsync()
    {
        await ExecuteAsync("pipeline", "DELETE FROM failed_cell_records");
        await ExecuteAsync("pipeline", "DELETE FROM capacity_buffer");
        await ExecuteAsync("pipeline", "DELETE FROM device_log_buffer");
    }

    // 内部数据库执行辅助

    private Task ExecuteAsync(string dbName, string sql,
        Action<IDbCommand>? bindParams = null)
    {
        return Task.Run(() =>
        {
            using var conn = _factory.Create(dbName);
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = sql;
            bindParams?.Invoke(cmd);
            cmd.ExecuteNonQuery();
        });
    }
}
