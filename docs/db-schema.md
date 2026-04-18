# CalibraHub Veritabanı Şeması

Kaynak: `src/CalibraHub.Persistence/Database/CalibraDatabaseInitializer.cs`
Toplam tablo: 47 (eski isimli stale girdiler kaldırıldı — yeni isimli tablolar: Contact, Item, Unit, Feature, FeatureValue, FieldGroup, Field, BOM, BOMLine, Location, Document, DocumentLine, PriceGroup, PriceList, User, Department — henüz dokümante edilmedi)

> Tüm tablolar per-company schema (`[{s}]`) altında oluşturulur. Sadece `dbo.Forms` global `dbo` schema'sındadır. Tip ve kısıtlar `CREATE TABLE` bloklarından birebir kopyalanmıştır; `ALTER TABLE ... ADD` ile sonradan eklenen kolonlar "(eklendi)" notu ile işaretlenmiştir. Bazı tablolar migration blokları sonucunda yeniden oluşturulur — bu durumlarda en güncel tanım kullanılmıştır.

---

## Kullanıcı & Yetki

### `user_settings`

> Aynı adda iki CREATE TABLE bloğu mevcut (satır 364 ve satır 4481). İkisi de aynı kolonlara sahiptir.

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PK |
| user_id | UNIQUEIDENTIFIER | NOT NULL | |
| setting_key | NVARCHAR(200) | NOT NULL | |
| setting_value | NVARCHAR(MAX) | NULL | |
| updated_at | DATETIME2(0) | NOT NULL | |

**Indexler:**
- `ux_user_settings_user_key` UNIQUE (user_id, setting_key)

---

## Şirket

### `company`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_company` |
| name | NVARCHAR(120) | NOT NULL | |
| title | NVARCHAR(200) | NOT NULL | |
| address | NVARCHAR(500) | NOT NULL | |
| tax_office | NVARCHAR(100) | NOT NULL | |
| tax_number | NVARCHAR(20) | NOT NULL | |
| is_e_document_approval_enabled | BIT | NOT NULL | DEFAULT(0) `df_company_is_e_document_approval_enabled` |
| is_active | BIT | NOT NULL | DEFAULT(1) `df_company_is_active` |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |
| connection_string | NVARCHAR(500) | NULL | (eklendi) |
| city | NVARCHAR(100) | NULL | (eklendi) |
| district | NVARCHAR(100) | NULL | (eklendi) |
| postal_code | NVARCHAR(10) | NULL | (eklendi) |

**Indexler:**
- `ux_company_name` UNIQUE (name)
- `ux_company_tax_number` UNIQUE (tax_number)

---

## Stok & Lojistik

### `stock_unit_conversions`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| stock_card_id | INT | NOT NULL | DEFAULT(0) (migration sonrası) |
| line_no | INT | NOT NULL | |
| unit_code | NVARCHAR(20) | NOT NULL | |
| multiplier | DECIMAL(18,6) | NOT NULL | DEFAULT(1) |

**Indexler:**
- `ux_stock_unit_conversions_card_line` UNIQUE (stock_card_id, line_no)

---

## Ürün Özellik / Konfigürasyon

