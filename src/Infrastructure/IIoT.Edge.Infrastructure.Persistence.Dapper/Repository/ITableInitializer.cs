using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;

/// <summary>
/// 表初始化接口
/// 
/// 每个 Store 实现这个接口，提供自己的建表 SQL
/// DI 注册时统一调用一次，确保表存在
/// 
/// 用 CREATE TABLE IF NOT EXISTS，幂等安全，重复调用不会出错
/// </summary>
public interface ITableInitializer
{
    /// <summary>
    /// 数据库名称（不含扩展名）
    /// 对应 SqliteConnectionFactory.Create 的参数
    /// 如："pipeline"、"logs"、"production"
    /// </summary>
    string DbName { get; }

    /// <summary>
    /// 执行建表（由 DI 注册时统一调用）
    /// </summary>
    Task InitializeTableAsync(IDbConnection connection);
}