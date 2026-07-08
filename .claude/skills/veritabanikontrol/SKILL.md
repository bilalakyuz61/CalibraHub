---
name: veritabanikontrol
description: >
  CalibraHub'da her setup/güncelleme (release) ÖNCESİ "kod ↔ DB şema senkron mu" denetimi.
  Boş bir LocalDB'ye kurulumu çalıştırıp gerçek şemayı çıkarır, tüm repository SQL kolon
  referanslarıyla karşılaştırır ve "Invalid column name / Invalid object name" riski taşıyan
  uyuşmazlıkları release-blocker olarak raporlar. Kullanıcı "veritabanı kontrol", "şema denetimi",
  "release öncesi kontrol", "kod db senkron mu", "sıfırdan kurulum hatasız mı", "güncelleme öncesi
  doğrula", "db audit" gibi bir şey dediğinde bu skill'i çalıştır.
---

# CalibraHub — Veritabanı Kontrol (Kod ↔ DB Şema Senkron Denetimi)

## Ne zaman kullanılır
Yeni bir setup exe üretmeden veya bir bilgisayara güncelleme yaymadan **önce**. Amaç: repository SQL'inin beklediği kolon/tablo adları ile `CalibraDatabaseInitializer`'ın gerçekte kurduğu şema arasındaki uyuşmazlıkları (ekranları "Invalid column name" ile patlatan sınıf) release'e çıkmadan yakalamak.

## Neden gerekli
CalibraHub EF migration kullanmaz; DB şeması her açılışta idempotent `CalibraDatabaseInitializer` ile "olması gereken duruma yakınsar". Bir DB değişikliği bu initializer'a işlenmezse, güncellenen bilgisayarın DB'si eski adda kalır ve repo yeni kolonu sorunca patlar. Bu denetim tam bu boşluğu, **gerçek şemayla** (tahmin değil) kapatır. (Kök neden geçmişi: `MigrateColumnRenamesAsync`/`MigrateTableRenamesAsync` tanımlı ama hiç çağrılmayan ölü koddu — bkz. memory `feedback_pascalcase_column_direction`.)

## Nasıl çalıştırılır

1. **Güncel yayın gerekli** (denetim, publish'e karşı çalışır = installer'ın gerçekten dağıtacağı şey). `publish\Web\CalibraHub.Web.exe` güncel değilse önce yayınla:
   - En temizi (setup'ı da üretir): `installer\build-installer.ps1`
   - Hızlı: `dotnet publish src\CalibraHub.Web\CalibraHub.Web.csproj -c Release -r win-x64 --self-contained -o publish\Web`
2. Denetimi ana proje kökünden çalıştır (LocalDB gerekir; production'a dokunmaz, tek-kullanımlık `CalibraSchemaAudit` DB'si kurup siler):
   ```
   powershell -ExecutionPolicy Bypass -File .claude\skills\veritabanikontrol\veritabanikontrol.ps1
   ```
   > 61001 (veya başka bir instance) o an publish'i **build** ediyorsa çakışma olmasın diye denetimi build bittikten sonra çalıştır. Script kendi portunu (61099) kullanır, 61001'e dokunmaz.
3. **Çıktıyı yorumla:**
   - `TEMIZ` / exit 0 → kod ile DB senkron; sıfırdan kurulum kolon/tablo-adı hatası vermez. **Setup'ı yayınlayabilirsin.**
   - `UYUSMAZLIK` / exit 1 → listelenen her referans fresh şemada YOK = release-blocker. Aşağıdaki gibi düzelt, sonra denetimi **tekrar** çalıştır (TEMIZ çıkana dek).
   - `HATA: init...` / exit 1 → init bile temiz tamamlanmadı (ör. sıra bağımlılığı / seed'den önce çalışan sorgu). stdout kuyruğundaki hataya göre initializer'ı düzelt.
   - exit 2 → ön-koşul eksik (publish yok). Adım 1'i yap.
   - **`Error Number:596` / "session is in the kill state" / Class 21** → LocalDB kararsız (çok create/drop döngüsünden). Sıfırla ve tekrar dene: `sqllocaldb stop MSSQLLocalDB` → `sqllocaldb start MSSQLLocalDB`. (Bu koşul canlı SQL Server'da oluşmaz; sadece tekrar tekrar denetim çalıştırınca LocalDB'de görülebilir.)

## Uyuşmazlık nasıl düzeltilir (hedef HER ZAMAN PascalCase)
Her satır: repo `[X]` sorguluyor ama tablo `[X]`'e sahip değil. Yön iki türlü olur:
- **repo snake sorguluyor, DB Pascal** → repo SQL'ini Pascal yap (`[is_active]`→`[IsActive]`). **Cerrahi ol** — aynı tabloda bilinçli snake kalan kolonlar olabilir (`notes.visibility`, `card_group_mappings.entity_type`, `configuration_property_values.feature_id`). `replace_all`'u yalnız tek anlamı olan bracket-token'da kullan; `item_locations` gibi legacy-snake tabloları kırma.
- **repo Pascal sorguluyor, DB snake** → o tablonun `Ensure*` metoduna (`CalibraDatabaseInitializer`, sıra garantili nokta) idempotent rename ekle:
  ```sql
  IF COL_LENGTH(N'[{s}].[tbl]', N'NewName') IS NULL AND COL_LENGTH(N'[{s}].[tbl]', N'old_name') IS NOT NULL
      EXEC sp_rename N'[{s}].[tbl].[old_name]', N'NewName', N'COLUMN';
  ```
- **Gerçek false-positive** (SELECT alias, interpolated `{_table}`, repo'nun kendi lazy-create ettiği tablo, `sys.*` katalog kolonu, harici Netsis) → script içindeki `$KnownFalsePositives` listesine `Dosya.cs::token` olarak ekle ki bir daha gürültü yapmasın.

Düzeltmelerden sonra: `dotnet build` (0 hata) → 61001'i yeni kodla yeniden başlat (canlı DB'ye migration uygulansın) → setup'ı yeniden derle.

## Kapsam / sınır (dürüst)
- **Yakalar:** repo ↔ şema kolon/tablo-adı uyuşmazlıkları (Invalid column/object sınıfı) + init'in temiz tamamlanıp tamamlanmadığı.
- **Yakalamaz:** mantık hataları, eksik seed verisi. İnterpolated tablo adı kullanan repo'lar (Document/InventoryCount/StockDoc — konsolidasyonda Pascal-native yazılmış) otomatik diff'e tam girmez; şüphede o repo'ların SQL'ini elle spot-doğrula.

## Referans
Bu denetim 2026-07-02'de **11 gizli-bozuk tabloyu** yakaladı: whatsapp_config, notes, wa_inbox, currencies, design_templates, document_types, integration_api_profiles, sales_representatives, card_group_mappings, configuration_property_values, DocumentSource. Fix yönü ve detay: memory `feedback_pascalcase_column_direction` + `project_fresh_db_init`.