### `material_card_field_options`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_material_card_field_options` |
| field_definition_id | UNIQUEIDENTIFIER | NOT NULL | FK → `material_card_field_settings(id)` |
| option_key | NVARCHAR(60) | NOT NULL | |
| option_label | NVARCHAR(160) | NOT NULL | |
| sort_order | INT | NOT NULL | DEFAULT(0) `df_material_card_field_options_sort_order` |
| is_active | BIT | NOT NULL | DEFAULT(1) `df_material_card_field_options_is_active` |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ux_material_card_field_options_field_definition_option_key` UNIQUE (field_definition_id, option_key)

### `ProductConfiguration`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| ParentId | INT | NULL | FK `fk_ProductConfiguration_ParentId` → self |
| RecordType | NVARCHAR(20) | NOT NULL | CHECK `ck_ProductConfiguration_RecordType` IN (FEATURE, VALUE, CONFIG, FEATURE_STOCK) |
| RecordCode | NVARCHAR(100) | NOT NULL | |
| RecordName | NVARCHAR(255) | NOT NULL | |
| DataType | NVARCHAR(20) | NULL | |
| RelatedMaterialCode | NVARCHAR(50) | NULL | |
| IsActive | BIT | NOT NULL | DEFAULT(1) `df_ProductConfiguration_IsActive` |
| CreatedDate | DATETIME | NOT NULL | DEFAULT(GETDATE()) `df_ProductConfiguration_CreatedDate` |

**Indexler:**
- `ix_ProductConfiguration_RecordType_ParentId` (RecordType, ParentId)
- `ix_ProductConfiguration_RelatedMaterialCode` (RelatedMaterialCode)
- `ux_ProductConfiguration_FeatureStock_Parent_RelatedMaterialCode` UNIQUE (ParentId, RelatedMaterialCode) WHERE RecordType=FEATURE_STOCK
- `ux_ProductConfiguration_Feature_RecordCode` UNIQUE (RecordCode) WHERE RecordType=FEATURE
- `ux_ProductConfiguration_Config_Material_RecordCode` UNIQUE (RelatedMaterialCode, RecordCode) WHERE RecordType=CONFIG

### `screen_layout_definitions`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_screen_layout_definitions` |
| screen_code | NVARCHAR(80) | NOT NULL | |
| layout_json | NVARCHAR(MAX) | NOT NULL | |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ux_screen_layout_definitions_screen_code` UNIQUE (screen_code)

### `stock_card_property_mappings`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_stock_card_property_mappings` |
| stock_card_id | INT | NOT NULL | FK → `Items(id)` |
| property_id | UNIQUEIDENTIFIER | NOT NULL | FK → `configuration_properties(id)` |
| property_value_id | UNIQUEIDENTIFIER | NULL | FK → `configuration_property_values(id)` |
| configuration_code | NVARCHAR(120) | NULL | |
| text_value | NVARCHAR(250) | NULL | |
| numeric_value | DECIMAL(18,4) | NULL | |
| date_value | DATE | NULL | |
| is_active | BIT | NOT NULL | DEFAULT(1) `df_stock_card_property_mappings_is_active` |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ix_stock_card_property_mappings_stock_card_id` (stock_card_id)
- `ix_stock_card_property_mappings_property_id` (property_id)

---

## Malzeme Grupları & Ürün Ağacı

### `MaterialGroupMappings`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| StockCardId | INT | NOT NULL | |
| SlotOrder | TINYINT | NOT NULL | |
| GroupCode | NVARCHAR(10) | NOT NULL | |

**Indexler:**
- `uq_mat_group_mappings_slot` UNIQUE (StockCardId, SlotOrder)

### `MaterialGroups`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| GroupCategory | TINYINT | NOT NULL | DEFAULT 1 |
| GroupCode | NVARCHAR(10) | NOT NULL | |
| GroupDescription | NVARCHAR(100) | NULL | |

**Indexler:**
- `uq_material_groups_cat_code` UNIQUE (GroupCategory, GroupCode)

---

## Satış Teklifi

### `sales_quote_attachments`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| quote_id | UNIQUEIDENTIFIER | NOT NULL | |
| file_name | NVARCHAR(300) | NOT NULL | |
| mime_type | NVARCHAR(120) | NULL | |
| file_size | BIGINT | NOT NULL | |
| content | VARBINARY(MAX) | NOT NULL | |
| uploaded_by | NVARCHAR(120) | NULL | |
| uploaded_at | DATETIME2(0) | NOT NULL | |
| is_active | BIT | NOT NULL | DEFAULT(1) |

**Indexler:**
- `ix_sales_quote_attachments_quote` (quote_id)

### `sales_quote_line_details`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| quote_line_id | UNIQUEIDENTIFIER | NOT NULL | FK `fk_sqld_quote_line` → `sales_quote_lines(id)` ON DELETE CASCADE |
| feature_name | NVARCHAR(200) | NOT NULL | |
| value_code | NVARCHAR(100) | NOT NULL | |
| value_name | NVARCHAR(200) | NOT NULL | |
| description | NVARCHAR(500) | NULL | |
| line_order | INT | NOT NULL | DEFAULT(0) |

**Indexler:**
- `ix_sqld_quote_line` (quote_line_id)

### `sales_representatives`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| rep_code | NVARCHAR(20) | NOT NULL | |
| rep_name | NVARCHAR(200) | NOT NULL | |
| is_active | BIT | NOT NULL | DEFAULT(1) |
| created_at | DATETIME2(0) | NOT NULL | DEFAULT(GETDATE()) |
| updated_at | DATETIME2(0) | NOT NULL | DEFAULT(GETDATE()) |

**Indexler:**
- `ux_sales_representatives_code` UNIQUE (rep_code)

---

## Fiyat Listesi & Döviz

### `currencies`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| code | NVARCHAR(5) | NOT NULL | |
| name | NVARCHAR(100) | NOT NULL | |
| symbol | NVARCHAR(5) | NULL | |
| is_active | BIT | NOT NULL | DEFAULT(1) |
| created_at | DATETIME2(0) | NOT NULL | DEFAULT(GETDATE()) |
| updated_at | DATETIME2(0) | NOT NULL | DEFAULT(GETDATE()) |

**Indexler:**
- `ux_currencies_code` UNIQUE (code)

### `exchange_rates`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| currency_code | NVARCHAR(5) | NOT NULL | |
| rate_date | DATE | NOT NULL | |
| buying_rate | DECIMAL(18,6) | NOT NULL | |
| selling_rate | DECIMAL(18,6) | NOT NULL | |
| effective_buying_rate | DECIMAL(18,6) | NOT NULL | DEFAULT(0) (eklendi) |
| effective_selling_rate | DECIMAL(18,6) | NOT NULL | DEFAULT(0) (eklendi) |
| source | NVARCHAR(20) | NOT NULL | DEFAULT(N'TCMB') |
| created_at | DATETIME2(0) | NOT NULL | DEFAULT(GETDATE()) |

**Indexler:**
- `ux_exchange_rates_code_date` UNIQUE (currency_code, rate_date)

---

## Belge, Rapor & Tasarım

### `design_templates`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| name | NVARCHAR(200) | NOT NULL | |
| type | NVARCHAR(50) | NOT NULL | |
| sub_type | NVARCHAR(100) | NULL | (eklendi) |
| description | NVARCHAR(500) | NULL | |
| html_content | NVARCHAR(MAX) | NULL | |
| css_content | NVARCHAR(MAX) | NULL | |
| gjs_data | NVARCHAR(MAX) | NULL | |
| jsr_content | NVARCHAR(MAX) | NULL | (eklendi — frx_content yerine) |
| is_active | BIT | NOT NULL | DEFAULT(1) `df_design_templates_is_active` |
| created_at | DATETIME2(0) | NOT NULL | |
| updated_at | DATETIME2(0) | NOT NULL | |

**Indexler:**
- `ix_design_templates_type` (type)
- `ix_design_templates_sub_type` (sub_type)

### `document_types`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| code | NVARCHAR(50) | NOT NULL | |
| name | NVARCHAR(200) | NOT NULL | |
| sql_view_name | NVARCHAR(128) | NULL | |
| description | NVARCHAR(500) | NULL | |
| is_active | BIT | NOT NULL | DEFAULT(1) |
| created_at | DATETIME2(0) | NOT NULL | |
| updated_at | DATETIME2(0) | NOT NULL | |

**Indexler:**
- `ux_document_types_code` UNIQUE (code)

### `report_templates`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| name | NVARCHAR(200) | NOT NULL | |
| document_type_id | UNIQUEIDENTIFIER | NOT NULL | FK `fk_report_templates_document_type` → `document_types(id)` |
| frx_file_path | NVARCHAR(500) | NULL | |
| frx_content | VARBINARY(MAX) | NULL | (tanımda var; ALTER ile de idempotent ekleme) |
| description | NVARCHAR(500) | NULL | |
| is_default | BIT | NOT NULL | DEFAULT(0) |
| is_active | BIT | NOT NULL | DEFAULT(1) |
| created_at | DATETIME2(0) | NOT NULL | |
| updated_at | DATETIME2(0) | NOT NULL | |

**Indexler:**
- `ix_report_templates_document_type` (document_type_id)

---

## Not (Notes)

### `card_group_mappings`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| entity_type | TINYINT | NOT NULL | |
| entity_id | NVARCHAR(50) | NOT NULL | |
| level | TINYINT | NOT NULL | |
| card_group_id | INT | NOT NULL | |

**Indexler:**
- `uq_card_group_mappings` UNIQUE (entity_type, entity_id, level)
- `ix_card_group_mappings_entity` (entity_type, entity_id)

### `card_groups`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| card_type | TINYINT | NOT NULL | |
| level | TINYINT | NOT NULL | |
| parent_id | INT | NULL | |
| code | NVARCHAR(20) | NOT NULL | |
| description | NVARCHAR(200) | NULL | |

**Indexler:**
- `ix_card_groups_type_level` (card_type, level)

### `note_attachments`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| note_id | UNIQUEIDENTIFIER | NOT NULL | FK `fk_note_attachments_note_id` → `notes(id)` |
| file_name | NVARCHAR(255) | NOT NULL | |
| stored_name | NVARCHAR(255) | NOT NULL | |
| content_type | NVARCHAR(100) | NULL | |
| file_size | BIGINT | NOT NULL | DEFAULT(0) `df_note_attachments_file_size` |
| uploaded_at | DATETIME2(0) | NOT NULL | |
| description | NVARCHAR(500) | NULL | (tanımda var; ayrıca ALTER ile idempotent ekleme) |

**Indexler:**
- `ix_note_attachments_note_id` (note_id)

### `note_folders`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| company_id | INT | NOT NULL | |
| user_id | UNIQUEIDENTIFIER | NOT NULL | |
| name | NVARCHAR(200) | NOT NULL | |
| parent_folder_id | UNIQUEIDENTIFIER | NULL | |
| created_at | DATETIME2 | NOT NULL | |
| is_deleted | BIT | NOT NULL | DEFAULT(0) `df_note_folders_is_deleted` |

**Indexler:**
- `ix_note_folders_company_user` (company_id, user_id)

### `note_reminders`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| note_id | UNIQUEIDENTIFIER | NOT NULL | FK `fk_note_reminders_notes_note_id` → `notes(id)` |
| remind_at | DATETIME2 | NOT NULL | |
| is_sent | BIT | NOT NULL | DEFAULT(0) `df_note_reminders_is_sent` |
| sent_at | DATETIME2 | NULL | |
| recurrence_type | INT | NOT NULL | DEFAULT(0) `df_note_reminders_recurrence_type` (eklendi) |
| recurrence_data | NVARCHAR(200) | NULL | (eklendi) |

**Indexler:**
- `ix_note_reminders_note_id` (note_id)
- `ix_note_reminders_unsent` (is_sent, remind_at)

### `note_shares`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| note_id | UNIQUEIDENTIFIER | NOT NULL | FK `fk_note_shares_notes_note_id` → `notes(id)` |
| shared_with_user_id | UNIQUEIDENTIFIER | NOT NULL | |
| shared_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ux_note_shares_note_user` UNIQUE (note_id, shared_with_user_id)
- `ix_note_shares_note_id` (note_id)
- `ix_note_shares_shared_with` (shared_with_user_id)

