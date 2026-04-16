using System.Globalization;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence;

/// <summary>
/// Faz D Adim 1 — Eski dinamik alan verilerini yeni WidgetMas + WidgetTra
/// tablolarina tasir.
///
/// Kaynak tablolar (dokunulmaz, sadece okunur):
///   material_card_field_groups  → WidgetMas satiri (dataType='group')
///   material_card_field_settings → WidgetMas satiri (parentId ile grubuna bagli)
///   material_card_field_options → WidgetMas.OptionsJson'a serialize
///   dynamic_field_values        → WidgetTra satirlari
///
/// Screen code → FormCode mapping:
///   material_cards / MaterialCards → ITEMS
///   contact_accounts              → CONTACTS
///   sales_quotes                  → SALES_QUOTE_EDIT
///   product_configuration         → PRODUCT_CONFIG
///
/// DataType enum → yeni lowercase key:
///   STRING → text, INTEGER/DECIMAL → numeric, DATE → date,
///   BOOLEAN → boolean, DROPDOWN → dropdown, MULTISELECT → multi-select,
///   LINK → skip (yeni sistemde yok)
///
/// Business key (RecordId) cozumleme:
///   ITEMS:            entity_id Guid → int (IntToGuid inverse) → Items.MaterialCode
///   CONTACTS:         entity_id Guid → int → contact_accounts.account_code
///   SALES_QUOTE_EDIT: entity_id Guid → sales_quotes.quote_number (native Guid)
/// </summary>
public sealed class LegacyMigrationService : ILegacyMigrationService
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IWidgetRepository _widgetRepo;
    private readonly string _schema;

    public LegacyMigrationService(
        SqlServerConnectionFactory connectionFactory,
        IWidgetRepository widgetRepo,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _widgetRepo = widgetRepo;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    // ── Mapping sabitleri ─────────────────────────────

    private static readonly Dictionary<string, string> ScreenCodeToFormCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["material_cards"]        = "ITEMS",
            ["materialcards"]         = "ITEMS",
            ["contact_accounts"]      = "CONTACTS",
            ["contactaccounts"]       = "CONTACTS",
            ["sales_quotes"]          = "SALES_QUOTE_EDIT",
            ["salesquotes"]           = "SALES_QUOTE_EDIT",
            ["sales_quote_edit"]      = "SALES_QUOTE_EDIT",
            ["product_configuration"] = "PRODUCT_CONFIG",
            ["product_config"]        = "PRODUCT_CONFIG",
        };

    private static readonly Dictionary<string, string?> LegacyDataTypeToNew =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["STRING"]      = "text",
            ["INTEGER"]     = "numeric",
            ["DECIMAL"]     = "numeric",
            ["DATE"]        = "date",
            ["BOOLEAN"]     = "boolean",
            ["DROPDOWN"]    = "dropdown",
            ["MULTISELECT"] = "multi-select",
            ["LINK"]        = null,   // skip — yeni sistemde yok
        };

    // ── Dahili DTO'lar (legacy okuma icin) ────────────

    private sealed class LegacyGroup
    {
        public Guid Id { get; set; }
        public string ScreenCode { get; set; } = "";
        public string GroupKey { get; set; } = "";
        public string GroupLabel { get; set; } = "";
        public int DisplayOrder { get; set; }
    }

    private sealed class LegacyField
    {
        public Guid Id { get; set; }
        public string ScreenCode { get; set; } = "";
        public Guid? GroupId { get; set; }
        public string FieldKey { get; set; } = "";
        public string FieldLabel { get; set; } = "";
        public string DataType { get; set; } = "STRING";
        public int DisplayOrder { get; set; }
    }

    private sealed class LegacyOption
    {
        public Guid FieldDefinitionId { get; set; }
        public string OptionLabel { get; set; } = "";
        public int SortOrder { get; set; }
    }

    private sealed class LegacyValue
    {
        public string ScreenCode { get; set; } = "";
        public Guid EntityId { get; set; }
        public Guid? FieldDefinitionId { get; set; }
        public string FieldKey { get; set; } = "";
        public string? TextValue { get; set; }
        public decimal? NumericValue { get; set; }
        public DateTime? DateValue { get; set; }
        public bool? BooleanValue { get; set; }
    }

    // ══════════════════════════════════════════════════════════
    // Main entry point
    // ══════════════════════════════════════════════════════════
    public async Task<LegacyMigrationReport> MigrateAsync(CancellationToken ct)
    {
        var report = new LegacyMigrationReport();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Eski tablolar var mi? Yoksa (fresh DB) — sessizce geri don
        if (!await TableExistsAsync(conn, "material_card_field_groups", ct))
        {
            report.Warnings.Add("material_card_field_groups tablosu bulunamadi; migration atlandi.");
            return report;
        }

        // ── 1. Eski verileri oku ─────────────────────
        var legacyGroups  = await LoadLegacyGroupsAsync(conn, ct);
        var legacyFields  = await LoadLegacyFieldsAsync(conn, ct);
        var optionsByFieldId = await LoadLegacyOptionsAsync(conn, ct);
        var legacyValues  = await LoadLegacyValuesAsync(conn, ct);

        // ── 2. Form katalogu ──────────────────────────
        var forms = await _widgetRepo.GetFormsAsync(ct);
        var formByCode = forms.ToDictionary(f => f.FormCode, f => f.Id, StringComparer.OrdinalIgnoreCase);

        // Cached widget listesi (idempotent skip icin)
        var widgetsByForm = new Dictionary<int, List<WidgetDefinition>>();

        async Task<List<WidgetDefinition>> GetWidgetsForForm(int formId)
        {
            if (widgetsByForm.TryGetValue(formId, out var cached)) return cached;
            var list = (await _widgetRepo.GetWidgetsByFormAsync(formId, ct)).ToList();
            widgetsByForm[formId] = list;
            return list;
        }

        // ── 3. Gruplari yaz ──────────────────────────
        var legacyGroupIdToNewId = new Dictionary<Guid, int>();
        foreach (var lg in legacyGroups)
        {
            var formCode = MapFormCode(lg.ScreenCode);
            if (formCode == null || !formByCode.TryGetValue(formCode, out var formId))
            {
                report.GroupsSkipped++;
                report.Warnings.Add($"Grup '{lg.GroupKey}' icin ScreenCode='{lg.ScreenCode}' eslesmedi.");
                continue;
            }

            var existing = await GetWidgetsForForm(formId);
            var dup = existing.FirstOrDefault(w =>
                string.Equals(w.WidgetCode, lg.GroupKey, StringComparison.OrdinalIgnoreCase));
            if (dup != null)
            {
                legacyGroupIdToNewId[lg.Id] = dup.Id;
                report.GroupsSkipped++;
                continue;
            }

            var newId = await _widgetRepo.UpsertWidgetAsync(new WidgetDefinition
            {
                FormId = formId,
                ParentId = null,
                WidgetCode = NormalizeCode(lg.GroupKey),
                Label = lg.GroupLabel,
                DataType = "group",
                SortOrder = lg.DisplayOrder,
                OptionsJson = null,
                IsActive = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            }, ct);

            legacyGroupIdToNewId[lg.Id] = newId;
            report.GroupsMigrated++;
            // cache'e ekle (sonraki field upsert'leri dogru duplicate check yapabilsin)
            existing.Add(new WidgetDefinition
            {
                Id = newId,
                FormId = formId,
                WidgetCode = NormalizeCode(lg.GroupKey),
                Label = lg.GroupLabel,
                DataType = "group",
                SortOrder = lg.DisplayOrder,
            });
        }

        // ── 4. Fieldlari yaz ─────────────────────────
        var legacyFieldIdToNewId = new Dictionary<Guid, int>();
        var legacyFieldIdToFormCode = new Dictionary<Guid, string>();
        foreach (var lf in legacyFields)
        {
            var formCode = MapFormCode(lf.ScreenCode);
            if (formCode == null || !formByCode.TryGetValue(formCode, out var formId))
            {
                report.FieldsSkipped++;
                report.Warnings.Add($"Field '{lf.FieldKey}' icin ScreenCode='{lf.ScreenCode}' eslesmedi.");
                continue;
            }

            // DataType map
            var dtKey = (lf.DataType ?? "STRING").ToUpperInvariant();
            if (!LegacyDataTypeToNew.TryGetValue(dtKey, out var newDataType))
            {
                report.FieldsSkipped++;
                report.Warnings.Add($"Field '{lf.FieldKey}' icin DataType='{lf.DataType}' tanimsiz.");
                continue;
            }
            if (newDataType == null)
            {
                // Link gibi skip edilecek tip
                report.FieldsSkipped++;
                continue;
            }

            int? parentId = null;
            if (lf.GroupId.HasValue && legacyGroupIdToNewId.TryGetValue(lf.GroupId.Value, out var pId))
                parentId = pId;

            // Options serialize (sadece dropdown / multi-select icin)
            string? optionsJson = null;
            if ((newDataType == "dropdown" || newDataType == "multi-select") &&
                optionsByFieldId.TryGetValue(lf.Id, out var opts) && opts.Count > 0)
            {
                var labels = opts
                    .OrderBy(o => o.SortOrder)
                    .Select(o => o.OptionLabel)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();
                optionsJson = JsonSerializer.Serialize(labels);
            }

            // Duplicate check
            var existing = await GetWidgetsForForm(formId);
            var dup = existing.FirstOrDefault(w =>
                string.Equals(w.WidgetCode, lf.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (dup != null)
            {
                legacyFieldIdToNewId[lf.Id] = dup.Id;
                legacyFieldIdToFormCode[lf.Id] = formCode;
                report.FieldsSkipped++;
                continue;
            }

            var newId = await _widgetRepo.UpsertWidgetAsync(new WidgetDefinition
            {
                FormId = formId,
                ParentId = parentId,
                WidgetCode = NormalizeCode(lf.FieldKey),
                Label = lf.FieldLabel,
                DataType = newDataType,
                SortOrder = lf.DisplayOrder,
                OptionsJson = optionsJson,
                IsActive = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            }, ct);

            legacyFieldIdToNewId[lf.Id] = newId;
            legacyFieldIdToFormCode[lf.Id] = formCode;
            report.FieldsMigrated++;

            existing.Add(new WidgetDefinition
            {
                Id = newId,
                FormId = formId,
                ParentId = parentId,
                WidgetCode = NormalizeCode(lf.FieldKey),
                Label = lf.FieldLabel,
                DataType = newDataType,
                SortOrder = lf.DisplayOrder,
                OptionsJson = optionsJson,
            });
        }

        // ── 5. Business key lookup tablolari (lazy) ──
        Dictionary<int, string>? materialCodeById = null;
        Dictionary<int, string>? accountCodeById = null;
        Dictionary<Guid, string>? quoteNumberById = null;

        async Task<Dictionary<int, string>> GetMaterialCodes()
        {
            if (materialCodeById != null) return materialCodeById;
            // Items tablosu snake_case: material_code
            materialCodeById = await LoadBusinessKeysIntAsync(conn, "Items", "Id", "material_code", ct);
            return materialCodeById;
        }
        async Task<Dictionary<int, string>> GetAccountCodes()
        {
            if (accountCodeById != null) return accountCodeById;
            // ContactAccounts tablosu PascalCase: Id / AccountCode
            accountCodeById = await LoadBusinessKeysIntAsync(conn, "ContactAccounts", "Id", "AccountCode", ct);
            return accountCodeById;
        }
        async Task<Dictionary<Guid, string>> GetQuoteNumbers()
        {
            if (quoteNumberById != null) return quoteNumberById;
            // sales_quotes tablosu snake_case: id / quote_number
            quoteNumberById = await LoadBusinessKeysGuidAsync(conn, "sales_quotes", "id", "quote_number", ct);
            return quoteNumberById;
        }

        // ── 6. Values'leri (formCode, recordId) ile gruplandir ──
        // UpsertValuesAsync DELETE-then-INSERT pattern kullaniyor; bir kayit
        // icin tek cagri ile tum degerleri yazmak gerekiyor.
        var valueGroups = new Dictionary<(string FormCode, string RecordId), Dictionary<int, string?>>();

        foreach (var lv in legacyValues)
        {
            var formCode = MapFormCode(lv.ScreenCode);
            if (formCode == null || !formByCode.TryGetValue(formCode, out var formId))
            {
                report.ValuesSkipped++;
                continue;
            }

            // Widget id cozumle
            int? widgetId = null;
            if (lv.FieldDefinitionId.HasValue &&
                legacyFieldIdToNewId.TryGetValue(lv.FieldDefinitionId.Value, out var wid))
            {
                widgetId = wid;
            }
            else if (!string.IsNullOrEmpty(lv.FieldKey))
            {
                var widgets = await GetWidgetsForForm(formId);
                var match = widgets.FirstOrDefault(w =>
                    string.Equals(w.WidgetCode, lv.FieldKey, StringComparison.OrdinalIgnoreCase));
                if (match != null) widgetId = match.Id;
            }
            if (widgetId == null)
            {
                report.ValuesSkipped++;
                continue;
            }

            // RecordId (business key) cozumle
            string? recordId = null;
            switch (formCode)
            {
                case "ITEMS":
                {
                    var intId = TryGuidToInt(lv.EntityId);
                    if (intId.HasValue)
                    {
                        var codes = await GetMaterialCodes();
                        codes.TryGetValue(intId.Value, out recordId);
                    }
                    break;
                }
                case "CONTACTS":
                {
                    var intId = TryGuidToInt(lv.EntityId);
                    if (intId.HasValue)
                    {
                        var codes = await GetAccountCodes();
                        codes.TryGetValue(intId.Value, out recordId);
                    }
                    break;
                }
                case "SALES_QUOTE_EDIT":
                {
                    var nums = await GetQuoteNumbers();
                    nums.TryGetValue(lv.EntityId, out recordId);
                    break;
                }
            }
            if (string.IsNullOrEmpty(recordId))
            {
                report.ValuesSkipped++;
                continue;
            }

            // Value string'e serialize
            string? valueStr = null;
            if (lv.TextValue != null)            valueStr = lv.TextValue;
            else if (lv.NumericValue.HasValue) valueStr = lv.NumericValue.Value.ToString(CultureInfo.InvariantCulture);
            else if (lv.DateValue.HasValue)    valueStr = lv.DateValue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (lv.BooleanValue.HasValue) valueStr = lv.BooleanValue.Value ? "true" : "false";
            if (valueStr == null)
            {
                report.ValuesSkipped++;
                continue;
            }

            // Multi-select: virgulle ayrilmis değerler → JSON array
            // (Eski sistem text_value'ya "Mavi,Kirmizi" gibi yaziyordu)
            var newWidgetCache = await GetWidgetsForForm(formId);
            var widgetDef = newWidgetCache.FirstOrDefault(w => w.Id == widgetId.Value);
            if (widgetDef?.DataType == "multi-select" && valueStr.Contains(','))
            {
                var parts = valueStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
                valueStr = JsonSerializer.Serialize(parts);
            }

            var key = (FormCode: formCode, RecordId: recordId);
            if (!valueGroups.TryGetValue(key, out var dict))
            {
                dict = new Dictionary<int, string?>();
                valueGroups[key] = dict;
            }
            dict[widgetId.Value] = valueStr;
        }

        // ── 7. Gruplanmis value'lari tek upsert cagrisi ile yaz ──
        foreach (var kv in valueGroups)
        {
            if (!formByCode.TryGetValue(kv.Key.FormCode, out var formId))
            {
                report.ValuesSkipped += kv.Value.Count;
                continue;
            }

            // Idempotent: bu (formId, recordId) icin WidgetTra'da hic kayit varsa
            // migration yapilmaz — kullanicinin Faz C'den beri girdigi veriyi korur.
            var existingValues = await _widgetRepo.GetValuesAsync(formId, kv.Key.RecordId, ct);
            if (existingValues.Count > 0)
            {
                report.ValuesSkipped += kv.Value.Count;
                continue;
            }

            await _widgetRepo.UpsertValuesAsync(formId, kv.Key.RecordId, kv.Value, ct);
            report.ValuesMigrated += kv.Value.Count;
        }

        return report;
    }

    // ══════════════════════════════════════════════════════════
    // Raw SQL readers
    // ══════════════════════════════════════════════════════════

    private async Task<bool> TableExistsAsync(SqlConnection conn, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT OBJECT_ID(N'[{_schema}].[{tableName}]', N'U')";
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj != null && obj != DBNull.Value;
    }

    private async Task<List<LegacyGroup>> LoadLegacyGroupsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<LegacyGroup>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [id], [screen_code], [group_key], [group_label], [display_order]
            FROM [{_schema}].[material_card_field_groups]
            WHERE [is_active] = 1;";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new LegacyGroup
            {
                Id = r.GetGuid(0),
                ScreenCode = r.IsDBNull(1) ? "" : r.GetString(1),
                GroupKey = r.IsDBNull(2) ? "" : r.GetString(2),
                GroupLabel = r.IsDBNull(3) ? "" : r.GetString(3),
                DisplayOrder = r.IsDBNull(4) ? 0 : r.GetInt32(4),
            });
        }
        return list;
    }

    private async Task<List<LegacyField>> LoadLegacyFieldsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<LegacyField>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [id], [screen_code], [group_id], [field_key], [field_label], [data_type], [display_order]
            FROM [{_schema}].[material_card_field_settings]
            WHERE [is_active] = 1;";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new LegacyField
            {
                Id = r.GetGuid(0),
                ScreenCode = r.IsDBNull(1) ? "" : r.GetString(1),
                GroupId = r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2),
                FieldKey = r.IsDBNull(3) ? "" : r.GetString(3),
                FieldLabel = r.IsDBNull(4) ? "" : r.GetString(4),
                DataType = r.IsDBNull(5) ? "STRING" : r.GetString(5),
                DisplayOrder = r.IsDBNull(6) ? 0 : r.GetInt32(6),
            });
        }
        return list;
    }

    private async Task<Dictionary<Guid, List<LegacyOption>>> LoadLegacyOptionsAsync(SqlConnection conn, CancellationToken ct)
    {
        var dict = new Dictionary<Guid, List<LegacyOption>>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [field_definition_id], [option_label], [sort_order]
            FROM [{_schema}].[material_card_field_options]
            WHERE [is_active] = 1
            ORDER BY [field_definition_id], [sort_order];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fieldId = r.GetGuid(0);
            if (!dict.TryGetValue(fieldId, out var list))
            {
                list = new List<LegacyOption>();
                dict[fieldId] = list;
            }
            list.Add(new LegacyOption
            {
                FieldDefinitionId = fieldId,
                OptionLabel = r.IsDBNull(1) ? "" : r.GetString(1),
                SortOrder = r.IsDBNull(2) ? 0 : r.GetInt32(2),
            });
        }
        return dict;
    }

    private async Task<List<LegacyValue>> LoadLegacyValuesAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<LegacyValue>();
        if (!await TableExistsAsync(conn, "dynamic_field_values", ct)) return list;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [screen_code], [entity_id], [field_definition_id], [field_key],
                   [text_value], [numeric_value], [date_value], [boolean_value]
            FROM [{_schema}].[dynamic_field_values];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new LegacyValue
            {
                ScreenCode = r.IsDBNull(0) ? "" : r.GetString(0),
                EntityId = r.GetGuid(1),
                FieldDefinitionId = r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2),
                FieldKey = r.IsDBNull(3) ? "" : r.GetString(3),
                TextValue = r.IsDBNull(4) ? null : r.GetString(4),
                NumericValue = r.IsDBNull(5) ? (decimal?)null : r.GetDecimal(5),
                DateValue = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6),
                BooleanValue = r.IsDBNull(7) ? (bool?)null : r.GetBoolean(7),
            });
        }
        return list;
    }

    /// <summary>
    /// Int PK'li entity tablolarindan Id → BusinessKey map yukler.
    /// Kullanim: Items/Id/MaterialCode, contact_accounts/id/account_code
    /// </summary>
    private async Task<Dictionary<int, string>> LoadBusinessKeysIntAsync(
        SqlConnection conn, string table, string idColumn, string keyColumn, CancellationToken ct)
    {
        var dict = new Dictionary<int, string>();
        if (!await TableExistsAsync(conn, table, ct)) return dict;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [{idColumn}], [{keyColumn}] FROM [{_schema}].[{table}] WHERE [{keyColumn}] IS NOT NULL;";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
            var id = r.GetInt32(0);
            var key = r.GetString(1);
            if (!string.IsNullOrWhiteSpace(key))
                dict[id] = key;
        }
        return dict;
    }

    /// <summary>
    /// Guid PK'li entity tablolarindan Id → BusinessKey map yukler.
    /// Kullanim: sales_quotes/id/quote_number
    /// </summary>
    private async Task<Dictionary<Guid, string>> LoadBusinessKeysGuidAsync(
        SqlConnection conn, string table, string idColumn, string keyColumn, CancellationToken ct)
    {
        var dict = new Dictionary<Guid, string>();
        if (!await TableExistsAsync(conn, table, ct)) return dict;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT [{idColumn}], [{keyColumn}] FROM [{_schema}].[{table}] WHERE [{keyColumn}] IS NOT NULL;";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
            var id = r.GetGuid(0);
            var key = r.GetString(1);
            if (!string.IsNullOrWhiteSpace(key))
                dict[id] = key;
        }
        return dict;
    }

    // ══════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════

    private static string? MapFormCode(string screenCode)
    {
        if (string.IsNullOrWhiteSpace(screenCode)) return null;
        return ScreenCodeToFormCode.TryGetValue(screenCode.Trim(), out var formCode) ? formCode : null;
    }

    /// <summary>
    /// IntToGuid(int value) inverse. Eski sistem Int PK'leri Guid'e donusturmek
    /// icin ilk 4 byte'a int yaziyordu, gerisi 0. TryGuidToInt o Guid'i geri
    /// cevirir (sadece first-4-byte pattern'i eslesirse).
    /// </summary>
    private static int? TryGuidToInt(Guid g)
    {
        var bytes = g.ToByteArray();
        // bytes[4..16] tum sifir olmali (IntToGuid'in pattern'i)
        for (int i = 4; i < 16; i++)
            if (bytes[i] != 0) return null;
        return BitConverter.ToInt32(bytes, 0);
    }

    /// <summary>
    /// WidgetCode'u WidgetService.NormalizeWidgetCode ile ayni kurallara gore
    /// normalize eder — backend upsert throws atmasin diye.
    /// </summary>
    private static string NormalizeCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unknown";
        var s = raw.Trim().ToLowerInvariant();
        s = s.Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
             .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "_");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9_]", "");
        if (s.Length == 0 || !char.IsLetter(s[0]))
            s = "w_" + s;
        if (s.Length > 100) s = s.Substring(0, 100);
        return s;
    }
}
