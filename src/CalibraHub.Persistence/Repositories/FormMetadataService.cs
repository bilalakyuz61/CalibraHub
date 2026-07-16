using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// IFormMetadataService implementasyonu. Mevcut Forms + WidgetMas + v_Flat_* altyapisini
/// salt-okunur sekilde tuketir. Per-company DB (SqlServerConnectionFactory).
/// </summary>
public sealed class FormMetadataService : IFormMetadataService
{
    // Identifier guvenligi — v_Flat_{FormCode} dinamik SQL'inde FormCode'u validate ederiz
    private static readonly Regex FormCodeRegex =
        new("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public FormMetadataService(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    public async Task<IReadOnlyList<IntegrationFormDto>> ListFormsAsync(CancellationToken ct)
    {
        var list = new List<IntegrationFormDto>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        // NOT EXISTS: bu form başka bir form'un LinesFormCode'unda referans ediliyorsa
        // entegrasyon picker'ında gosterilmez — kalem formlari "kok form" degil, alt
        // bilesendir (Step 1'de seciliyse sistem otomatik kesfedecek). Ornek:
        // SALES_ORDER_LINES, SALES_ORDER_NEW.LinesFormCode olarak referans edildiginden
        // bu liste'den filtrelenir.
        await using var cmd = conn.CreateCommand();
        // Filtreler:
        //   1) Aktif formlar (IsActive=1)
        //   2) Kalem (lines) formlari gizle — baska bir form'un LinesFormCode'unda referans varsa
        //   3) "Yeni" formlari gizle (_NEW suffixli) — entegrasyon mevcut kayit uzerinde calisir,
        //      henuz yaratilmamis kayit icin entegrasyon olamaz. _EDIT veya tek formCode tercih edilir.
        //   4) Liste formlari gizle — BaseTable bos olan formlar genelde board/list view'lardir,
        //      kayit formu degil, entegrasyon hedefi olamaz.
        //   Sonuc: Wizard Step 1'de sadece KAYIT formlari (genelde _EDIT) gosterilir.
        cmd.CommandText = $"""
            SELECT f.[Id], f.[FormCode], f.[FormName], f.[Module], f.[SubModule],
                   f.[BaseTable], f.[BaseRecordKey], f.[LinesFormCode], f.[LinesParentColumn],
                   f.[ListUrl], f.[NewUrl], f.[EditUrl], f.[Icon], f.[IconColor], f.[IsTransferable]
            FROM [{_schema}].[Forms] f
            WHERE f.[IsActive] = 1
              AND NOT EXISTS (
                  SELECT 1 FROM [{_schema}].[Forms] p
                  WHERE p.[LinesFormCode] = f.[FormCode]
                    AND p.[IsActive] = 1)
              AND f.[FormCode] NOT LIKE '%[_]NEW'      -- yeni-form variant'larini gizle
              AND f.[BaseTable] IS NOT NULL            -- liste formlari (BaseTable bos) gizle
              AND f.[BaseTable] <> ''
              AND ISNULL(f.[IsTransferable], 1) = 1    -- entegrasyon kapali formlar gizle (Faz N)
            ORDER BY f.[Module], f.[SubModule], f.[SortOrder], f.[FormCode];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapForm(reader));
        }
        return list;
    }

    public async Task<IntegrationFormDto?> GetFormAsync(string formCode, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 1 [Id], [FormCode], [FormName], [Module], [SubModule], [BaseTable], [BaseRecordKey],
                         [LinesFormCode], [LinesParentColumn],
                         [ListUrl], [NewUrl], [EditUrl], [Icon], [IconColor], [IsTransferable]
            FROM [{_schema}].[Forms]
            WHERE [FormCode] = @FormCode;
            """;
        cmd.Parameters.Add(new SqlParameter("@FormCode", formCode));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapForm(reader);
    }

    private static IntegrationFormDto MapForm(SqlDataReader r) => new(
        Id:                r.GetInt32(0),
        FormCode:          r.GetString(1),
        FormName:          r.GetString(2),
        Module:            r.GetString(3),
        SubModule:         r.IsDBNull(4) ? null : r.GetString(4),
        BaseTable:         r.IsDBNull(5) ? null : r.GetString(5),
        BaseRecordKey:     r.IsDBNull(6) ? null : r.GetString(6),
        LinesFormCode:     r.IsDBNull(7) ? null : r.GetString(7),
        LinesParentColumn: r.IsDBNull(8) ? null : r.GetString(8),
        // Faz N — Forms metadata hub kolonlari (eski schema icin SafeRead pattern)
        ListUrl:           SafeStr(r, "ListUrl"),
        NewUrl:            SafeStr(r, "NewUrl"),
        EditUrl:           SafeStr(r, "EditUrl"),
        Icon:              SafeStr(r, "Icon"),
        IconColor:         SafeStr(r, "IconColor"),
        IsTransferable:    SafeBool(r, "IsTransferable", defaultValue: true));

    private static string? SafeStr(SqlDataReader r, string col)
    {
        try
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? null : r.GetString(ord);
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    private static bool SafeBool(SqlDataReader r, string col, bool defaultValue)
    {
        try
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? defaultValue : r.GetBoolean(ord);
        }
        catch (IndexOutOfRangeException) { return defaultValue; }
    }

    /// <summary>
    /// Master-Detail destekli 3 katmanli field listesi:
    ///   1. Header — formun kendi alanları (BaseTable kolonlari + WidgetMas) → Section="Header"
    ///   2. Lines  — form.LinesFormCode doluysa kalem form'unun alanları → Section="Lines"
    ///   3. Combination — kalem form varsa "Code" sabit field (runtime resolver) → Section="Combination"
    ///
    /// Wizard Step 3'te source field grupları oluşturmak için kullanılır.
    /// LinesFormCode NULL ise sadece Header katmanı doner (geriye uyum: tek-seviyeli formlar).
    ///
    /// **Iki kaynak birlestirilir:**
    ///   - WidgetMas: kullanici/admin tanimlamis widget alanlari (IsPlainField mix)
    ///   - INFORMATION_SCHEMA.COLUMNS: BaseTable'in fiziksel kolonlari (sistem alanlari)
    /// Iki kaynaktan ayni Code geldiginde WidgetMas oncelik kazanir (dedup by Code).
    /// </summary>
    public async Task<IReadOnlyList<IntegrationFormFieldDto>> GetFieldsAsync(string formCode, CancellationToken ct)
    {
        // 1. Header form metadata + alanları
        var form = await GetFormAsync(formCode, ct);
        if (form is null) return Array.Empty<IntegrationFormFieldDto>();

        var list = new List<IntegrationFormFieldDto>();
        await FillFieldsAsync(list, form, "Header", ct);

        // 2. Kalem form alanları (varsa)
        if (!string.IsNullOrWhiteSpace(form.LinesFormCode))
        {
            var linesForm = await GetFormAsync(form.LinesFormCode, ct);
            if (linesForm is not null)
                await FillFieldsAsync(list, linesForm, "Lines", ct);

            // 3. Combination — runtime resolver field (DocumentLine.CombinationId → ItemConfiguration.Code)
            //    Kullanici Step 3'te `Combination.Code` source ile mapping yapabilir.
            list.Add(new IntegrationFormFieldDto(
                Code:         "Code",
                Label:        "Kombinasyon Kodu",
                DataType:     "string",
                IsRequired:   false,
                IsPlainField: false,    // Sentetik field (runtime resolver, gerçek tabloda yok)
                GroupKey:     null,
                Section:      "Combination"));
        }

        return list;
    }

    private async Task FillFieldsAsync(
        List<IntegrationFormFieldDto> sink, IntegrationFormDto form, string section, CancellationToken ct)
    {
        // Dedup by Code: WidgetMas oncelik (admin tanimi); INFORMATION_SCHEMA fallback (sistem kolonu)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // ── A) WidgetMas: kullanici/admin tanimi widget'lar ──
        await using (var widgetCmd = conn.CreateCommand())
        {
            widgetCmd.CommandText = $"""
                SELECT w.[WidgetCode], w.[Label], w.[DataType], w.[IsRequired], w.[IsPlainField]
                FROM [{_schema}].[WidgetMas] w
                INNER JOIN [{_schema}].[Forms] f ON f.[Id] = w.[FormId]
                WHERE f.[FormCode] = @FormCode
                  AND w.[IsActive] = 1
                  AND w.[DataType] NOT IN ('group', 'grid')
                ORDER BY w.[SortOrder], w.[Id];
                """;
            widgetCmd.Parameters.Add(new SqlParameter("@FormCode", form.FormCode));
            await using var rdr = await widgetCmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var code = rdr.GetString(0);
                if (!seen.Add(code)) continue;
                sink.Add(new IntegrationFormFieldDto(
                    Code:         code,
                    Label:        rdr.GetString(1),
                    DataType:     rdr.GetString(2),
                    IsRequired:   rdr.GetBoolean(3),
                    IsPlainField: rdr.GetBoolean(4),
                    GroupKey:     null,
                    Section:      section));
            }
        }

        // ── B) INFORMATION_SCHEMA: BaseTable fiziksel kolonlari (sistem alanlari) ──
        // BaseTable formati "dbo.Document" veya "Document" — schema/table parse et.
        if (string.IsNullOrWhiteSpace(form.BaseTable)) return;

        var (tblSchema, tblName) = ParseBaseTable(form.BaseTable, _schema);
        if (string.IsNullOrWhiteSpace(tblName)) return;

        // Base tablonun fiziksel kolon adlari — FK cozumleme (C bolumu) icin gerekli.
        var baseColNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var colCmd = conn.CreateCommand())
        {
            // Sistem audit kolonlari (Created/Updated) entegrasyonda ihtimaller az; gosterilebilir.
            colCmd.CommandText = """
                SELECT [COLUMN_NAME], [DATA_TYPE], [IS_NULLABLE]
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE [TABLE_SCHEMA] = @Sch AND [TABLE_NAME] = @Tbl
                ORDER BY [ORDINAL_POSITION];
                """;
            colCmd.Parameters.Add(new SqlParameter("@Sch", tblSchema));
            colCmd.Parameters.Add(new SqlParameter("@Tbl", tblName));
            await using var rdr = await colCmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var code = rdr.GetString(0);
                baseColNames.Add(code);            // fiziksel kolon envanteri (FK cozumleme icin)
                if (!seen.Add(code)) continue;     // Widget oncelikli
                var sqlType  = rdr.GetString(1);
                var nullable = rdr.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase);
                sink.Add(new IntegrationFormFieldDto(
                    Code:         code,
                    Label:        HumanizeColumnName(code),   // PascalCase → "Grand Total" + Tr sozluk
                    DataType:     MapSqlType(sqlType),
                    IsRequired:   !nullable,
                    IsPlainField: true,            // Tablo kolonu
                    GroupKey:     null,
                    Section:      section));
            }
        }

        // ── C) FK cozulmus Kod/Ad alanlari (v_Flat_* icinde LEFT JOIN ile uretilir) ──
        // Base tabloda FK kolonu (ContactId/ItemId...) varsa, flat view'daki cozulmus
        // {Prefix}Code/{Prefix}Name kolonlarini SENTETIK secilebilir alan olarak yayinla.
        // Boylece kullanici entegrasyon eslemesinde ham FK Id yerine cari/stok KODUNU
        // dogrudan secebilir (bugunku kirilgan Lookup'a gerek kalmaz). Runtime zaten
        // SELECT * FROM v_Flat_* okudugu icin secilen alanin degeri akar.
        // Kaynak: FlatViewFkResolver.Map (flat view ureticileriyle TEK kaynak).
        foreach (var fk in FlatViewFkResolver.Map)
        {
            if (!baseColNames.Contains(fk.FkColumn)) continue;
            foreach (var syntheticCode in new[] { fk.OutPrefix + "Code", fk.OutPrefix + "Name" })
            {
                if (!seen.Add(syntheticCode)) continue;   // base kolon/widget zaten sagliyorsa atla
                sink.Add(new IntegrationFormFieldDto(
                    Code:         syntheticCode,
                    Label:        HumanizeColumnName(syntheticCode),
                    DataType:     "string",
                    IsRequired:   false,
                    IsPlainField: false,           // Sentetik — base tabloda yok, view'da cozulur
                    GroupKey:     null,
                    Section:      section));
            }
        }
    }

    /// <summary>
    /// "dbo.Document" -> ("dbo", "Document"). Schema yoksa default _schema.
    /// </summary>
    private static (string Schema, string Table) ParseBaseTable(string baseTable, string defaultSchema)
    {
        var s = baseTable.Trim().Replace("[", "").Replace("]", "");
        var dot = s.IndexOf('.');
        if (dot < 0) return (defaultSchema, s);
        return (s.Substring(0, dot), s.Substring(dot + 1));
    }

    /// <summary>SQL Server tipi → integration data type</summary>
    private static string MapSqlType(string sqlType) => (sqlType ?? "").ToLowerInvariant() switch
    {
        "int" or "smallint" or "tinyint" or "bigint" => "int",
        "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
        "float" or "real" => "decimal",
        "date" => "date",
        "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "datetime",
        "bit" => "bool",
        "uniqueidentifier" => "string",
        _ => "string",
    };

    /// <summary>
    /// Plain tablo kolonlari icin label uretir — UI dropdown'larinda "Belge No (DocumentNumber)"
    /// goster. Once Turkce sozlukten arar, bulamazsa PascalCase'i bosluklarla ayirir.
    /// </summary>
    private static string HumanizeColumnName(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return code ?? "";
        if (TurkishLabels.TryGetValue(code, out var tr)) return tr;
        // PascalCase split: "DocumentNumber" → "Document Number"
        var spaced = System.Text.RegularExpressions.Regex.Replace(code, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return spaced;
    }

    /// <summary>
    /// Yaygin CalibraHub kolon adlarinin Turkce karsiliklari. Genisletmek
    /// icin sadece bu sozlugu uzat — UI otomatik gunceller.
    /// </summary>
    private static readonly Dictionary<string, string> TurkishLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common doc fields
        ["Id"]                = "Id",
        ["DocumentNumber"]    = "Belge Numarasi",
        ["DocumentDate"]      = "Belge Tarihi",
        ["DocumentTypeId"]    = "Belge Tipi",
        ["ValidUntil"]        = "Gecerlilik Tarihi",
        ["ContactId"]         = "Cari Hesap",
        ["ContactAddress"]    = "Cari Adresi",
        ["SalesRepId"]        = "Satis Temsilcisi",
        ["Currency"]          = "Para Birimi",
        ["SubTotal"]          = "Ara Toplam",
        ["DiscountRate"]      = "Iskonto Orani",
        ["DiscountAmount"]    = "Iskonto Tutari",
        ["TaxRate"]           = "KDV Orani",
        ["TaxAmount"]         = "KDV Tutari",
        ["GrandTotal"]        = "Genel Toplam",
        ["PaymentTerms"]      = "Odeme Sartlari",
        ["DeliveryTerms"]     = "Teslimat Sartlari",
        ["DeliveryAddress"]   = "Teslimat Adresi",
        ["Description"]       = "Aciklama",
        ["Notes"]             = "Notlar",
        // Item / line fields
        ["ItemId"]            = "Stok Karti",
        ["ItemCode"]          = "Stok Kodu",
        ["Quantity"]          = "Miktar",
        ["UnitPrice"]         = "Birim Fiyat",
        ["LineTotal"]         = "Satir Toplami",
        ["CombinationId"]     = "Kombinasyon",
        ["UnitId"]            = "Olcu Birimi",
        // Common
        ["Code"]              = "Kod",
        ["Name"]              = "Ad",
        ["IsActive"]          = "Aktif",
        ["Created"]           = "Olusturma",
        ["Updated"]           = "Guncelleme",
        ["CreatedBy"]         = "Olusturan",
        ["UpdatedBy"]         = "Guncelleyen",
    };

    public async Task<IntegrationSampleRecordDto?> GetSampleRecordAsync(string formCode, string? recordId, CancellationToken ct)
    {
        // Identifier validation — formCode SQL'e gomulecek (v_Flat_{FormCode})
        if (!FormCodeRegex.IsMatch(formCode)) return null;

        // Once formu cek ki BaseRecordKey'i bilelim (id alani adi)
        var form = await GetFormAsync(formCode, ct);
        if (form is null || string.IsNullOrWhiteSpace(form.BaseRecordKey)) return null;

        // BaseRecordKey de identifier — guvenli mi?
        if (!FormCodeRegex.IsMatch(form.BaseRecordKey)) return null;

        var viewName = "v_Flat_" + formCode;

        // View var mi kontrolu — yoksa null don
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        bool viewExists;
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT CASE WHEN OBJECT_ID(N'[{_schema}].[{viewName}]', N'V') IS NOT NULL THEN 1 ELSE 0 END;";
            viewExists = ((int)(await chk.ExecuteScalarAsync(ct) ?? 0)) == 1;
        }
        if (!viewExists) return null;

        // recordId NULL ise en son kaydi (ORDER BY base PK DESC TOP 1) cek
        var keyEsc = form.BaseRecordKey.Replace("]", "]]");

        // İki adımlı arama: form'un BaseRecordKey'i kullanici-dostu bir kolon olabilir
        // (orn. SALES_ORDER_EDIT → "DocumentNumber" = "TKL202600000013"), ama widget
        // engine ve UI entegrasyon trigger'i integer Id ile cagirir (orn. "13").
        // İkisinin de calismasi icin once BaseRecordKey ile dene; hit yoksa ve view'da
        // standart "Id" kolonu varsa fallback olarak Id ile dene.
        var dict = await TryFetchRowAsync(conn, viewName, keyEsc, recordId, ct);
        if (dict is null
            && recordId is not null
            && !string.Equals(form.BaseRecordKey, "Id", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(recordId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            // View'da "Id" kolonu var mi? (base.* projeksiyonu Document/DocumentLine vs.
            // tablolarinin "Id" PK'sini her zaman ihrac eder)
            dict = await TryFetchRowAsync(conn, viewName, "Id", recordId, ct);
        }
        if (dict is null) return null;

        // RecordId'yi base record key kolonundan oku
        var resolvedId = dict.TryGetValue(form.BaseRecordKey, out var rid) ? rid?.ToString() ?? "" : (recordId ?? "");

        return new IntegrationSampleRecordDto(resolvedId, dict);
    }

    public async Task<string?> FindRecordIdByFieldValueAsync(string formCode, string fieldName, string fieldValue, CancellationToken ct)
    {
        if (!FormCodeRegex.IsMatch(formCode) || !FormCodeRegex.IsMatch(fieldName)) return null;
        var viewName = $"v_Flat_{formCode}";
        var fieldEsc = fieldName.Replace("]", "]]");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        var row = await TryFetchRowAsync(conn, viewName, fieldEsc, fieldValue, ct);
        if (row is null) return null;
        return row.TryGetValue("Id", out var id) && id is not null and not DBNull
            ? id.ToString()
            : null;
    }

    private async Task<Dictionary<string, object?>?> TryFetchRowAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        string viewName,
        string keyColumnEsc,
        string? recordId,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        if (recordId is null)
        {
            cmd.CommandText = $"SELECT TOP 1 * FROM [{_schema}].[{viewName}] ORDER BY [{keyColumnEsc}] DESC;";
        }
        else
        {
            cmd.CommandText = $"SELECT TOP 1 * FROM [{_schema}].[{viewName}] WHERE CAST([{keyColumnEsc}] AS NVARCHAR(100)) = @Rid;";
            cmd.Parameters.Add(new SqlParameter("@Rid", recordId));
        }
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                dict[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            return dict;
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            // Kolon yoksa "Invalid column name" hatasi gelir — sessizce null don.
            return null;
        }
    }
}