### `notes`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| company_id | INT | NOT NULL | |
| user_id | UNIQUEIDENTIFIER | NOT NULL | |
| title | NVARCHAR(200) | NOT NULL | |
| content | NVARCHAR(MAX) | NULL | |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |
| is_deleted | BIT | NOT NULL | DEFAULT(0) `df_notes_is_deleted` |
| is_fully_encrypted | BIT | NOT NULL | DEFAULT(0) `df_notes_is_fully_encrypted` |
| encryption_hint | NVARCHAR(300) | NULL | |
| folder_id | UNIQUEIDENTIFIER | NULL | (eklendi) |
| is_pinned | BIT | NOT NULL | DEFAULT(0) `df_notes_is_pinned` (eklendi) |

**Indexler:**
- `ix_notes_company_user` (company_id, user_id)

---

## Widget / EAV / Alan Ayarları

### `FldSet`

> Form sabit alan–rehber eşleştirme ve ayar tablosu.

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_FldSet` |
| FormId | INT | NOT NULL | |
| FieldKey | NVARCHAR(120) | NOT NULL | |
| FieldLabel | NVARCHAR(200) | NOT NULL | |
| GuideCode | NVARCHAR(60) | NULL | |
| FilterJson | NVARCHAR(MAX) | NULL | |
| IsRequired | BIT | NOT NULL | DEFAULT(0) `df_FldSet_Req` |
| FormatJson | NVARCHAR(MAX) | NULL | |
| IsActive | BIT | NOT NULL | DEFAULT(1) `df_FldSet_Active` |
| SortOrder | INT | NOT NULL | DEFAULT(0) `df_FldSet_Sort` |
| CreatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_FldSet_Created` |
| UpdatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_FldSet_Updated` |

**Indexler:**
- `ux_FldSet_FormField` UNIQUE (FormId, FieldKey)
- `ix_FldSet_Guide` (GuideCode) WHERE GuideCode IS NOT NULL

### `Forms` (dbo schema)

> Tek `dbo` tablosu — tüm şirketler için ortak form kataloğu.

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| FormCode | NVARCHAR(50) | NOT NULL | |
| FormName | NVARCHAR(200) | NOT NULL | |
| Module | NVARCHAR(50) | NOT NULL | |
| SubModule | NVARCHAR(50) | NULL | |
| SortOrder | INT | NOT NULL | DEFAULT(0) |
| IsActive | BIT | NOT NULL | DEFAULT(1) |
| BaseTable | NVARCHAR(120) | NULL | (eklendi) |
| BaseRecordKey | NVARCHAR(60) | NULL | (eklendi) |

**Indexler:**
- `ux_forms_code` UNIQUE (FormCode)

### `WidgetMas`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_WidgetMas` |
| CompanyId | INT | NOT NULL | DEFAULT(0) `df_WidgetMas_Company` |
| FormId | INT | NOT NULL | FK `fk_WidgetMas_Form` → `Forms(Id)` |
| ParentId | INT | NULL | FK `fk_WidgetMas_Parent` → self |
| WidgetCode | NVARCHAR(100) | NOT NULL | |
| Label | NVARCHAR(255) | NOT NULL | |
| DataType | NVARCHAR(30) | NOT NULL | |
| MaxLength | INT | NULL | |
| MinLength | INT | NULL | |
| ExpectedLength | INT | NULL | |
| MinValue | DECIMAL(18,4) | NULL | |
| MaxValue | DECIMAL(18,4) | NULL | |
| SortOrder | INT | NOT NULL | DEFAULT(0) `df_WidgetMas_Sort` |
| OptionsJSON | NVARCHAR(MAX) | NULL | |
| RulesJSON | NVARCHAR(MAX) | NULL | |
| IsPlainField | BIT | NOT NULL | DEFAULT(0) `df_WidgetMas_Plain` |
| IsRequired | BIT | NOT NULL | DEFAULT(0) `df_WidgetMas_Req` |
| IsListable | BIT | NOT NULL | DEFAULT(1) `df_WidgetMas_Listable` |
| IsActive | BIT | NOT NULL | DEFAULT(1) `df_WidgetMas_Active` |
| ColorType | INT | NOT NULL | DEFAULT(0) `df_WidgetMas_ColorType` |
| ColorValue | NVARCHAR(100) | NULL | |
| CreatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_WidgetMas_Created` |
| UpdatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_WidgetMas_Updated` |

