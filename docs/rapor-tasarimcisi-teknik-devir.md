# Rapor Tasarımcısı — Teknik Devir Dokümanı

> Amaç: CalibraHub'daki React tabanlı rapor/dashboard tasarımcısını **başka bir projeye taşımak** için gereken her şey.
> Kaynak: canlı kod haritası (2026-07-17) + tasarım oturum notları (2026-06-16..21).
> Referanslar `src\...` ana ağacındandır.

---

## 0) Önemli ön uyarı — bu ad altında İKİ AYRI sistem var

| # | Sistem | Ne | Tablolar | API | Chart |
|---|--------|-----|----------|-----|-------|
| **A** | **Rapor Tasarımcısı** (React panel/dashboard) — **taşınacak asıl modül** | Sürükle-bırak panel ızgarası, 23 görsel türü, snapshot/cache hibrit | `ReportDesign`, `ReportSource`, `ReportSnapshot_{id}` | `/Dashboard/*` + `/api/report/*` | **recharts** + react-simple-maps |
| B | "Rapor Tasarımı" (`Views/Reporting/Designer.cshtml`) — legacy, menüde YOK | Tek VIEW seç → tablo/grafik/pivot (ad-hoc) | `RptView`, `RptViewCol`, `RptDef`, `RptDefRole`, `RptViewRole`, `RptRunLog` | `/api/reporting/*` | ECharts + Tabulator + PivotTable.js (CDN) |

A, B'nin uçlarını **kullanmaz**; B fiilen bağımsız/legacy'dir. Bu doküman **Sistem A**'yı anlatır. B'den taşınmaya değer tek şey: güvenli sorgu motoru deseni (bkz. §6 güvenlik).

---

## 1) Mimari genel bakış

4 sayfa, 2 React kök bileşeni:

```
Menü "Rapor Tasarımcısı" → /Dashboard/Designer      (tasarım listesi, SmartBoard)
                        → /Dashboard/DesignerEdit?load={id}
                            #reportDesignerRoot → mountReportDesigner(el, {
                                sourcesUrl:'/Dashboard/DesignerSources',
                                saveUrl:'/Dashboard/SaveDesigned',
                                listUrl:'/Dashboard/Designer', loadId })

Menü "Rapor Panoları"   → /Dashboard/Boards          (görüntüleme listesi, SmartBoard)
                        → /Dashboard/View/{id}
                            #reportViewerRoot → mountReportViewer(el, {
                                loadUrl:'/Dashboard/LoadView/{id}' })
```

- Designer = tam ekran editör (`position:fixed;inset:0`): topbar + sayfa sekmeleri + sol tür paleti + canvas (react-grid-layout) + sağ ayar paneli.
- Viewer = salt-okunur; **aynı** `PanelChart`/`ReportGrid`/`FilterField` bileşenlerini yeniden kullanır + Excel/PDF export + drill-down.
- Tasarım = tek JSON monolit (`ReportDesign.PanelsJson`), sayfalar → paneller ağacı.

## 2) Dosya envanteri

### React — `ClientApp/src/components/ReportDesigner/`
| Dosya | Satır | Rol |
|-------|------:|-----|
| `ReportDesigner.jsx` | 734 | Kök: sayfa/panel state, undo/redo (snapshot geçmişi, 400ms birleştirme, Ctrl+Z/Y), kaydet/yükle, topbar |
| `PanelChart.jsx` | 1681 | **Motor kalbi**: `buildSql` (istemci SQL üretici) + `useReportData` hook + TÜM renderer'lar |
| `SettingsSidebar.jsx` | 1541 | Sağ ayar paneli — panel JSON sözleşmesini üreten UI |
| `ReportDesigner.css` | 3010 | Tüm stiller (`rd-*` designer, `rv-*` viewer, `rs-*` sources) — **koşulsuz dark** tema |
| `SourcesModal.jsx` | 419 | Kayıtlı SQL kaynağı CRUD + snapshot switch + otomatik yenileme zamanlaması |
| `LeftPalette.jsx` | 260 | Görsel türü paleti (panel seçiliyken tür değiştirir, değilse yeni panel ekler) |
| `ChartPreview.jsx` | 195 | Veri yokken iskelet/placeholder |
| `ReportGrid.jsx` | 117 | react-grid-layout sarmalayıcı (12 kolon, rowHeight 26, compactType="vertical") |
| `PanelCard.jsx` | 81 | Panel kartı (grip/sil/seç + PanelChart) |
| `FilterField.jsx` | 70 | Distinct-değer çoklu seçim filtre bileşeni |

