# CalibraHub — Test & Kural Uyum Raporu

> Bu doküman, CalibraHub'ın bir kullanıcı gibi uçtan uca test edilmesi ve `CLAUDE.md`
> içindeki proje kurallarına uyumun denetlenmesi için kullanılır.
> Her ekran için: (1) smoke test (açılıyor mu / hata var mı), (2) kural-uyum kontrolü.
>
> **Başlangıç:** 2026-06-04 · **Test eden:** Claude (Cowork) · **Ortam:** localhost:61001

---

## Kontrol edilecek kurallar (CLAUDE.md özet checklist)

Her liste/tanım ekranında aşağıdaki maddeler denetlenir:

| # | Kural | Açıklama |
|---|-------|----------|
| K1 | **SmartBoard / C-Grid** | Tüm liste ekranları C-Grid standardında mı? |
| K2 | **C-Grid header düzeni** | `[İkon] Başlık + alt başlık` · Arama · Filtre · Excel · Widget · Ana eylem sırası |
| K3 | **In-place refresh** | Değişiklikte `window.location.reload()` yerine `refreshBoard()` kullanılıyor mu? |
| K4 | **Switch (toggle)** | Boolean alanlar native checkbox değil, switch/toggle ile mi? |
| K5 | **Silme onay modalı** | Ortada custom modal mı? (native `confirm()`/`alert()` yasak) |
| K6 | **Kod alanı yok** | Tanım ekranlarında kullanıcıya "Kod" inputu gösterilmiyor; isim üzerinden uniqueness |
| K7 | **Standart rehber** | Rehber alanları ValueColumn=Code / DisplayColumn=Name davranışı |
| K8 | **Konsol temiz** | Sayfa yüklenince JS hatası / 404 / 500 yok |
| K9 | **Tema uyumu** | Tüm sayfa + içeriden açılan modal + rehberler hem aydınlık hem karanlık temada doğru (sabit/hardcoded renk yok) — *kullanıcı kriteri* |

> Not: K6, K7 gibi kurallar yalnızca ilgili ekran tipinde (tanım/rehber) geçerlidir.

---

## DEVİR-TESLİM — derleyen agent için

> Bu bölüm, kod değişikliklerini derleyip çalıştıran agent içindir. Aşağıdaki ilk grup
> **zaten diskte uygulandı** — derleyip (`dotnet run --project src/CalibraHub.Web`) doğrulaman yeterli.
> İkinci grup **öneri/bekleyen** — onay/uygulama gerektirir.