**Indexler:**
- `ux_WidgetMas_FormCode` UNIQUE (CompanyId, FormId, WidgetCode)
- `ix_WidgetMas_Parent` (ParentId)

### `WidgetTra`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | BIGINT | NOT NULL | IDENTITY(1,1), PK `pk_WidgetTra` |
| WidgetId | INT | NOT NULL | FK `fk_WidgetTra_Widget` → `WidgetMas(Id)` |
| RecordId | NVARCHAR(60) | NOT NULL | |
| ParentRecordId | NVARCHAR(60) | NULL | (eklendi) |
| Value | NVARCHAR(MAX) | NULL | |
| CreatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_WidgetTra_Created` |
| UpdatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_WidgetTra_Updated` |

**Indexler:**
- `ux_WidgetTra_Record` UNIQUE (WidgetId, RecordId)
- `ix_WidgetTra_Record_Widget` (RecordId, WidgetId)
- `ix_WidgetTra_Parent` (ParentRecordId, WidgetId) WHERE ParentRecordId IS NOT NULL

### `dynamic_field_values`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| screen_code | NVARCHAR(60) | NOT NULL | |
| entity_id | UNIQUEIDENTIFIER | NOT NULL | |
| field_definition_id | UNIQUEIDENTIFIER | NOT NULL | |
| field_key | NVARCHAR(60) | NOT NULL | |
| text_value | NVARCHAR(MAX) | NULL | |
| numeric_value | DECIMAL(18,4) | NULL | |
| date_value | DATETIME2 | NULL | |
| boolean_value | BIT | NULL | |
| created_at | DATETIME2(0) | NOT NULL | |
| updated_at | DATETIME2(0) | NOT NULL | |