### React — `ClientApp/src/components/ReportViewer/`
- `ReportViewer.jsx` (350) — viewer + sol filtre drawer'ı + Export dropdown + `?dF=&dV=` cross-report filtre alımı.

### Mount
- `ClientApp/src/mount.jsx` → `mountReportDesigner` (~1811), `mountReportViewer` (~1784); `window.CalibraHub.mountXXX` global'leri; her mount ErrorBoundary'ye sarılı. Bundle: `wwwroot/react/calibrahub-widgets.js|.css`.

### NPM bağımlılıkları
`recharts ^3.8.1` · `react-simple-maps ^3.0.0` (+d3-geo, topojson-client) · `react-grid-layout ^1.5.0` (**1.5.0'a sabit — 2.x'te WidthProvider yok**) · `@dnd-kit/core|sortable|utilities` (kolon sıralama).

### Backend
| Dosya | Rol |
|-------|-----|
| `Controllers/DashboardController.cs` (398) | Tasarım CRUD + viewer + VIEW keşfi + SmartBoard config. `[PermissionScope(FormCodes.Dashboards)]` |
| `Controllers/ReportEngineController.cs` (293) | `/api/report` — kaynak CRUD + sorgu (inline/source) + materialize + zamanlama senkronu |
| `Controllers/GenericExportController.cs` | `POST /api/export/report-excel` (ClosedXML) |
| `Infrastructure/Reporting/ReportQueryService.cs` | Sorgu yürütme + hibrit snapshot/cache + SqlBulkCopy snapshot kurucu |
| `Persistence/Repositories/SqlReportDesignRepository.cs`, `SqlReportSourceRepository.cs` | CRUD |
| `Application/Contracts/ReportEngineContracts.cs` | DTO'lar |
| `Application/Services/Scheduling/ReportSnapshotRefreshTaskExecutor.cs` | Zamanlanmış snapshot yenileme (worker) |
| `Persistence/Repositories/SqlDbSchemaRepository.cs:300` | `GetDesignerViewsAsync` — sys.views+sys.columns keşfi |

DI kayıtları: `Program.cs` ~138-142, 170 (Web) + Worker Program.cs (executor + repo + query service).

## 3) DB şeması

```sql
-- Kayıtlı tasarımlar (JSON monolit)
CREATE TABLE dbo.ReportDesign (
    Id INT IDENTITY PRIMARY KEY,
    Title NVARCHAR(200) NOT NULL,
    GroupName NVARCHAR(100) NULL,
    Description NVARCHAR(1000) NULL,
    PanelsJson NVARCHAR(MAX) NOT NULL,      -- pages[] ağacı (bkz. §4)
    IsActive BIT NOT NULL DEFAULT 1,        -- soft delete
    CreatedBy NVARCHAR(120) NULL, UpdatedBy NVARCHAR(120) NULL,
    CreatedById INT NULL, UpdatedById INT NULL,
    Created DATETIME NOT NULL, Updated DATETIME NULL);

-- Kayıtlı SQL kaynakları (snapshot/cache)
CREATE TABLE dbo.ReportSource (
    Id INT IDENTITY PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,            -- UX unique WHERE IsActive=1
    Description NVARCHAR(500) NULL,
    SqlQuery NVARCHAR(MAX) NOT NULL,
    CacheTtlMinutes INT NOT NULL DEFAULT 5, -- canlı modda IMemoryCache TTL
    Materialize BIT NOT NULL DEFAULT 0,     -- 1 = snapshot tablodan oku
    LastMaterialized DATETIME NULL,
    MaterializedRows INT NULL,
    RefreshScheduleJson NVARCHAR(200) NULL, -- {mode:'hourly|daily|weekly', time, weekday}
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedBy/UpdatedBy/ById..., Created/Updated);

-- Dinamik: kaynak başına fiziksel snapshot tablosu (runtime DROP+CREATE+SqlBulkCopy)
-- dbo.ReportSnapshot_{sourceId}  — kolon tipleri DataTable'dan türetilir
```