### A) Bu oturumda diskte UYGULANAN değişiklikler (derle + doğrula)
1. **`src/CalibraHub.Web/wwwroot/js/site.js` — KRİTİK ONARIM + fix**
   - *Onarım:* Dosya sonu kesikti (truncated). `setupInlineEditNavigation`'ın son click-handler'ından
     sonraki **tüm `setupX()` çağrıları + `setupOpenWindowsManager` tanımı + IIFE `})();` kapanışı**
     eksikti (brace dengesi açık, `node --check` "Unexpected end of input"). Son sağlam (HEAD) sürümden
     geri getirildi → `node --check` ✓, denge 0/0/0. **Bu olmadan tam yenilemede tüm ön yüz JS çökerdi.**
   - *Fix:* Açık-sekme başlığı artık sayfanın gerçek başlığından okunuyor — `readFrameTitle` +
     `reconcileTabTitle` (iframe `load`'da `body[data-page-title]` ile uzlaştırma). Eski yanlış
     (action-adı) localStorage kayıtlarını da self-heal eder.
2. **React bundle cache-bust (6 liste view)** — `?v=@(DateTime.UtcNow.Ticks)` → `IFileVersionProvider.AddFileVersionToPath(...)` ve `@inject IFileVersionProvider`:
   `Production/Personnel`, `Production/Operations`, `Production/Routings`, `Production/ActivityReasons`, `Production/Shifts`, `Logistics/Machines`.
   (Sekme geçişinde bundle yeniden indirilmesini/yenilenme animasyonunu giderir.)
3. **"Yeni" ekranlarda alt sekme çubuğu (4 edit view)** — `<partial name="_ProductionDefsTabs" .../>` eklendi:
   `Production/OperationEdit` (operations), `Production/PersonnelEdit` (personnel), `Logistics/MachineEdit` (machines), `Production/RoutingEdit` (routings).
   (ActivityReasonEdit + ShiftEdit'te zaten vardı.)

> ⚠️ Not: `.cshtml` değişiklikleri build-time derlenir; `site.js` ise `asp-append-version` hash'i mount-FS'te
> bayatlayabildiği için **temiz bir restart** (process'i durdur + `dotnet run`) gerekir.

### B) ÖNERİLEN / BEKLEYEN düzeltmeler (onay gerektirir)
- **K9 tema — yüksek:** `RuleBuilderModal.jsx` dark-only (light override yok); `Purchase/FulfillmentCenter.cshtml` + `FulfillModal.cshtml` dark-only. (Detay aşağıda K9 bölümünde, file:line ile.)
- **K9 tema — toplu:** İki tekrarlayan kalıp (DB-şema tooltip ~34 dosya, mount-hata fallback div ~36 dosya) → paylaşılan partial + `--theme-*` sınıfına taşınmalı.
- **K6 ihlali:** `Production/OperationEdit.cshtml` formunda **"Kod" inputu** var → "kullanıcı kod girmez" kuralına aykırı, kaldırılmalı (kod backend'de auto-türetilir). Diğer tanım edit ekranları da kontrol edilmeli.
- **Bundle cache (opsiyonel):** Aynı `Ticks` deseni kalan ~57 view'da da mevcut; istenirse geneline yayılabilir.

---

## Ekran haritası

Aşağıdaki tablo controller/view taramasından çıkarıldı. `Tip` sütunu beklenen ekran türünü gösterir
(L = liste/C-Grid, E = düzenleme formu, D = dashboard/özel, S = sistem/ayar).

### Ana modüller (sol menüden erişilen)

| Modül | Ekran | URL | Tip | Smoke | Kural notu |
|-------|-------|-----|-----|:-----:|-----------|
| Dashboard | Ana Sayfa | `/Dashboard/Index` | D | ⬜ | |
| Dashboard | Grafana | `/Dashboard/Grafana` | D | ⬜ | |
| Logistics | Malzeme Kartları | `/Logistics/MaterialCards` | L | ⬜ | |
| Logistics | Malzeme Grupları | `/Logistics/MaterialGroups` | L | ⬜ | |
| Logistics | Makineler | `/Logistics/Machines` | L | ⬜ | Referans C-Grid |
| Logistics | Lokasyonlar | `/Logistics/Locations` | L | ⬜ | |
| Logistics | Ölçü Birimleri | `/Logistics/Units` | L | ⬜ | |
| Logistics | BOM'lar | `/Logistics/BOMs` | L | ⬜ | |
| Logistics | Ürün Konfigürasyonu | `/Logistics/ProductConfiguration` | D | ⬜ | |
| Logistics | Kombinasyonlar | `/Logistics/Combinations` | L | ⬜ | |
| Production | Operasyonlar | `/Production/Operations` | L | ⬜ | Referans C-Grid |
| Production | Personel | `/Production/Personnel` | L | ⬜ | Referans C-Grid |
| Production | Rotalar (Routing) | `/Production/Routings` | L | ⬜ | RoutingTree (custom) |
| Production | İş Emirleri | `/Production/WorkOrders` | L | ⬜ | |
| Production | Vardiyalar | `/Production/Shifts` | L | ⬜ | |
| Production | Aktivite Nedenleri | `/Production/ActivityReasons` | L | ⬜ | |
| Production | Vardiya Atamaları | `/Production/ShiftAssignments` | D | ⬜ | |
| Production | Saha (ShopFloor) | `/Production/ShopFloor` | D | ⬜ | |
| Sales | Teklifler/Belgeler | `/Sales/Documents` | L | ⬜ | |
| Sales | Siparişler | `/Sales/Orders` | L | ⬜ | |
| Purchase | Tedarik Merkezi | `/Purchase/FulfillmentCenter` | D | ⬜ | |
| Warehouse | Stok Girişi | `/Warehouse/StockEntry` | D | ⬜ | |
| Warehouse | Transfer | `/Warehouse/Transfer` | D | ⬜ | |
| Finance | Cariler (Contacts) | `/Finance/Contacts` | L | ⬜ | |
| PriceList | Fiyat Grupları | `/PriceList/PriceGroups` | L | ⬜ | |
| PriceList | Fiyat Listesi | `/PriceList/PriceList` | L | ⬜ | |
| PriceList | Rapor | `/PriceList/Report` | D | ⬜ | Silme modalı referansı |
| Definitions | Kart Grupları | `/Definitions/CardGroups` | L | ⬜ | |
| GeneralDefinitions | Cari Grupları | `/GeneralDefinitions/CariGroups` | L | ⬜ | |
| GeneralDefinitions | Para Birimleri | `/GeneralDefinitions/Currencies` | L | ⬜ | |
| GeneralDefinitions | Satış Temsilcileri | `/GeneralDefinitions/SalesRepresentatives` | L | ⬜ | |
| Approval | E-Belge Onay | `/Approval/Index` | L | ⬜ | |
| PendingApproval | Bekleyen Onaylar | `/PendingApproval/Index` | L | ⬜ | |
| Integrations | Entegrasyonlar | `/Integrations/Index` | L | ⬜ | |
| Integrations | Kuyruk | `/Integrations/Queue` | L | ⬜ | |
| Integrations | Çalışmalar | `/Integrations/Runs` | L | ⬜ | |
| Notes | Notlar | `/Notes/Index` | D | ⬜ | |
| OrgChart | Organizasyon Şeması | `/OrgChart/Index` | D | ⬜ | |
| MailSend | Posta | `/MailSend/Index` | L | ⬜ | |
| WhatsApp | WhatsApp | `/WhatsApp/Index` | D | ⬜ | |
| Reporting | Rapor Tasarımcısı | `/Reporting/Designer` | D | ⬜ | |
| DocDesigner | Belge Tasarımcısı | `/DocDesigner/Index` | D | ⬜ | |

### Admin / Sistem ayarları

| Ekran | URL | Tip | Smoke | Kural notu |
|-------|-----|-----|:-----:|-----------|
| Admin Ana | `/Admin/Index` | S | ⬜ | |
| Kullanıcılar | `/Admin/Users` | L | ⬜ | |
| Roller | `/Admin/Roles` | L | ⬜ | |
| Departmanlar | `/Admin/Departments` | L | ⬜ | |
| Şirket Ayarları | `/Admin/CompanySettings` | S | ⬜ | |
| Parametreler | `/Admin/Parameters` | S | ⬜ | |
| Görünüm/Etiketler | `/Admin/Appearance` | S | ⬜ | |
| Görünüm Ayarları | `/Admin/ViewSettings` | S | ⬜ | |
| Form Yönetimi | `/Admin/FormManagement` | L | ⬜ | |
| Zamanlanmış Görevler | `/Admin/ScheduledTasks` | L | ⬜ | |
| ERP Ayarları | `/Admin/ErpSettings` | S | ⬜ | |
| Entegratör Ayarları | `/Admin/IntegratorSettings` | S | ⬜ | |
| Mail Ayarları | `/Admin/MailSettings` | S | ⬜ | |
| Kilitler | `/Admin/Locks` | S | ⬜ | |
| Loglar | `/Admin/Logs` | S | ⬜ | |
| DB Şema | `/Admin/DbSchema` | S | ⬜ | |
| Onay Akışları | `/ApprovalFlow/Index` | L | ⬜ | |
| SQL Sorgu Kütüphanesi | `/ApprovalSqlQuery/SqlQueryLibrary` | S | ⬜ | |
| Belge No Kuralları | `/DocumentNumberRule/Index` | L | ⬜ | |
| Belge Layout Kuralları | `/DocLayoutRule/Index` | L | ⬜ | |

---

## Bulgular

> Test ilerledikçe bu bölüm doldurulacak. Format:
> **[Önem]** Ekran — Bulgu — (ekran görüntüsü ref)
> Önem: 🔴 Kritik (hata/çökme) · 🟠 Orta (kural ihlali) · 🟡 Düşük (kozmetik) · 🟢 OK

### 🔴 KRİTİK — `site.js` diskte bozuk/yarım kalmıştı (onarıldı)
Test sırasında `wwwroot/js/site.js`'in çalışma kopyası **sonu kesilmiş** halde bulundu:
`setupInlineEditNavigation` içindeki click handler'dan sonrası (tüm `setupX()` çağrıları +
`setupOpenWindowsManager` tanımı + IIFE `})();` kapanışı) kaybolmuştu. Brace dengesi `{}`+2,
`()`+1 açık; `node --check` "Unexpected end of input" veriyordu. Bu dosya bir sonraki tam
sayfa yenilemesinde **tüm ön yüz JS'ini çökertirdi** (tarayıcı şu an cache'li sağlam kopyayla çalışıyordu).
→ **Onarıldı:** Eksik kuyruk son sağlam (committed/HEAD) sürümden geri getirildi; `node --check` ✓,
brace dengesi 0/0/0. Ortadaki mevcut değişiklikler korundu.

### 🟠 Açık-sayfa sekme başlıkları teknik/route adı gösteriyor (fix yazıldı)
**Bulgu:** Üstteki açık-sekme çubuğunda başlıklar ekranın Türkçe başlığı yerine URL action adı
çıkıyordu: `MachineEdit` (olması gereken "Yeni Makine"), `Operations` ("Operasyon Tanımlamaları"),
`Machines` ("Makine Tanımlamaları"). `document.title` de aynı yanlış değerle eziliyordu.
**Kök sebep (`site.js` ~1526):** sekme açılırken başlık, hedef sayfanın gerçek başlığından
(`ViewData["Title"]` / `body[data-page-title]`) değil **tıklanan linkin metninden** türetiliyor;
yanlış değer localStorage'a yazılıp kalıcılaşıyor.
**Fix (yazıldı, sağlam):** iframe `load` olayında sayfanın otoriter `data-page-title`'ı okunup sekme
başlığı **deterministik olarak uzlaştırılıyor** (`readFrameTitle` + `reconcileTabTitle`); eski/yanlış
kayıtları da kendiliğinden iyileştirir. Mimariye uygun, heuristik yok.
**Durum:** Sunucu cache-bypass'ta yeni JS'i veriyor (doğrulandı). Aktif olması için tarayıcı
hard-refresh veya uygulama restart gerekiyor (mount FS'te versiyon-hash cache'i bayatladı).

### 🟠 Sekme geçişinde React bundle yeniden indiriliyor (yenilenme animasyonu) — fix yazıldı (kapsam: Üretim Tanımlamaları grubu)
**Bulgu:** Üretim Tanımlamaları alt sekmeleri (Personel/Operasyon/Makine/Rota/Aktivite/Vardiya)
arasında gezerken her seferinde bir yenilenme/skeleton animasyonu oynuyordu; tab çubuğu sabit kalmıyordu.
**Kök sebep:** Bu ekranlarda React bundle `'/react/calibrahub-widgets.js?v=@(DateTime.UtcNow.Ticks)'`
ile yükleniyordu. `DateTime.UtcNow.Ticks` her render'da benzersiz → bundle **hiç cache'lenmiyor**,
SPA geçişlerinde her seferinde yeniden indirilip SmartBoard sıfırdan mount ediliyordu. (Aynı desen
projede **63 view / 124 satırda** mevcut — performans açısından da genel sorun.)
**Fix (yazıldı):** 6 ekranda `IFileVersionProvider.AddFileVersionToPath(...)` kullanıldı (site.js'in
`asp-append-version` yöntemiyle aynı, içerik-hash tabanlı). Bundle artık cache'lenir; yalnızca
`npm run build` ile gerçekten değişince (ve restart sonrası) yenilenir. → grup içi geçişlerde yeniden
indirme/mount olmaz.
**Kapsam kararı:** Önce yalnızca bu ekran grubu (kullanıcı onayı). Kalan 57 view sonraki adımda
aynı desenle genele yayılabilir.
**Durum:** Restart sonrası canlı doğrulanacak.

### 🟠 "Yeni X" ekranlarında alt sekme çubuğu kayboluyordu — fix yazıldı (Üretim Tanımlamaları grubu)
**Bulgu:** Üretim Tanımlamaları grubunda "+ Yeni" ile açılan düzenleme ekranlarında (Yeni Operasyon,
Yeni Personel, Yeni Makine, Yeni Rota) üstteki alt sekme çubuğu (Personel/Operasyon/Makine/Rota/
Aktivite/Vardiya) görünmüyordu; bağlam kayboluyordu.
**Fix (yazıldı):** İlgili 4 edit view'a `<partial name="_ProductionDefsTabs" model='"..."' />` eklendi
(doğru aktif sekme ile). ActivityReasonEdit + ShiftEdit'te zaten vardı → grubun 6 edit ekranının
tamamında çubuk artık sabit kalıyor.
**Durum:** Restart sonrası canlı doğrulanacak.

### 🟠 K6 İHLALİ — Operasyon edit ekranında "Kod" inputu var
**Bulgu:** "Yeni Operasyon" formunda kullanıcıya **"Kod *"** girişi gösteriliyor (placeholder
"OP-CUT, OP-WELD"). CLAUDE.md "Kullanıcı kod girmez" kuralına aykırı — tanım ekranlarında Kod inputu
olmamalı, uniqueness isim üzerinden sağlanmalı, kod backend'de auto-türetilmeli.
**Karşılaştırma:** "Yeni Makine" ekranı kurala **uygun** (Kod alanı yok, sadece Makine Adı). Operasyon
ekranı uyumsuz. (Diğer tanım edit ekranları da ayrıca kontrol edilmeli: Personel, Rota, Vardiya, Aktivite.)
**Durum:** Bildirildi — kullanıcı onayıyla ayrıca düzeltilebilir.

### K9 — Tema (aydınlık/karanlık) uyum denetimi (statik kod taraması)
**Tema mekanizması:** İki geçerli yöntem — Razor: `body.app-theme-light/-dark`; React: `html.dark`.
Doğru desen: `--theme-*` değişkenleri **veya** light-default + `body.app-theme-light`/`.dark` override.

**🔴 Yüksek öncelik (modal/rehber — kullanıcıyı etkiler):**
- `ClientApp/.../AdminWidgetRegistry/RuleBuilderModal.jsx` — **dark-only**: `colorScheme:'dark'` (566) + metinler `rgba(255,255,255,..)` (318, 825, 868, 1166, 1429-1432, 1529-1573…), `<option>` zeminleri `#0a0e1a` (619,624). Aydınlık temada beyaz metin açık zeminde okunmaz. ~29 inline ihlal, light override yok.
- `GuideLookup/guideLookup.css:219-220` — `.gl-popover-divider #cbd5e1` / `.gl-popover-count #94a3b8` dark override eksik (düşük etki, doğrulanmalı).
- `OptionsModal.jsx`, `GuideCustomizationModal.jsx` — birkaç inline sabit (doğrulanmalı).

**🟠 Orta öncelik (sayfa — dark-only, light override yok):**
- `Purchase/FulfillmentCenter.cshtml` — `.fc3-*` sabit slate renkleri (~102 hex), `app-theme-light` override yok → aydınlıkta sorun.
- `Purchase/FulfillModal.cshtml` — modal içeriği dark-only (`#e2e8f0`, `#cbd5e1`, `#94a3b8`), override yok.
- `EngineTest/Index.cshtml` (light-only, dev sayfası), `Shared/Error.cshtml` (dark standalone) — düşük.

**🟡 Tekrarlayan düşük-riskli kalıplar (toplu):**
- DB-şema info-tooltip HTML stringi (`#2563eb`/`#e2e8f0`) — **~34 dosyada** (tüm C-Grid listeleri).
- JS mount-hata fallback div'i (`#0c0f1a`/`#f87171`/`#6366f1`) — **~36 dosyada** (yalnız bundle yüklenemezse görünür).

**✅ İyi referanslar:** `PriceList/PriceList.cshtml` (dark + `body.app-theme-light` override), `GuideLookup/guideLookup.css` (`.dark` override'lı rehber standardı), `CostViewerModal.jsx` (isLight ternary).

**Önerilen tek-noktadan düzeltme:** İki tekrarlayan kalıbı paylaşılan partial + site.css sınıfına (`--theme-*` ile) taşı → ~70 dosyadaki sabit renk tek yere iner, otomatik tema-uyumlu olur. RuleBuilderModal ve FulfillmentCenter için light override seti (PriceList desenini kopyala).

### Smoke test sonuçları
- `/Production/Operations` — açıldı, hata yok, C-Grid render ✓ (3 operasyon kartı)
- `/Logistics/Machines` — açıldı, konsol hatası yok ✓
- `/Logistics/MachineEdit` (Yeni Makine) — açıldı ✓

### Kural uyum gözlemleri (Yeni Makine ekranı)
- **K4 (switch):** ✓ "Durum" alanı toggle switch ile (checkbox değil) — kurala uygun.
- **K6 (kod alanı yok):** ✓ Formda "Kod" inputu yok; sadece "Makine Adı". Kurala uygun.
- Header: "← Listeye Dön / Kaydet (primary) / İptal" — sticky save bar standardı ✓.

---

## İlerleme

- [ ] Test ortamı hazır (Chrome bağlı + login)
- [x] Smoke test — ana modüller (60 endpoint, 0 × 500)
- [x] Smoke test — admin/sistem (302 auth redirect — sağlıklı)
- [ ] Kural uyum denetimi — C-Grid ekranları
- [ ] CRUD akış testleri (stok kartı açma vb.)

---

## 2026-06-04 Aksiyon Logu — Çözülen ve eklenenler

> Bu bölüm Bulgular kısmındaki maddelere karşılık üretilen kod değişikliklerini özetler.

### ✅ K9 — RuleBuilderModal tema-duyarlı (kritik)
**Dosya:** `ClientApp/.../AdminWidgetRegistry/RuleBuilderModal.jsx`
**Değişiklik:** `useThemeIsLight()` hook + `themePalette(isLight)` ile ~80 inline renk noktası tek noktaya toplandı (text/textStrong/textMute/panel/surface/inputBg/dropdownBg/border/backdrop/shadow/accent vb. 26 token). 3 alt-component (FieldDropdown/OperatorDropdown/ArithmeticOperatorDropdown) ayrı `isLight` prop'u alıyor. `colorScheme:'dark'` → dinamik. `<option>` zeminleri tema-duyarlı. **Light tema okunabilir.**

### ✅ K9 — Purchase/FulfillmentCenter + FulfillModal light override
**Dosyalar:** `Views/Purchase/FulfillmentCenter.cshtml`, `Views/Purchase/FulfillModal.cshtml`
**Değişiklik:** FulfillmentCenter `<style>` sonuna ~150 satır `body.app-theme-light` override eklendi (~75 selector). FulfillModal inline style'lardan `.fm-*` class-tabanlı yapıya refactor + 30 light override kuralı. Status badge renkleri (`StatusClass()`) light tema kontrast değerleri ile (slate-700, emerald-700, red-700 vb.).

### ✅ K9 — OptionsModal (GlassSelect) tema-duyarlı
**Dosya:** `ClientApp/.../AdminWidgetRegistry/OptionsModal.jsx`
**Değişiklik:** `useThemeIsLight()` hook + `t.*` palette (12 token: selBg/hoverBg/text/hint/check vb.). GlassSelect dropdown light/dark ikisinde okunabilir.

### ✅ K9 — GuideLookup popover light/dark
**Dosya:** `ClientApp/.../GuideLookup/guideLookup.css` (line 219+)
**Değişiklik:** `.gl-popover-divider` ve `.gl-popover-count` için `.dark` override eklendi (eskiden sadece light default vardı, dark'ta soluk slate okunmuyordu).

### ✅ K6 — Operasyon/Routing/Activity/Shift "Kod" inputu toplu temizlik
**Dosyalar (8):**
- `Application/Services/OperationService.cs` · `RoutingService.cs` · `ActivityReasonService.cs` · `ShiftService.cs`
- `Views/Production/OperationEdit.cshtml` · `RoutingEdit.cshtml` · `ActivityReasonEdit.cshtml` · `ShiftEdit.cshtml`

**Değişiklik:** Service'lerden `Code` zorunlu kontrolü kaldırıldı, Name uniqueness (case-insensitive, kendisi hariç) eklendi, `DeriveCode(name)` auto-derive helper'ı eklendi (50 char trim). Update'te `existing.Code` korunuyor. View'lardan "Kod *" label + input bloğu silindi; JS handler `code = name` set ediyor. Hata mesajı: `"Aynı isimde başka bir {entity} zaten tanımlı: '{name}'"`.

### ✅ Smoke automation (60 endpoint)
**Sonuç:** 0 × 500 (sıfır server hatası). 57 × 302 (auth redirect — sağlıklı). 2 × 200 (ShopFloor + WhatsApp — AllowAnonymous). 1 false-positive 404 (`/ApprovalSqlQuery/SqlQueryLibrary` → gerçek URL `/Admin/SqlQueryLibrary` — `[Route("/Admin/[action]")]`).

### ✅ Mimari iyileştirme — Üretim Tanımlamaları URL rename
**Dosyalar:** `ProductionController.cs`, `MenuDefinition.cs`, `AiChatService.cs`, `SchemaProbeRegistry.cs`
**Değişiklik:** `/Production/Personnel` → `/Production/Definitions` (sayfa 6 tab içerdiği için anlamlı). Eski URL 301 redirect ile backward-compat.

### ✅ Mimari iyileştirme — Alan Rehberi entity-based form selector
**Yeni dosya:** `ClientApp/.../AdminWidgetRegistry/entityRegistry.js`
**Değişiklik:** Backend'in 60 form'undan 49 anlamlı entity tanımı. Header+lines tipi (Satış Teklifi vb.) için variant'lı yapı + segmented control (Üst Bilgi / Kalem Bilgisi). 13 yeni entity (Notlar, e-Belgeler ×3, Döviz, Lokasyon, Ölçü Birimi, Malzeme Grup, Makine Tipi, Kart Grup, Fiyat Listesi 2-variant, Belge Şablon). `*_NEW` ve liste-only variant'lar `hiddenAliases`'a alındı. ModuleSelector ve AdminWidgetRegistryPanel buna göre refactor.

### ✅ Mimari — DBInfo tooltip + Mount fallback shared CSS
**Dosyalar:** `wwwroot/css/site.css` (+44 satır), yeni `Views/Shared/_DbInfoTable.cshtml`
**Değişiklik:** `.cb-dbinfo-*` (table/row/name/desc — light+dark tema-aware) ve `.cb-mount-error` (+ button) sınıfları eklendi. 3 örnek view migrate edildi (MailSend/Machines/Personnel) — inline 8 satırlık style bloğu `class="cb-mount-error"` tek satıra indi.
**Karar:** 70+ dosya toplu migration kapsam dışı (her view farklı yapı, dedicated PR + manuel görsel doğrulama gerek). Yeni view'lar bu sınıfları kullanır.

### ✅ PJAX — Üretim Tanımlamaları sub-tab geçişlerinde flash giderildi
**Yeni dosya:** `wwwroot/js/production-defs-pjax.js` (~225 satır)
**Değişen dosyalar:** `_ProductionDefsTabs.cshtml` (data-pdt-tab + data-workspace-ignore + body-class), 6 view (Personnel/Operations/Routings/ActivityReasons/Shifts + Logistics/Machines) — content `<div id="pdt-tab-content">` wrapper'ı içine alındı, `@section Styles/Scripts` wrapper'a inline taşındı (PJAX swap'inde script'ler manuel re-execute edilir). `_Layout.cshtml`'e script registration. `mount.jsx`'e `window.CalibraHub.unmountAllInside(rootEl)` helper'ı eklendi.
**Sonuç:** Tab geçişlerinde top tab bar **persistent**; sadece content swap. React mount'ları temiz unmount/remount. History pushState ile URL güncel, popstate destekli. Fetch hata olursa graceful full-nav fallback.

### ✅ Inline Edit MVP — Personel
**Dosyalar:** `ProductionController.cs`, `Personnel.cshtml`, `PersonnelEdit.cshtml`
**Değişiklik:** "Yeni Personel" action URL → `#pe-new` (hash trigger). Personnel.cshtml'e sağdan kayan slide-out panel (960px max) + backdrop + iframe (`#pePanelFrame`) + hashchange/postMessage listener. PersonnelEdit.cshtml `?pe-frame=1` query ile iframe modu algılar → top tabs partial gizlenir, Save/Cancel/Delete → parent'a `postMessage({type:'pe:close', refresh})`. Backward-compat: direct URL erişimi eski davranış (frame query yoksa).
**Kapsam dışı (bilinçli MVP):** Sol tabsheet (widget grupları sidebar) — DynamicWidgetRenderer'a `layout="sidetabs"` prop'u büyük iş, ayrı oturum. Diğer 4 entity (Operasyon/Makine/Rota/Aktivite/Vardiya) aynı pattern, kopyala-yapıştır, kullanıcı onayıyla.

### Üretim Tanımlamaları React bundle cache fix
**Değişiklik:** 6 view'da bundle URL'i `?v=@(DateTime.UtcNow.Ticks)` (her render benzersiz, hiç cache'lenmez) yerine `IFileVersionProvider.AddFileVersionToPath(...)` (content-hash, asp-append-version benzeri) kullanıldı. Bundle artık cache'lenir, gerçekten değişince yenilenir.

---

## Bu oturumda atılan agent sayısı

4 paralel agent (PJAX + DBInfo CSS + Entity Registry + Inline Edit MVP) + inline işler (GuideLookup, RuleBuilderModal, K6, FulfillmentCenter, OptionsModal, URL rename, Smoke automation, Alan Rehberi entity refactor).
**Build sonucu:** 0 hata, mevcut 6-29 nullable warning korundu.
**Smoke sonrası restart:** Sunucu canlı, `/Production/Definitions` 302, PJAX script 200.