**Indexler:**
- `ix_dfv_entity` (screen_code, entity_id)

---

## Rehber (Guide / Lookup)

### `GuideMas`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_GuideMas` |
| GuideCode | NVARCHAR(60) | NOT NULL | |
| GuideLabel | NVARCHAR(200) | NOT NULL | |
| ViewName | NVARCHAR(200) | NOT NULL | |
| ValueColumn | NVARCHAR(60) | NOT NULL | |
| DisplayColumn | NVARCHAR(60) | NOT NULL | |
| GridColumnsJson | NVARCHAR(MAX) | NOT NULL | |
| DefaultSortColumn | NVARCHAR(60) | NULL | |
| IsActive | BIT | NOT NULL | DEFAULT(1) `df_GuideMas_Active` |
| CreatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_GuideMas_Created` |
| UpdatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_GuideMas_Updated` |

**Indexler:**
- `ux_GuideMas_GuideCode` UNIQUE (GuideCode)

---

## Organizasyon

### `org_chart_nodes`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| chart_id | UNIQUEIDENTIFIER | NOT NULL | FK `fk_org_chart_nodes_chart` → `org_charts(id)` |
| user_id | UNIQUEIDENTIFIER | NOT NULL | |
| parent_user_id | UNIQUEIDENTIFIER | NULL | |
| position_title | NVARCHAR(200) | NULL | |
| sort_order | INT | NOT NULL | DEFAULT(0) `df_org_chart_nodes_sort` |

**Indexler:**
- `uq_org_chart_nodes_chart_user` UNIQUE (chart_id, user_id)
- `ix_org_chart_nodes_chart` (chart_id)

### `org_charts`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| company_id | INT | NOT NULL | |
| name | NVARCHAR(200) | NOT NULL | |
| is_default | BIT | NOT NULL | DEFAULT(0) `df_org_charts_is_default` |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ix_org_charts_company` (company_id)

---

## Entegrasyon

### `erp_connection_settings`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PK `pk_erp_connection_settings` |
| company_id | INT | NOT NULL | DEFAULT(0) |
| provider | NVARCHAR(50) | NOT NULL | DEFAULT(N'Netsis') `df_erp_connection_settings_provider` |
| company | NVARCHAR(50) | NOT NULL | |
| business | NVARCHAR(100) | NOT NULL | |
| branch | NVARCHAR(100) | NOT NULL | |
| username | NVARCHAR(160) | NOT NULL | |
| password | NVARCHAR(300) | NOT NULL | |
| is_active | BIT | NOT NULL | DEFAULT(1) `df_erp_connection_settings_is_active` |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ux_erp_connection_settings_provider_company_business_branch` UNIQUE (provider, company, business, branch)

### `incoming_documents`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_incoming_documents` |
| integrator_settings_id | INT | NULL | (migration sonrası INT NULL) FK `fk_incoming_documents_integrator_settings_id` |
| envelope_id | NVARCHAR(120) | NOT NULL | |
| document_number | NVARCHAR(120) | NOT NULL | |
| kind | NVARCHAR(30) | NOT NULL | |
| issue_date | DATE | NOT NULL | |
| sender_tax_number | NVARCHAR(20) | NOT NULL | |
| recipient_tax_number | NVARCHAR(20) | NOT NULL | |
| payload_raw | NVARCHAR(MAX) | NOT NULL | |
| approval_status | NVARCHAR(20) | NOT NULL | DEFAULT(N'Pending') `df_incoming_documents_approval_status` |
| imported_at | DATETIME2 | NOT NULL | |
| sender_name | NVARCHAR(200) | NULL | (eklendi) |

**Indexler:**
- `ux_incoming_documents_envelope_id` UNIQUE (envelope_id)
- `ix_incoming_documents_approval_status_imported_at` (approval_status, imported_at DESC)
- `ix_incoming_documents_kind` (kind)
- `ux_incoming_documents_kind_document_number_recipient_tax_number` UNIQUE (kind, document_number, recipient_tax_number)

### `integration_api_profiles`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | DEFAULT NEWID(), PK `PK_integration_api_profiles` |
| company_id | INT | NOT NULL | DEFAULT({DefaultCompanyId}) (eklendi/guaranteed) |
| name | NVARCHAR(200) | NOT NULL | |
| auth_type | NVARCHAR(50) | NOT NULL | DEFAULT 'None' |
| base_url | NVARCHAR(500) | NOT NULL | |
| auth_config_json | NVARCHAR(MAX) | NULL | |
| is_active | BIT | NOT NULL | DEFAULT 1 |
| created_at | DATETIME2 | NOT NULL | DEFAULT GETDATE() |
| updated_at | DATETIME2 | NOT NULL | DEFAULT GETDATE() |

