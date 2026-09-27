using Domain.Enums;
using Domain.Models.DynamicData;

namespace Data.Sql.DynamicData;

public sealed class PostgresqlCatalogReader : SqlCatalogReader
{
    public override string ColumnsSql =>
        """
        SELECT c.table_schema, c.table_name, c.column_name, c.ordinal_position,
               CASE WHEN c.is_nullable = 'YES' THEN 1 ELSE 0 END,
               c.data_type, c.numeric_precision, c.numeric_scale, c.character_maximum_length,
               NULL, c.udt_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE t.table_type IN ('BASE TABLE', 'VIEW')
          AND c.table_schema NOT IN ('pg_catalog', 'information_schema')
          AND c.table_schema NOT LIKE 'pg_toast%'
          AND c.table_schema NOT LIKE 'pg_temp%'
        ORDER BY c.table_schema, c.table_name, c.ordinal_position
        """;

    public override string PrimaryKeysSql =>
        """
        SELECT ns.nspname, cl.relname, att.attname, k.ord
        FROM pg_constraint con
        JOIN pg_class cl ON cl.oid = con.conrelid
        JOIN pg_namespace ns ON ns.oid = cl.relnamespace
        CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum
        WHERE con.contype = 'p'
        """;

    public override string ForeignKeysSql =>
        """
        SELECT con.conname, ns.nspname, cl.relname, att.attname, k.ord,
               fns.nspname, fcl.relname, fatt.attname
        FROM pg_constraint con
        JOIN pg_class cl ON cl.oid = con.conrelid
        JOIN pg_namespace ns ON ns.oid = cl.relnamespace
        JOIN pg_class fcl ON fcl.oid = con.confrelid
        JOIN pg_namespace fns ON fns.oid = fcl.relnamespace
        CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(attnum, fattnum, ord)
        JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.attnum
        JOIN pg_attribute fatt ON fatt.attrelid = con.confrelid AND fatt.attnum = k.fattnum
        WHERE con.contype = 'f'
        """;

    public override FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var native = type.Lowered;

        var kind = native switch
        {
            "smallint" or "integer" => FieldKind.Integer32,
            "bigint" => FieldKind.Integer64,
            "numeric" or "decimal" or "money" => FieldKind.Fixed,
            "real" or "double precision" => FieldKind.Floating,
            "boolean" => FieldKind.Flag,
            "character varying" or "character" or "text" or "name" => FieldKind.Text,
            "uuid" => FieldKind.Uuid,
            "date" => FieldKind.Date,
            "time without time zone" => FieldKind.Time,
            "timestamp without time zone" => FieldKind.Timestamp,
            "timestamp with time zone" => FieldKind.TimestampOffset,
            "json" or "jsonb" => FieldKind.Json,
            "bytea" => FieldKind.Binary,
            "user-defined" when string.Equals(type.Extra, "citext", StringComparison.OrdinalIgnoreCase) => FieldKind.Text,
            _ => FieldKind.Unknown,
        };

        return Field(name, kind, isNullable, native == "user-defined" ? type.Extra ?? native : native);
    }
}