Not: FK yok (tasarım JSON monolit). Snapshot tablosu isimle bağlı. **CreatedBy string kalmalı** — bu tabloların repoları string audit kullanır (2026-06-21'de INT migration bunları kırmıştı, bilinçli geri alındı).

## 4) JSON sözleşmesi (PanelsJson)

Save payload: `{ id?, title, groupName?, description?, panels: pages }` — backend `object[]` olarak serialize eder, **şema doğrulaması yok** (sözleşme tamamen frontend'de).

```jsonc
// pages[] — çok sayfalı; eski düz panel dizisi yüklenirken otomatik Sayfa 1'e sarılır
[{ "id": "pg_1", "title": "Sayfa 1",
   "source": {            // SAYFA DÜZEYİ tek veri kaynağı — paneller miras alır
     "sourceType": "view" | "saved" | "sql",
     "source": "vw_X",    // view adı
     "sourceLabel": "…", "sqlQuery": "…", "sourceId": 3, "sourceName": "…" },
   "panels": [ /* Panel[] */ ] }]
```

```jsonc
// Panel — temel + tür-özel (tümü PanelChart.jsx tüketir)
{ "id": "rdp_1", "type": "bar", "title": "…", "subtitle": "…",
  "layout": { "x":0, "y":0, "w":6, "h":8 },     // react-grid-layout, 12 kolon birimi
  // view modunda alanlar:
  "metric": "Amount", "aggregate": "SUM|AVG|COUNT|COUNT_DISTINCT|MIN|MAX",
  "group": "Month", "groupIsTime": true,
  // saved/sql modunda alan eşleme (ham kolonlardan):
  "labelField": "…", "valueField": "…", "rawAgg": "SUM",
  "valueField2/seriesField/xField/yField/sizeField": "…",         // combo/radar/scatter
  "regionField/latField/lonField": "…",                            // haritalar
  "startField/endField/colorField": "…",                           // gantt
  // görsel: color, color2, thickness, curve, dots, fillOpacity, horizontal,
  //         donut, showValues, showLabels, showPercent, gaugeMin/Max, bulletTarget,
  //         prefix, suffix, decimals, textContent (metin kartı basit markdown)
  // tablo:
  "columns": { "ColName": { "visible": true, "label": "…",
      "format": "auto|text|number|currency|percent|date|datetime|duration|bool|custom",
      "decimals": 2, "currency": "TRY", "align": "auto|left|center|right",
      "total": true, "totalAgg": "SUM", "filter": true } },
  "columnOrder": ["Code","Name"], "sorts": [{ "field":"…", "dir":"asc|desc" }],
  // pivot (çok-alanlı, Excel tarzı; client-side computePivot):
  "pivotRows": [], "pivotCols": [], "pivotValues": [{ "field":"…", "agg":"sum" }],
  "showTotals": true,
  // etkileşim (yalnız viewer'da aktif):
  "clickAction": "drill" | "navigate", "clickTargetId": 5 }
```

**23 görsel türü:** line, area, bar, pie, treemap, funnel, combo (çift Y ekseni), waterfall, stacked100, scatter (z=bubble), radar (çoklu seri), gauge, bullet, heatmap, stat (KPI), table, pivot, text (basit markdown — dangerouslySetInnerHTML YOK), gantt, map_tr, map_world, map_bubble, filter. Haritaların geo verisi runtime CDN'den (`MAP_PRESETS`; offline için panel `geoUrl` override).

**Filtreler:** kolon `filter:true` → otomatik sol filtre rayı (designer inline ray, viewer sol drawer). Sayfa-bazlı runtime state (`filtersByPage`), persist edilmez. Eşleşme alan ADI ile (aynı kolonu içeren tüm view panellerine `WHERE [field] IN (...)`).

## 5) API yüzeyi

```
# Tasarım (DashboardController) — cookie auth + CSRF (POST'larda RequestVerificationToken)
GET  /Dashboard/DesignsList             → tasarım özetleri
GET  /Dashboard/LoadDesign/{id}         → {title, groupName, description, panelsJson}   (designer yetkisi)
GET  /Dashboard/LoadView/{id}           → aynı                                          (viewer yetkisi)
POST /Dashboard/SaveDesigned            ← {id?, title, groupName?, description?, panels}
POST /Dashboard/DeleteDesign/{id}       → soft-delete
GET  /Dashboard/DesignerSources         → [{name, metrics[], groups[]}]  (dbo VIEW keşfi; cbv_Guide_* elenir)

# Sorgu motoru (ReportEngineController, /api/report)
GET    /api/report/sources              → ReportSourceDto[]
POST   /api/report/sources              ← SaveReportSourceRequest → {ok,id}  (+cache invalidate +zamanlama senkron)
DELETE /api/report/sources/{id}
POST   /api/report/sources/{id}/materialize → {ok,rows}   (yetki: RefreshReportData)
GET    /api/report/query/source/{id}    → {ok, columns[], rows[][], rowCount, fromCache, elapsedMs}
POST   /api/report/query/inline         ← {sql, cacheTtlMinutes} → aynı şekil

# Export
POST /api/export/report-excel           ← form payload=<json> {fileName, sheets:[{sheetName,headers:[{id,label}],rows}]}
```

## 6) Veri yürütme + GÜVENLİK BORCU (taşımada 1 numaralı iş)

- **SQL istemcide üretilir** (`PanelChart.buildSql`): `SELECT {agg}([col]) AS [value] FROM [{view}] GROUP BY ...`. Kolon/kaynak `[ ]` ile sarılır, filtre değerleri `'`→`''`.
- Backend inline SQL'i **olduğu gibi** çalıştırır: parametreleştirme YOK, whitelist YOK, SELECT-only zorlaması YOK (30 sn timeout var). Yetki: dashboard görüntüleme izni olan herkes kendi şirket DB'sinde **keyfi SQL** çalıştırabilir.
- **Taşırken düzelt:** (a) en az read-only DB kullanıcısı + SELECT doğrulaması; (b) ideali Sistem B'nin katmanlı deseni: VIEW whitelist (FK tablo) → ad regex → kolon whitelist → enum-only operatör/aggregate → tam parametrize bind → context injection (CompanyId/UserId otomatik WHERE). Referans: `Application/Services/ReportEngineService.cs` (satır 17-23 savunma katmanları, 538 BuildFilterClause, 464 InjectContext).

**Hibrit snapshot/cache** (`ReportQueryService.QuerySourceAsync`):
- `Materialize=1` → `SELECT * FROM ReportSnapshot_{id}` (yoksa ilk istekte kurulur). SQL değişikliği snapshot'a ancak "Yenile" ya da zamanlanmış görevle yansır.
- `Materialize=0` → canlı SQL + IMemoryCache TTL (`CacheTtlMinutes`). Cache anahtarında DB adı (multi-tenant izolasyon). Kaynak kaydedilince invalidate + frontend `dataNonce` mekanizmasıyla panel refetch.
- Snapshot kurma: SQL → DataTable → DROP+CREATE → SqlBulkCopy.
- **Zamanlama:** `RefreshScheduleJson` → deterministik adlı ScheduledTask (`report-snapshot-refresh-{id}`) upsert; 3-durumlu sahiplik (kapalı=görev yok · snapshot açık+oto kapalı=Manual görev, admin düzenler · oto açık=`managedBy:"report"`, admin salt-okunur). Worker `MaterializeSourceForCompanyAsync(companyId, sourceId)` — HttpContext'siz.

## 7) Render notları

- recharts + el yapımı SVG/DOM (gauge/bullet/heatmap/gantt/pivot/table).
- Tablo `formatCell`: number/currency(₺$€)/percent/date/duration(`2sa 15dk`)/bool/custom şablon (`{} adet`, `{n2}`), `tr-TR` locale, sağa-hizalı sayısall