### `integration_event_definitions`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| company_id | INT | NOT NULL | DEFAULT(0) (migration sonrası INT) |
| name | NVARCHAR(200) | NOT NULL | |
| event_source | NVARCHAR(100) | NOT NULL | |
| event_type | NVARCHAR(50) | NOT NULL | |
| event_detail | NVARCHAR(200) | NULL | |
| sql_command | NVARCHAR(MAX) | NOT NULL | |
| stop_on_error | BIT | NOT NULL | DEFAULT(1) |
| is_active | BIT | NOT NULL | DEFAULT(1) |
| execution_order | INT | NOT NULL | DEFAULT(0) |
| created_at | DATETIME2(0) | NOT NULL | |
| updated_at | DATETIME2(0) | NOT NULL | |
| action_type | NVARCHAR(50) | NOT NULL | DEFAULT 'SqlCommand' (eklendi) |
| procedure_name | NVARCHAR(200) | NULL | (eklendi) |
| parameters_json | NVARCHAR(MAX) | NULL | (eklendi) |
| api_config_json | NVARCHAR(MAX) | NULL | (eklendi) |

**Indexler:**
- `ix_ied_lookup` (company_id, event_source, event_type, is_active)

### `integration_event_logs`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PRIMARY KEY |
| definition_id | UNIQUEIDENTIFIER | NOT NULL | |
| company_id | INT | NOT NULL | DEFAULT(0) (migration sonrası INT) |
| event_source | NVARCHAR(100) | NOT NULL | |
| event_type | NVARCHAR(50) | NOT NULL | |
| executed_sql | NVARCHAR(MAX) | NOT NULL | |
| success | BIT | NOT NULL | |
| error_message | NVARCHAR(MAX) | NULL | |
| executed_at | DATETIME2(0) | NOT NULL | |
| duration_ms | BIGINT | NOT NULL | DEFAULT(0) |
| action_type | NVARCHAR(50) | NULL | DEFAULT 'SqlCommand' (eklendi) |
| response_body | NVARCHAR(MAX) | NULL | (eklendi) |

**Indexler:**
- `ix_iel_browse` (company_id, executed_at DESC)

### `integrator_settings`

> İlk CREATE bloğu (satır 394) eski GUID tabanlı tanımdır; `MigrateIntegratorSettingsTableAsync` içindeki INT IDENTITY versiyon güncel şemadır (aşağıda).

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_integrator_settings` |
| company_id | INT | NOT NULL | |
| provider | NVARCHAR(50) | NOT NULL | |
| name | NVARCHAR(100) | NOT NULL | DEFAULT N'' |
| base_url | NVARCHAR(300) | NOT NULL | |
| company_tax_number | NVARCHAR(20) | NOT NULL | DEFAULT N'' |
| username | NVARCHAR(120) | NOT NULL | |
| secret | NVARCHAR(1024) | NOT NULL | |
| polling_interval_seconds | INT | NOT NULL | DEFAULT(120), CHECK >= 10 |
| max_records_per_pull | INT | NOT NULL | DEFAULT(200), CHECK 1..5000 |
| log_retention_days | INT | NOT NULL | DEFAULT(30), CHECK 1..3650 |
| include_received_documents_in_pull | BIT | NOT NULL | DEFAULT(0) |
| mark_downloaded_documents_as_received | BIT | NOT NULL | DEFAULT(0) |
| include_issued_einvoice_in_pull | BIT | NOT NULL | DEFAULT(0) |
| include_issued_earchive_in_pull | BIT | NOT NULL | DEFAULT(0) |
| include_issued_edispatch_in_pull | BIT | NOT NULL | DEFAULT(0) |
| is_active | BIT | NOT NULL | DEFAULT(1) |
| created_at | DATETIME2 | NOT NULL | DEFAULT(GETDATE()) |
| updated_at | DATETIME2 | NOT NULL | DEFAULT(GETDATE()) |
| app_str | NVARCHAR(100) | NULL | (eklendi) |
| source | NVARCHAR(20) | NULL | (eklendi) |
| app_version | NVARCHAR(20) | NULL | (eklendi) |
| schedule_enabled | BIT | NOT NULL | DEFAULT(0) (eklendi) |
| timeout_seconds | INT | NOT NULL | DEFAULT(30) (eklendi) |
| lookback_days | INT | NOT NULL | DEFAULT(30) (eklendi) |

**Indexler:**
- `ux_integrator_settings_company_id` UNIQUE (company_id) — şirket başına tek kayıt

### `smtp_profiles`

> Migration sonrası güncel şema (UNIQUEIDENTIFIER id + company_id + OAuth2). Eski INT IDENTITY versiyon varsa DROP edilip bu şemayla yeniden oluşturulur.

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | UNIQUEIDENTIFIER | NOT NULL | PK `pk_smtp_profiles` |
| company_id | INT | NOT NULL | DEFAULT(0) (ALTER ile de idempotent) |
| name | NVARCHAR(120) | NOT NULL | |
| from_email | NVARCHAR(160) | NOT NULL | |
| from_display_name | NVARCHAR(120) | NULL | |
| host | NVARCHAR(200) | NOT NULL | |
| port | INT | NOT NULL | DEFAULT(587) `df_smtp_profiles_port`, CHECK 1..65535 |
| username | NVARCHAR(160) | NOT NULL | |
| password | NVARCHAR(2000) | NOT NULL | |
| auth_method | NVARCHAR(30) | NOT NULL | DEFAULT(N'Normal') (eklendi) |
| oauth2_client_id | NVARCHAR(300) | NULL | (eklendi) |
| oauth2_client_secret | NVARCHAR(300) | NULL | (eklendi) |
| oauth2_refresh_token | NVARCHAR(500) | NULL | (eklendi) |
| use_ssl | BIT | NOT NULL | DEFAULT(1) `df_smtp_profiles_use_ssl` |
| is_active | BIT | NOT NULL | DEFAULT(1) `df_smtp_profiles_is_active` |
| created_at | DATETIME2 | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ux_smtp_profiles_name` UNIQUE (name)

