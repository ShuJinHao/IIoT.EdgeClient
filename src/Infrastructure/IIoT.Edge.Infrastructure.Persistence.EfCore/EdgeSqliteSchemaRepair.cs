using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore;

/// <summary>
/// 开发阶段 SQLite 结构修复器，用于收口已改名但本地库已应用旧迁移的字段。
/// </summary>
internal static class EdgeSqliteSchemaRepair
{
    private const string IoMappingTable = "hw_io_mapping";

    public static void Repair(EdgeDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            if (!TableExists(connection, IoMappingTable))
            {
                return;
            }

            RepairIoMappingColumns(connection);
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static void RepairIoMappingColumns(DbConnection connection)
    {
        var columns = LoadColumns(connection, IoMappingTable);

        EnsureColumn(connection, columns, "category", "TEXT NOT NULL DEFAULT '单点读数据'");
        RenameOrCreateColumn(connection, columns, "label", "signal_key", "TEXT NOT NULL DEFAULT ''");
        RenameOrCreateColumn(connection, columns, "group_name", "business_group", "TEXT NOT NULL DEFAULT ''");
        RenameOrCreateColumn(connection, columns, "display_role", "signal_name", "TEXT NOT NULL DEFAULT ''");
    }

    private static void RenameOrCreateColumn(
        DbConnection connection,
        ISet<string> columns,
        string oldName,
        string newName,
        string definition)
    {
        if (columns.Contains(newName))
        {
            if (columns.Contains(oldName))
            {
                Execute(
                    connection,
                    $"""
                    UPDATE {IoMappingTable}
                    SET {newName} = {oldName}
                    WHERE ({newName} IS NULL OR TRIM({newName}) = '')
                      AND {oldName} IS NOT NULL
                      AND TRIM({oldName}) <> '';
                    """);
            }

            return;
        }

        if (columns.Contains(oldName))
        {
            Execute(connection, $"ALTER TABLE {IoMappingTable} RENAME COLUMN {oldName} TO {newName};");
            columns.Remove(oldName);
            columns.Add(newName);
            return;
        }

        EnsureColumn(connection, columns, newName, definition);
    }

    private static void EnsureColumn(
        DbConnection connection,
        ISet<string> columns,
        string name,
        string definition)
    {
        if (columns.Contains(name))
        {
            return;
        }

        Execute(connection, $"ALTER TABLE {IoMappingTable} ADD COLUMN {name} {definition};");
        columns.Add(name);
    }

    private static bool TableExists(DbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static ISet<string> LoadColumns(DbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        using var reader = command.ExecuteReader();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
