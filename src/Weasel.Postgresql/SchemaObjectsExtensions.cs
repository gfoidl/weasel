using System;
using System.IO;
using System.Threading.Tasks;
using Baseline;
using Npgsql;
using Weasel.Postgresql.Tables;

namespace Weasel.Postgresql
{
    public static class SchemaObjectsExtensions
    {
        internal static string ToIndexName(this DbObjectName name, string prefix, params string[] columnNames)
        {
            return $"{prefix}_{name.Name}_{columnNames.Join("_")}";
        }
        
        public static async Task<SchemaPatch> CreatePatch(this ISchemaObject schemaObject, NpgsqlConnection conn)
        {
            var patch = new SchemaPatch(new DdlRules());
            await patch.Apply(conn, AutoCreate.All, schemaObject);

            return patch;
        }

        public static async Task ApplyChanges(this ISchemaObject schemaObject, NpgsqlConnection conn)
        {
            var patch = new SchemaPatch(new DdlRules());
            await patch.Apply(conn, AutoCreate.CreateOrUpdate, schemaObject);

            await conn.CreateCommand(patch.UpdateDDL).ExecuteNonQueryAsync();
        }

        public static Task Drop(this ISchemaObject schemaObject, NpgsqlConnection conn)
        {
            var writer = new StringWriter();
            schemaObject.WriteDropStatement(new DdlRules(), writer);

            return conn.CreateCommand(writer.ToString()).ExecuteNonQueryAsync();
        }

        public static Task Create(this ISchemaObject schemaObject, NpgsqlConnection conn)
        {
            var writer = new StringWriter();
            schemaObject.WriteCreateStatement(new DdlRules(), writer);
            
            return conn.CreateCommand(writer.ToString()).ExecuteNonQueryAsync();
        }
        
    }
}