---

## Sistem / Log / UI

### `PLT_SISTEM_LOG`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| ID | INT | NOT NULL | IDENTITY(1,1), PRIMARY KEY |
| VERITABANI | VARCHAR(50) | NULL | |
| UYGULAMA_ID | INT | NULL | |
| ACIKLAMA | VARCHAR(200) | NULL | |
| MODUL_NO | INT | NULL | |
| PROGRAM_NO | INT | NULL | |
| KAYIT_NO | INT | NULL | |
| ISLETME_KODU | INT | NULL | |
| SUBE_KODU | INT | NULL | |
| BELGE_TURU | VARCHAR(50) | NULL | |
| STOK_KODU | VARCHAR(50) | NULL | |
| CARI_KOD | VARCHAR(50) | NULL | |
| MUH_KODU | VARCHAR(50) | NULL | |
| PROJE_KODU | VARCHAR(50) | NULL | |
| BELGE_NO | VARCHAR(50) | NULL | |
| DEPO_KODU | INT | NULL | |
| HESAP_KODU | VARCHAR(50) | NULL | |
| TARIH | DATETIME | NULL | |
| MIKTAR | DECIMAL(18,8) | NULL | |
| FIYAT | DECIMAL(18,8) | NULL | |
| TUTAR | DECIMAL(18,8) | NULL | |
| SIRA_NO | INT | NULL | |
| SERI_NO | VARCHAR(50) | NULL | |
| S_SAHA_01 | VARCHAR(50) | NULL | |
| S_SAHA_02 | VARCHAR(100) | NULL | |
| S_SAHA_03 | VARCHAR(50) | NULL | |
| S_SAHA_04 | VARCHAR(50) | NULL | |
| S_SAHA_05 | VARCHAR(50) | NULL | |
| C_SAHA_01 | CHAR(1) | NULL | |
| C_SAHA_02 | CHAR(1) | NULL | |
| C_SAHA_03 | CHAR(1) | NULL | |
| C_SAHA_04 | CHAR(1) | NULL | |
| C_SAHA_05 | CHAR(1) | NULL | |
| I_SAHA_01 | INT | NULL | |
| I_SAHA_02 | INT | NULL | |
| I_SAHA_03 | INT | NULL | |
| I_SAHA_04 | INT | NULL | |
| I_SAHA_05 | INT | NULL | |
| F_SAHA_01 | DECIMAL(18,8) | NULL | |
| F_SAHA_02 | DECIMAL(18,8) | NULL | |
| F_SAHA_03 | DECIMAL(18,8) | NULL | |
| F_SAHA_04 | DECIMAL(18,8) | NULL | |
| F_SAHA_05 | DECIMAL(18,8) | NULL | |
| D_SAHA_01 | DATETIME | NULL | |
| D_SAHA_02 | DATETIME | NULL | |
| D_SAHA_03 | DATETIME | NULL | |
| D_SAHA_04 | DATETIME | NULL | |
| D_SAHA_05 | DATETIME | NULL | |
| N_SAHA_01 | NVARCHAR(MAX) | NULL | |
| N_SAHA_02 | NVARCHAR(MAX) | NULL | |
| COMPANY_ID | INT | NULL | FK `fk_plt_sistem_log_company_company_id` → `company(id)` |
| KAYITYAPANKUL | VARCHAR(50) | NULL | |
| KAYITTARIHI | DATETIME | NULL | |
| DUZELTMEYAPANKUL | VARCHAR(50) | NULL | |
| DUZELTMETARIHI | DATETIME | NULL | |
| ONAYTIPI | NCHAR(10) | NULL | |
| ONAYNUM | NCHAR(10) | NULL | |

**Indexler:**
- `ix_plt_sistem_log_company_id` (COMPANY_ID)

### `ui_label_translations`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| id | INT | NOT NULL | IDENTITY(1,1), PK `pk_ui_label_translations` |
| form_key | NVARCHAR(120) | NOT NULL | |
| label_key | NVARCHAR(120) | NOT NULL | |
| language_code | NVARCHAR(20) | NOT NULL | |
| label_text | NVARCHAR(500) | NOT NULL | |
| updated_at | DATETIME2 | NOT NULL | |

