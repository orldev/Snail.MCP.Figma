using Microsoft.Data.Sqlite;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>Commands and the way values are written in the cache file: moments as Unix milliseconds.</summary>
internal static class SnapshotSql
{
    public static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static long Milliseconds(DateTimeOffset moment) => moment.ToUnixTimeMilliseconds();

    public static DateTimeOffset Moment(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

    public static string? TextOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
