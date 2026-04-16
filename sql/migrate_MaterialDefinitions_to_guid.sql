-- ============================================================
-- MaterialDefinitions: INT id → GUID id migrasyonu
-- + Legacy ERP kolonlarini kaldir
--
-- ONCE YEDEK ALIN. Geri alinamaz.
-- ============================================================

DECLARE @schema NVARCHAR(128) = N'dbo'; -- gerekirse degistirin
DECLARE @table  NVARCHAR(256) = QUOTENAME(@schema) + N'.[MaterialDefinitions]';

-- Zaten GUID ise hic bir sey yapma
IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types  t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(@table)
      AND c.name      = N'id'
      AND t.name      = N'int'
)
BEGIN
    PRINT N'id kolonu zaten GUID, migrasyon gerekmez.';
    RETURN;
END

PRINT N'INT → GUID migrasyonu basliyor...';

-- 1. Yeni GUID kolonu ekle
ALTER TABLE [dbo].[MaterialDefinitions] ADD [id_guid] UNIQUEIDENTIFIER NULL;

-- 2. Mevcut satirlara CalibraHub'in kodlama formatiyla GUID ata
--    C# karsiligi: "00000000-0000-0000-0000-0000" + id.ToString("X8")
--    Ornek: id=1  -> 00000000-0000-0000-0000-000000000001
--           id=255-> 00000000-0000-0000-0000-0000000000FF
UPDATE [dbo].[MaterialDefinitions]
SET [id_guid] = CAST(
    N'00000000-0000-0000-0000-0000'
    + RIGHT(N'00000000' + UPPER(CONVERT(NVARCHAR(8), CONVERT(VARBINARY(4), [id]), 2)), 8)
    AS UNIQUEIDENTIFIER
);

-- 3. NOT NULL yap
ALTER TABLE [dbo].[MaterialDefinitions] ALTER COLUMN [id_guid] UNIQUEIDENTIFIER NOT NULL;

-- 4. Mevcut PK kısıtını bul ve kaldir
DECLARE @pkName NVARCHAR(256);
SELECT @pkName = kc.name
FROM sys.key_constraints kc
JOIN sys.tables t ON t.object_id = kc.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE kc.type = N'PK'
  AND t.name   = N'MaterialDefinitions'
  AND s.name   = @schema;

IF @pkName IS NOT NULL
    EXEC(N'ALTER TABLE [' + @schema + N'].[MaterialDefinitions] DROP CONSTRAINT [' + @pkName + N'];');

-- 5. Eski INT id kolonunu kaldir
ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [id];

-- 6. id_guid'i id olarak yeniden adlandir
EXEC sp_rename N'[dbo].[MaterialDefinitions].[id_guid]', N'id', N'COLUMN';

-- 7. Yeni PK ekle
ALTER TABLE [dbo].[MaterialDefinitions]
    ADD CONSTRAINT [pk_MaterialDefinitions_id] PRIMARY KEY ([id]);

-- 8. Legacy ERP kolonlarini kaldir (varsa)
DECLARE @sql NVARCHAR(MAX) = N'';

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'IsActive')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [IsActive];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'MaterialCode')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [MaterialCode];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'MaterialName')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [MaterialName];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'MaterialDescription')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [MaterialDescription];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'BaseUnitId')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [BaseUnitId];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'HasSerialNo')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [HasSerialNo];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'HasLotNo')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [HasLotNo];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'CreatedDate')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [CreatedDate];' + CHAR(13);
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]') AND name = N'ModifiedDate')
    SET @sql += N'ALTER TABLE [dbo].[MaterialDefinitions] DROP COLUMN [ModifiedDate];' + CHAR(13);

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;

-- 9. Unique index (yoksa)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[MaterialDefinitions]')
      AND name      = N'ux_MaterialDefinitions_stock_code'
)
    CREATE UNIQUE INDEX [ux_MaterialDefinitions_stock_code]
        ON [dbo].[MaterialDefinitions]([stock_code]);

PRINT N'Migrasyon tamamlandi.';
PRINT N'Son tablo kolonlari: id (GUID), stock_code, stock_name, unit_name, is_active, created_at, updated_at, created_by, updated_by';
