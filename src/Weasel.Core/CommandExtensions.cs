using System.Data;
using System.Data.Common;
using JasperFx.Core;

namespace Weasel.Core;

public static class CommandExtensions
{
    /// <summary>
    ///     Create a CommandBuilder around the DbConnection
    /// </summary>
    /// <param name="connection"></param>
    /// <returns></returns>
    public static DbCommandBuilder ToCommandBuilder(this DbConnection connection)
    {
        return new DbCommandBuilder(connection);
    }

    /// <summary>
    ///     Set the CommandText on this DbCommand in a Fluent Interface style
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="sql"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T Sql<T>(this T cmd, string sql) where T : DbCommand
    {
        cmd.CommandText = sql;
        return cmd;
    }

    /// <summary>
    ///     Set the command text of the command object to a stored procedure name and
    ///     change the command type to StoredProcedure in a fluent interface style
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="storedProcedureName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T CallsSproc<T>(this T cmd, string storedProcedureName) where T : DbCommand
    {
        cmd.CommandText = storedProcedureName;
        cmd.CommandType = CommandType.StoredProcedure;

        return cmd;
    }

    /// <summary>
    ///     Set the command text of the command object to a stored procedure name and
    ///     change the command type to StoredProcedure in a fluent interface style
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="storedProcedureName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T CallsSproc<T>(this T cmd, DbObjectName storedProcedureName) where T : DbCommand
    {
        cmd.CommandText = storedProcedureName.QualifiedName;
        cmd.CommandType = CommandType.StoredProcedure;

        return cmd;
    }

    /// <summary>
    ///     Execute all of the SQL statements against the supplied DbConnection. This assumes
    ///     that the connection is already open
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="sqls"></param>
    /// <returns></returns>
    public static Task<int> RunSqlAsync(this DbConnection conn, params string[] sqls) =>
        conn.RunSqlAsync(default, sqls);

    /// <summary>
    ///     Execute all of the SQL statements against the supplied DbConnection. This assumes
    ///     that the connection is already open
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="sqls"></param>
    /// <returns></returns>
    public static Task<int> RunSqlAsync(this DbConnection conn, CancellationToken ct, params string[] sqls)
    {
        var sql = sqls.Join(";");
        return conn.CreateCommand(sql).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    ///     Create a new DbCommand attached to the connection with the supplied
    ///     CommandText
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="sql"></param>
    /// <returns></returns>
    public static DbCommand CreateCommand(this DbConnection conn, string sql)
    {
        var command = conn.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// <summary>
    ///     Create a new DbCommand enlisted in the current transaction and connection
    /// </summary>
    /// <param name="tx"></param>
    /// <param name="command"></param>
    /// <returns></returns>
    public static DbCommand CreateCommand(this DbTransaction tx, string command)
    {
        var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = command;

        return cmd;
    }

    /// <summary>
    ///     Execute the supplied command as a data reader and convert each row to an object
    ///     of type T with the supplied transform function
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="transform"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static async Task<IReadOnlyList<T>> FetchListAsync<T>(
        this DbCommand cmd,
        Func<DbDataReader, Task<T>> transform,
        CancellationToken cancellation = default
    )
    {
        var list = new List<T>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellation).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            list.Add(await transform(reader).ConfigureAwait(false));
        }

        return list;
    }

    /// <summary>
    ///     Execute the command return a list of the values in the first column
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static Task<IReadOnlyList<T?>> FetchListAsync<T>(this DbCommand cmd,
        CancellationToken cancellation = default)
    {
        return cmd.FetchListAsync(async reader =>
        {
            if (await reader.IsDBNullAsync(0, cancellation).ConfigureAwait(false))
            {
                return default;
            }

            return await reader.GetFieldValueAsync<T>(0, cancellation).ConfigureAwait(false);
        }, cancellation);
    }


    /// <summary>
    ///     Execute the command and return the value in the first column and row as type
    ///     T. If there is no data returned, this function will return default(T)
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static async Task<T?> FetchOne<T>(this DbCommand cmd, CancellationToken cancellation = default)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellation).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            if (await reader.IsDBNullAsync(0, cancellation).ConfigureAwait(false))
            {
                return default;
            }

            var result = await reader.GetFieldValueAsync<T>(0, cancellation).ConfigureAwait(false);
            return result;
        }

        return default;
    }

    /// <summary>
    ///     Open the attached connection, execute the command, and close the connection
    ///     in one call. This assumes that the connection is not already open
    /// </summary>
    /// <param name="command"></param>
    /// <param name="cancellation"></param>
    public static async Task ExecuteOnce(this DbCommand command, CancellationToken cancellation = default)
    {
        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        if (command.Connection is not { State: ConnectionState.Closed })
        {
            throw new InvalidOperationException(
                "The command must have an attached, but not yet open connection to use this extension method");
        }

        var conn = command.Connection;
        try
        {
            await conn.OpenAsync(cancellation).ConfigureAwait(false);

            await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Add a single parameter to a DbCommand with the supplied value and optional DbType
    /// </summary>
    /// <param name="command"></param>
    /// <param name="value"></param>
    /// <param name="dbType"></param>
    /// <returns></returns>
    public static DbParameter AddParameter(this DbCommand command, object value, DbType? dbType = null)
    {
        var name = "arg" + command.Parameters.Count;

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;

        if (dbType.HasValue)
        {
            parameter.DbType = dbType.Value;
        }

        command.Parameters.Add(parameter);

        return parameter;
    }

    /// <summary>
    ///     Find or add a single DbParameter with the supplied name. Will set the parameter value and DbType
    /// </summary>
    /// <param name="command"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public static DbParameter AddNamedParameter(this DbCommand command, string name, object value,
        DbType? type = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;

        if (type.HasValue)
        {
            parameter.DbType = type.Value;
        }

        command.Parameters.Add(parameter);

        return parameter;
    }

    public static T With<T>(this T command, string name, object value) where T : DbCommand
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);

        return command;
    }

    public static T With<T>(this T command, string name, object? value, DbType dbType) where T : DbCommand
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        parameter.DbType = dbType;
        command.Parameters.Add(parameter);

        return command;
    }

    public static T With<T>(this T command, string name, string value) where T : DbCommand
    {
        return command.With(name, value, DbType.String);
    }

    public static T With<T>(this T command, string name, int value) where T : DbCommand
    {
        return command.With(name, value, DbType.Int32);
    }

    public static T With<T>(this T command, string name, long value) where T : DbCommand
    {
        return command.With(name, value, DbType.Int64);
    }


    public static T With<T>(this T command, string name, Guid value) where T : DbCommand
    {
        return command.With(name, value, DbType.Guid);
    }

    public static T With<T>(this T command, string name, bool value) where T : DbCommand
    {
        return command.With(name, value, DbType.Boolean);
    }

    public static T With<T>(this T command, string name, byte[] value) where T : DbCommand
    {
        return command.With(name, value, DbType.Binary);
    }

    public static T With<T>(this T command, string name, DateTimeOffset? value) where T : DbCommand
    {
        return command.With(name, value, DbType.DateTimeOffset);
    }
}
