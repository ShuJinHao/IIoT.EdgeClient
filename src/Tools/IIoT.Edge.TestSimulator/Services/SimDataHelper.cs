using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using System.Data;

namespace IIoT.Edge.TestSimulator.Services;

/// <summary>
/// 测试专用数据库辅助工具。
/// 直接操作 SQLite，用于测试前后清理和状态准备。
/// </summary>
public sealed class SimDataHelper
{
    private readonly SqliteConnectionFactory _factory;

    public SimDataHelper(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task ResetRetryTimesAsync()
    {
        await ExecuteAsync("pipeline_cloud",
            "UPDATE failed_cloud_records SET NextRetryTime = @t",
            cmd =>
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@t";
                p.Value = DateTime.UtcNow.AddSeconds(-5).ToString("O");
                cmd.Parameters.Add(p);
            });
    }

    public async Task ClearAllAsync()
    {
        await ExecuteAsync("pipeline_cloud", "DELETE FROM failed_cloud_records");
        await ExecuteAsync("pipeline_cloud", "DELETE FROM cloud_fallback_records");
        await ExecuteAsync("pipeline_cloud", "DELETE FROM capacity_buffer");
        await ExecuteAsync("pipeline_cloud", "DELETE FROM device_log_buffer");
        await ExecuteAsync("pipeline_mes", "DELETE FROM failed_mes_records");
        await ExecuteAsync("pipeline_mes", "DELETE FROM mes_fallback_records");
    }

    private Task ExecuteAsync(string dbName, string sql, Action<IDbCommand>? bindParams = null)
    {
        return Task.Run(() =>
        {
            using var conn = _factory.Create(dbName);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            bindParams?.Invoke(cmd);
            cmd.ExecuteNonQuery();
        });
    }
}