**Indexler:**
- `ux_ui_label_translations_form_label_language` UNIQUE (form_key, label_key, language_code)

---

## Dinamik Raporlama

### `RptDef`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_RptDef` |
| Code | NVARCHAR(60) | NOT NULL | |
| Name | NVARCHAR(200) | NOT NULL | |
| ViewId | INT | NOT NULL | FK `fk_RptDef_View` → `RptView(Id)` |
| Category | TINYINT | NOT NULL | |
| ConfigJson | NVARCHAR(MAX) | NOT NULL | |
| OwnerUserId | UNIQUEIDENTIFIER | NOT NULL | |
| IsShared | BIT | NOT NULL | DEFAULT(0) `df_RptDef_Shared` |
| IsActive | BIT | NOT NULL | DEFAULT(1) `df_RptDef_Active` |
| CreatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_RptDef_Created` |
| UpdatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_RptDef_Updated` |

**Indexler:**
- `ux_RptDef_Code` UNIQUE (Code)
- `ix_RptDef_Owner` (OwnerUserId) WHERE IsActive=1
- `ix_RptDef_View` (ViewId)

### `RptDefRole`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_RptDefRole` |
| DefId | INT | NOT NULL | FK `fk_RptDefRole_Def` → `RptDef(Id)` ON DELETE CASCADE |
| Role | TINYINT | NOT NULL | |
| CanView | BIT | NOT NULL | DEFAULT(1) `df_RptDefRole_V` |
| CanEdit | BIT | NOT NULL | DEFAULT(0) `df_RptDefRole_E` |
| CanDelete | BIT | NOT NULL | DEFAULT(0) `df_RptDefRole_D` |

**Indexler:**
- `ux_RptDefRole_DefRole` UNIQUE (DefId, Role)

### `RptRunLog`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | BIGINT | NOT NULL | IDENTITY(1,1), PK `pk_RptRunLog` |
| DefId | INT | NULL | |
| ViewId | INT | NOT NULL | |
| UserId | UNIQUEIDENTIFIER | NOT NULL | |
| CompanyId | INT | NULL | |
| StartedAt | DATETIME2(3) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_RptRunLog_Started` |
| DurationMs | INT | NULL | |
| RowCount | INT | NULL | |
| Error | NVARCHAR(2000) | NULL | |
| SqlHash | BINARY(32) | NULL | |

**Indexler:**
- `ix_RptRunLog_DefDate` (DefId, StartedAt DESC)
- `ix_RptRunLog_UserDate` (UserId, StartedAt DESC)

### `RptView`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_RptView` |
| Code | NVARCHAR(60) | NOT NULL | |
| Name | NVARCHAR(200) | NOT NULL | |
| SqlObjectName | NVARCHAR(120) | NOT NULL | |
| Description | NVARCHAR(500) | NULL | |
| IsActive | BIT | NOT NULL | DEFAULT(1) `df_RptView_Active` |
| CreatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_RptView_Created` |
| UpdatedAt | DATETIME2(0) | NOT NULL | DEFAULT(SYSUTCDATETIME()) `df_RptView_Updated` |

**Indexler:**
- `ux_RptView_Code` UNIQUE (Code)
- `ux_RptView_SqlObjectName` UNIQUE (SqlObjectName) WHERE IsActive=1

### `RptViewCol`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_RptViewCol` |
| ViewId | INT | NOT NULL | FK `fk_RptViewCol_View` → `RptView(Id)` ON DELETE CASCADE |
| ColName | NVARCHAR(120) | NOT NULL | |
| DisplayName | NVARCHAR(200) | NOT NULL | |
| DataType | TINYINT | NOT NULL | 1=String, 2=Integer, 3=Decimal, 4=Date, 5=DateTime, 6=Boolean |
| IsFilterable | BIT | NOT NULL | DEFAULT(1) `df_RptViewCol_Filt` |
| IsGroupable | BIT | NOT NULL | DEFAULT(0) `df_RptViewCol_Grp` |
| IsAggregatable | BIT | NOT NULL | DEFAULT(0) `df_RptViewCol_Agg` |
| DefaultAggregate | TINYINT | NULL | |
| Ordinal | INT | NOT NULL | DEFAULT(0) `df_RptViewCol_Ord` |
| ContextBinding | TINYINT | NULL | 0=None, 1=CompanyId, 2=UserId, 3=OwnerUserId |

**Indexler:**
- `ux_RptViewCol_ViewCol` UNIQUE (ViewId, ColName)
- `ix_RptViewCol_View` (ViewId, Ordinal)

### `RptViewRole`

| Kolon | Tip | Null | Default / Not |
|-------|-----|------|---------------|
| Id | INT | NOT NULL | IDENTITY(1,1), PK `pk_RptViewRole` |
| ViewId | INT | NOT NULL | FK `fk_RptViewRole_View` → `RptView(Id)` ON DELETE CASCADE |
| Role | TINYINT | NOT NULL | |
| CanQuery | BIT | NOT NULL | DEFAULT(1) `df_RptViewRole_Q` |
| CanDesign | BIT | NOT NULL | DEFAULT(0) `df_RptViewRole_D` |

**Indexler:**
- `ux_RptViewRole_ViewRole` UNIQUE (ViewId, Role)
