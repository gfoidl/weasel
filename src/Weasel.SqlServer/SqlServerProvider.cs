using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using Baseline;
using Baseline.ImTools;
using Weasel.Core;

#nullable enable
namespace Weasel.SqlServer
{
    public class SqlServerProvider : DatabaseProvider<SqlCommand, SqlParameter, SqlConnection, SqlTransaction,
        System.Data.SqlDbType, SqlDataReader>
    {
        public static readonly SqlServerProvider Instance = new();

        private SqlServerProvider()
        {
            store<bool>(System.Data.SqlDbType.Bit, "bit");
            store<long>(System.Data.SqlDbType.BigInt, "bigint");
            store<byte[]>(System.Data.SqlDbType.Binary, "binary");
            store<DateTime>(System.Data.SqlDbType.Date, "datetime");
            store<DateTimeOffset>(System.Data.SqlDbType.DateTimeOffset, "datetimeoffset");
            store<decimal>(System.Data.SqlDbType.Decimal, "decimal");
            store<double>(System.Data.SqlDbType.Float, "double");
            store<int>(System.Data.SqlDbType.Int, "int");
            store<TimeSpan>(System.Data.SqlDbType.Time, "time");
        }

        public List<Type> ContainmentOperatorTypes { get; } = new();
        public List<Type> TimespanTypes { get; } = new();
        public List<Type> TimespanZTypes { get; } = new();


        // Lazily retrieve the CLR type to SqlDbType and PgTypeName mapping from exposed ISqlTypeMapper.Mappings.
        // This is lazily calculated instead of precached because it allows consuming code to register
        // custom Sql mappings prior to execution.
        private string? ResolveDatabaseType(Type type)
        {
            if (DatabaseTypeMemo.Value.TryFind(type, out var value))
            {
                return value;
            }

            if (determineParameterType(type, out var dbType))
            {
                ParameterTypeMemo.Swap(d => d.AddOrUpdate(type, dbType));
            }

            DatabaseTypeMemo.Swap(d => d.AddOrUpdate(type, value));

            return value;
        }

        private System.Data.SqlDbType? ResolveSqlDbType(Type type)
        {
            if (ParameterTypeMemo.Value.TryFind(type, out var value))
            {
                return value;
            }

            if (determineParameterType(type, out var dbType))
            {
                ParameterTypeMemo.Swap(d => d.AddOrUpdate(type, dbType));
            }

            return System.Data.SqlDbType.Variant;
        }


        protected override Type[] determineClrTypesForParameterType(System.Data.SqlDbType dbType)
        {
            return new Type[0];
        }


        public string ConvertSynonyms(string type)
        {
            switch (type.ToLower())
            {
                case "text":
                case "varchar":
                    return "varchar";

                case "boolean":
                case "bool":
                    return "bit";

                case "integer":
                    return "int";

            }

            return type;
        }


        protected override bool determineParameterType(Type type, out System.Data.SqlDbType dbType)
        {
            var SqlDbType = ResolveSqlDbType(type);
            if (SqlDbType != null)
            {
                {
                    dbType = SqlDbType.Value;
                    return true;
                }
            }

            if (type.IsNullable())
            {
                dbType = ToParameterType(type.GetInnerTypeFromNullable());
                return true;
            }

            if (type.IsEnum)
            {
                dbType = System.Data.SqlDbType.Int;
                return true;
            }

            if (type.IsArray)
            {
                throw new NotSupportedException("Sql Server does not support arrays");
            }

            if (type == typeof(DBNull))
            {
                dbType = System.Data.SqlDbType.Variant;
                return true;
            }

            dbType = System.Data.SqlDbType.Variant;
            return false;
        }

        public override string GetDatabaseType(Type memberType, EnumStorage enumStyle)
        {
            if (memberType.IsEnum)
            {
                return enumStyle == EnumStorage.AsInteger ? "integer" : "varchar";
            }

            if (memberType.IsArray)
            {
                return GetDatabaseType(memberType.GetElementType()!, enumStyle) + "[]";
            }

            if (memberType.IsNullable())
            {
                return GetDatabaseType(memberType.GetInnerTypeFromNullable(), enumStyle);
            }

            if (memberType.IsConstructedGenericType)
            {
                var templateType = memberType.GetGenericTypeDefinition();
                return ResolveDatabaseType(templateType) ?? "jsonb";
            }

            return ResolveDatabaseType(memberType) ?? "jsonb";
        }

        public override void AddParameter(SqlCommand command, SqlParameter parameter)
        {
            command.Parameters.Add(parameter);
        }

        public override void SetParameterType(SqlParameter parameter, System.Data.SqlDbType dbType)
        {
            parameter.SqlDbType = dbType;
        }

        public bool HasTypeMapping(Type memberType)
        {
            if (memberType.IsNullable())
            {
                return HasTypeMapping(memberType.GetInnerTypeFromNullable());
            }

            // more complicated later
            return ResolveDatabaseType(memberType) != null || memberType.IsEnum;
        }

        private Type GetNullableType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsValueType)
            {
                return typeof(Nullable<>).MakeGenericType(type);
            }

            return type;
        }

    }
}