public sealed class SqlServerCatalogReader : SqlCatalogReader
{
    public override string ColumnsSql =>
        """
        SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
               CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END,
               c.DATA_TYPE, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.CHARACTER_MAXIMUM_LENGTH,
               NULL, NULL
        FROM INFORMATION_SCHEMA.COLUMNS c
        JOIN INFORMATION_SCHEMA.TABLES t
          ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
          AND c.TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;

    public override string PrimaryKeysSql =>
        """
        SELECT SCHEMA_NAME(t.schema_id), t.name, c.name, ic.key_ordinal
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        JOIN sys.tables t ON t.object_id = i.object_id
        WHERE i.is_primary_key = 1
        """;

    public override string ForeignKeysSql =>
        """
        SELECT fk.name, SCHEMA_NAME(pt.schema_id), pt.name, pc.name, fkc.constraint_column_id,
               SCHEMA_NAME(rt.schema_id), rt.name, rc.name
        FROM sys.foreign_key_columns fkc
        JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
        JOIN sys.tables pt ON pt.object_id = fkc.parent_object_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        """;

    public override FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var native = type.Lowered;

        var (kind, isLongText) = native switch
        {
            "bit" => (FieldKind.Flag, false),
            "tinyint" or "smallint" or "int" => (FieldKind.Integer32, false),
            "bigint" => (FieldKind.Integer64, false),
            "decimal" or "numeric" or "money" or "smallmoney" => (FieldKind.Fixed, false),
            "float" or "real" => (FieldKind.Floating, false),
            "char" or "varchar" or "nchar" or "nvarchar" => (FieldKind.Text, false),
            "text" or "ntext" => (FieldKind.Text, true),
            "uniqueidentifier" => (FieldKind.Uuid, false),
            "date" => (FieldKind.Date, false),
            "time" => (FieldKind.Time, false),
            "datetime" or "datetime2" or "smalldatetime" => (FieldKind.Timestamp, false),
            "datetimeoffset" => (FieldKind.TimestampOffset, false),
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => (FieldKind.Binary, false),
            _ => (FieldKind.Unknown, false),
        };

        return Field(name, kind, isNullable, native, isLongText);
    }
}

public sealed class MysqlCatalogReader : SqlCatalogReader
{
    public override string ColumnsSql =>
        """
        SELECT NULL, c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
               CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END,
               c.DATA_TYPE, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.CHARACTER_MAXIMUM_LENGTH,
               NULL, c.COLUMN_TYPE
        FROM information_schema.COLUMNS c
        JOIN information_schema.TABLES t
          ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE c.TABLE_SCHEMA = DATABASE()
          AND t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
        ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
        """;

    public override string PrimaryKeysSql =>
        """
        SELECT NULL, TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION
        FROM information_schema.KEY_COLUMN_USAGE
        WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY'
        """;

    public override string ForeignKeysSql =>
        """
        SELECT CONSTRAINT_NAME, NULL, TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION,
               NULL, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
        FROM information_schema.KEY_COLUMN_USAGE
        WHERE TABLE_SCHEMA = DATABASE()
          AND REFERENCED_TABLE_NAME IS NOT NULL
          AND REFERENCED_TABLE_SCHEMA = DATABASE()
        """;

    public override FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var native = type.Lowered;
        var columnType = type.Extra?.ToLowerInvariant() ?? native;
        var unsigned = columnType.Contains("unsigned", StringComparison.Ordinal);

        var kind = native switch
        {
            "tinyint" when columnType.StartsWith("tinyint(1)", StringComparison.Ordinal) => FieldKind.Flag,
            "bit" when columnType == "bit(1)" => FieldKind.Flag,
            "tinyint" or "smallint" or "mediumint" or "year" => FieldKind.Integer32,
            "int" or "integer" => unsigned ? FieldKind.Integer64 : FieldKind.Integer32,
            "bigint" => unsigned ? FieldKind.Fixed : FieldKind.Integer64,
            "decimal" or "numeric" => FieldKind.Fixed,
            "float" or "double" or "real" => FieldKind.Floating,
            "char" when type.Length == 36 => FieldKind.Uuid,
            "char" or "varchar" or "tinytext" or "text" or "mediumtext" or "longtext" or "enum" or "set" => FieldKind.Text,
            "date" => FieldKind.Date,
            "time" => FieldKind.Time,
            "datetime" or "timestamp" => FieldKind.Timestamp,
            "json" => FieldKind.Json,
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => FieldKind.Binary,
            _ => FieldKind.Unknown,
        };

        return Field(name, kind, isNullable, columnType);
    }
}

public sealed class OracleCatalogReader : SqlCatalogReader
{
    private const string CurrentSchema = "SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')";

    public override string ColumnsSql =>
        $"""
        SELECT c.OWNER, c.TABLE_NAME, c.COLUMN_NAME, c.COLUMN_ID,
               CASE WHEN c.NULLABLE = 'Y' THEN 1 ELSE 0 END,
               c.DATA_TYPE, c.DATA_PRECISION, c.DATA_SCALE, c.DATA_LENGTH,
               NULL, NULL
        FROM ALL_TAB_COLUMNS c
        JOIN ALL_OBJECTS o
          ON o.OWNER = c.OWNER AND o.OBJECT_NAME = c.TABLE_NAME AND o.OBJECT_TYPE IN ('TABLE', 'VIEW')
        WHERE c.OWNER = {CurrentSchema}
        ORDER BY c.TABLE_NAME, c.COLUMN_ID
        """;

    public override string PrimaryKeysSql =>
        $"""
        SELECT cc.OWNER, cc.TABLE_NAME, cc.COLUMN_NAME, cc.POSITION
        FROM ALL_CONSTRAINTS c
        JOIN ALL_CONS_COLUMNS cc ON cc.OWNER = c.OWNER AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
        WHERE c.CONSTRAINT_TYPE = 'P' AND c.OWNER = {CurrentSchema}
        """;

    public override string ForeignKeysSql =>
        $"""
        SELECT c.CONSTRAINT_NAME, cc.OWNER, cc.TABLE_NAME, cc.COLUMN_NAME, cc.POSITION,
               rc.OWNER, rc.TABLE_NAME, rc.COLUMN_NAME
        FROM ALL_CONSTRAINTS c
        JOIN ALL_CONS_COLUMNS cc ON cc.OWNER = c.OWNER AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
        JOIN ALL_CONS_COLUMNS rc
          ON rc.OWNER = c.R_OWNER AND rc.CONSTRAINT_NAME = c.R_CONSTRAINT_NAME AND rc.POSITION = cc.POSITION
        WHERE c.CONSTRAINT_TYPE = 'R' AND c.OWNER = {CurrentSchema}
        """;

    public override FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var native = type.DataType.ToUpperInvariant();

        var (kind, isLongText) = native switch
        {
            "NUMBER" when type.Scale is 0 && type.Precision is > 0 and <= 9 => (FieldKind.Integer32, false),
            "NUMBER" when type.Scale is 0 && type.Precision is > 9 and <= 18 => (FieldKind.Integer64, false),
            "NUMBER" => (FieldKind.Fixed, false),
            "FLOAT" or "BINARY_FLOAT" or "BINARY_DOUBLE" => (FieldKind.Floating, false),
            "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" => (FieldKind.Text, false),
            "CLOB" or "NCLOB" => (FieldKind.Text, true),
            "DATE" => (FieldKind.Timestamp, false),
            "RAW" when type.Length == 16 => (FieldKind.Uuid, false),
            "RAW" or "BLOB" => (FieldKind.Binary, false),
            "BOOLEAN" => (FieldKind.Flag, false),
            "JSON" => (FieldKind.Json, false),
            _ when native.StartsWith("TIMESTAMP", StringComparison.Ordinal)
                   && native.EndsWith("WITH TIME ZONE", StringComparison.Ordinal)
                   && !native.Contains("LOCAL", StringComparison.Ordinal) => (FieldKind.TimestampOffset, false),
            _ when native.StartsWith("TIMESTAMP", StringComparison.Ordinal) => (FieldKind.Timestamp, false),
            _ => (FieldKind.Unknown, false),
        };

        return Field(name, kind, isNullable, type.DataType, isLongText);
    }
}

public sealed class FirebirdCatalogReader : SqlCatalogReader
{
    private const string OctetsCharacterSet = "1";

    public override string ColumnsSql =>
        """
        SELECT CAST(NULL AS VARCHAR(63)), TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), rf.RDB$FIELD_POSITION,
               CASE WHEN COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0) = 1 THEN 0 ELSE 1 END,
               CAST(f.RDB$FIELD_TYPE AS VARCHAR(10)), f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$FIELD_LENGTH,
               f.RDB$FIELD_SUB_TYPE, CAST(f.RDB$CHARACTER_SET_ID AS VARCHAR(10))
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
        JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
        WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_POSITION
        """;

    public override string PrimaryKeysSql =>
        """
        SELECT CAST(NULL AS VARCHAR(63)), TRIM(rc.RDB$RELATION_NAME), TRIM(s.RDB$FIELD_NAME), s.RDB$FIELD_POSITION
        FROM RDB$RELATION_CONSTRAINTS rc
        JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
        WHERE rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'
        """;

    public override string ForeignKeysSql =>
        """
        SELECT TRIM(rc.RDB$CONSTRAINT_NAME), CAST(NULL AS VARCHAR(63)), TRIM(rc.RDB$RELATION_NAME),
               TRIM(s.RDB$FIELD_NAME), s.RDB$FIELD_POSITION,
               CAST(NULL AS VARCHAR(63)), TRIM(pk.RDB$RELATION_NAME), TRIM(ps.RDB$FIELD_NAME)
        FROM RDB$RELATION_CONSTRAINTS rc
        JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
        JOIN RDB$RELATION_CONSTRAINTS pk ON pk.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ
        JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
        JOIN RDB$INDEX_SEGMENTS ps
          ON ps.RDB$INDEX_NAME = pk.RDB$INDEX_NAME AND ps.RDB$FIELD_POSITION = s.RDB$FIELD_POSITION
        WHERE rc.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
        """;

    public override FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var scaled = type.Scale is < 0;
        var octets = type.Extra == OctetsCharacterSet;

        var (kind, isLongText, native) = type.DataType switch
        {
            "7" => (scaled ? FieldKind.Fixed : FieldKind.Integer32, false, "SMALLINT"),
            "8" => (scaled ? FieldKind.Fixed : FieldKind.Integer32, false, "INTEGER"),
            "16" => (scaled || type.SubType is 1 or 2 ? FieldKind.Fixed : FieldKind.Integer64, false, "BIGINT"),
            "26" or "24" or "25" => (FieldKind.Fixed, false, "INT128/DECFLOAT"),
            "10" or "27" => (FieldKind.Floating, false, "DOUBLE PRECISION"),
            "12" => (FieldKind.Date, false, "DATE"),
            "13" => (FieldKind.Time, false, "TIME"),
            "35" => (FieldKind.Timestamp, false, "TIMESTAMP"),
            "29" => (FieldKind.TimestampOffset, false, "TIMESTAMP WITH TIME ZONE"),
            "23" => (FieldKind.Flag, false, "BOOLEAN"),
            "14" when octets && type.Length == 16 => (FieldKind.Uuid, false, "CHAR(16) OCTETS"),
            "14" or "37" when octets => (FieldKind.Binary, false, "OCTETS"),
            "14" => (FieldKind.Text, false, "CHAR"),
            "37" => (FieldKind.Text, false, "VARCHAR"),
            "261" when type.SubType == 1 => (FieldKind.Text, true, "BLOB SUB_TYPE TEXT"),
            "261" => (FieldKind.Binary, false, "BLOB"),
            _ => (FieldKind.Unknown, false, type.DataType),
        };

        return Field(name, kind, isNullable, native, isLongText);
    }
}

public sealed class SqliteCatalogReader : SqlCatalogReader
{
    public override string ColumnsSql =>
        """
        SELECT NULL, m.name, p.name, p.cid,
               CASE WHEN p."notnull" = 1 OR p.pk > 0 THEN 0 ELSE 1 END,
               p.type, NULL, NULL, NULL, NULL, NULL
        FROM sqlite_master m
        JOIN pragma_table_info(m.name) p
        WHERE m.type IN ('table', 'view') AND m.name NOT LIKE 'sqlite!_%' ESCAPE '!'
        ORDER BY m.name, p.cid
        """;

    public override string PrimaryKeysSql =>
        """
        SELECT NULL, m.name, p.name, p.pk
        FROM sqlite_master m
        JOIN pragma_table_info(m.name) p
        WHERE m.type = 'table' AND p.pk > 0
        """;

    public override string ForeignKeysSql =>
        """
        SELECT m.name || '_fk_' || f.id, NULL, m.name, f."from", f.seq, NULL, f."table", f."to"
        FROM sqlite_master m
        JOIN pragma_foreign_key_list(m.name) f
        WHERE m.type = 'table'
        """;

    public override FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var native = type.DataType.ToUpperInvariant();

        var kind = native switch
        {
            "" => FieldKind.Unknown,
            _ when native.Contains("INT", StringComparison.Ordinal) => FieldKind.Integer64,
            _ when native.Contains("CHAR", StringComparison.Ordinal)
                   || native.Contains("CLOB", StringComparison.Ordinal)
                   || native.Contains("TEXT", StringComparison.Ordinal) => FieldKind.Text,
            _ when native.Contains("BLOB", StringComparison.Ordinal) => FieldKind.Binary,
            _ when native.Contains("REAL", StringComparison.Ordinal)
                   || native.Contains("FLOA", StringComparison.Ordinal)
                   || native.Contains("DOUB", StringComparison.Ordinal) => FieldKind.Floating,
            _ when native.Contains("BOOL", StringComparison.Ordinal) => FieldKind.Flag,
            _ when native.Contains("DEC", StringComparison.Ordinal)
                   || native.Contains("NUM", StringComparison.Ordinal) => FieldKind.Fixed,
            _ => FieldKind.Text,
        };

        return Field(name, kind, isNullable, type.DataType);
    }
}
