---
name: sekmeli-form
description: |
  CalibraHub veri giriş ekranı oluşturur — solda sekmeler (sabit + WidgetMas
  GroupCaption'a göre dinamik), sağda seçili sekmenin alanları, üstte sticky
  aksiyon şeridi (Geri / Kaydet). Kullanıcı "X için veri giriş ekranı tasarla",
  "tab'lı form", "sekmeli düzenleme ekranı", "edit ekranı oluştur" / "yeni edit
  sayfası" gibi ifadeler kullandığında tetikle. Yeni bir entity için tanım /
  düzenleme / edit cshtml + controller endpoint'i yazılacaksa bu pattern
  zorunludur.
---

# sekmeli-form

CalibraHub'da bütün veri giriş (tanım/düzenleme) ekranları aynı pattern'i
takip eder: solda dikey sekme menüsü, sağda seçili sekmenin formu, üstte
sticky aksiyon şeridi. Bu skill bu pattern'in kurallarını ve referans
implementasyonlarını verir.

## Genel iskelet

```
┌──────────────────────────────────────────────────────────┐
│ [< Geri] [Kayıt adı]              [Aktif ⓘ]  [Kaydet]   │  ← sticky action bar
├────────────────┬─────────────────────────────────────────┤
│ ▸ Genel        │                                          │
│   Detay        │       Seçili sekmenin form alanları      │
│   Adres        │       (form-row layout)                  │
│ ────           │                                          │
│   Özel Alanlar │                                          │
│   (dinamik)    │                                          │
└────────────────┴─────────────────────────────────────────┘
```

## Yapısal kurallar

### Layout
- Container class: `.st-modal-body--tabbed` (sol 220px tab + sağ flex içerik)
- Sol panel: `.st-tabs` — sabit sekmeler ÖNCE (Genel, Detay vb.), sonra
  WidgetMas'tan `GroupCaption` ile gruplanan dinamik sekmeler
- Sağ panel: `.st-tab-pane` — aktif olan `.is-active`
- Üstte sticky aksiyon şeridi: `.ap-action-bar` pattern'i
  (Geri Dön / kayıt adı / Aktif switch / Kaydet)
- Tüm yükseklik 100%, scroll sadece sağ pane içinde

### Sol sekme listesi
```html
<aside class="st-tabs">
  <button class="st-tab is-active" data-pane="general">Genel</button>
  <button class="st-tab"            data-pane="detail">Detay</button>

  @* Dinamik gruplar (WidgetMas.GroupCaption distinct) *@
  @foreach (var grup in widgetGruplari) {
    <button class="st-tab" data-pane="@grup.Slug">@grup.Caption</button>
  }
</aside>
```

### Sağ pane'ler
```html
<main class="st-content">
  <section class="st-tab-pane is-active" id="st-pane-general">
    <div class="form-row">
      <label>Ad</label>
      <input class="form-input" id="fName" />
    </div>
    ...
  </section>
  <section class="st-tab-pane" id="st-pane-detail">...</section>
  @foreach (var grup in widgetGruplari) {
    <section class="st-tab-pane" id="st-pane-@grup.Slug">
      @foreach (var w in grup.Widgets) {
        @* widget tipine göre render *@
      }
    </section>
  }
</main>
```

## Dinamik alan kaynağı (WidgetMas)

1. **Controller'da çek:**
   ```csharp
   var widgets = await _widgetService.GetWidgetsByFormAsync("MY_FORM_CODE", ct);
   var widgetGruplari = widgets
     .Where(w => !string.IsNullOrWhiteSpace(w.GroupCaption))
     .GroupBy(w => w.GroupCaption!.Trim())
     .OrderBy(g => g.Key)
     .Select(g => new {
       Caption = g.Key,
       Slug    = SlugHelper.ToSlug(g.Key),
       Widgets = g.OrderBy(w => w.SortOrder).ToList()
     })
     .ToList();
   ViewBag.WidgetGruplari = widgetGruplari;
   ```

2. **Render — widget tipine göre kontrol seç:**
   - `Text` / `NumericText` → `<input>`
   - `Date` → date picker
   - `Boolean` → **switch (toggle)** — native checkbox YASAK
   - `Guide` (standart rehber) → `cbv_Guide_*` view + guideLookup widget
   - `Lookup` (özel alan rehberi) → admin tanımlı GuideMas

3. **Save sırasında**: dinamik widget değerlerini `WidgetTra` tablosuna
   `IWidgetService.SaveValuesAsync(formCode, entityId, values, ct)` ile yaz.

## Boolean alanlar — switch zorunlu

CalibraHub'da native `<input type="checkbox">` ham haliyle UI'a düşmez.
Aktif/Pasif gibi tüm açma-kapama girişleri **switchkey (toggle)** ile gösterilir:

```html
<label class="ap-switch">
  <input type="checkbox" id="fActive" checked />
  <span class="ap-switch-track is-on">
    <span class="ap-switch-thumb"></span>
  </span>
  <span class="ap-switch-label is-active">Aktif</span>
</label>
```

Veya Bootstrap `form-check form-switch`. Track + sliding thumb pattern'i.

## Aksiyon şeridi (sticky)

```html
<div class="ap-action-bar" id="ap-action-bar">
  <button type="button" class="ap-btn" onclick="history.back()">
    <svg>...</svg> Geri
  </button>
  <span class="ap-meta">
    <strong id="ap-record-name">Yeni Kayıt</strong>
  </span>
  <!-- Aktif switch buraya da konabilir -->
  <button type="button" class="ap-btn ap-btn--primary" id="ap-save-btn">
    <svg>...</svg> Kaydet
  </button>
</div>
```

**KITT animasyonu**: kaydet başarılı olunca aksiyon şeridi altında yeşil
tarayıcı ışık çalıştır — class ekle: `actionBar.classList.add('is-save-flash')`,
1850ms sonra kaldır. CSS zaten `Views/Admin/Parameters.cshtml` içinde tanımlı,
referans alabilirsin.

## Tab geçişi JS

```js
var tabs = document.querySelectorAll('.st-tab');
var panes = document.querySelectorAll('.st-tab-pane');
tabs.forEach(function (t) {
  t.addEventListener('click', function () {
    tabs.forEach(function (x) { x.classList.toggle('is-active', x === t); });
    panes.forEach(function (p) {
      p.classList.toggle('is-active', p.id === 'st-pane-' + t.dataset.pane);
    });
  });
});
```

## Save endpoint

- POST endpoint → JSON body: `{ id, name, isActive, ...sabit alanlar,
  widgetValues: { widgetCode: value, ... } }`
- Backend validation + frontend validation
- Başarılı → toast (sağ üst) + KITT flash + güncel kaydet butonu state'i
- Hata → toast (kırmızı) + ilgili input shake animasyonu (varsa)

## Tema kuralları

- CSS değişkenleri kullan: `--app-surface`, `--app-border`, `--app-text`,
  `--app-bg`, `--app-accent` vb.
- **Hardcoded hex YASAK**. Bütün renkler değişken üzerinden.
- `body.app-theme-dark` override otomatik çalışsın
- Native form kontrolleri için `color-scheme: light` / `dark` set et

## Silme onayı (varsa)

Native `confirm()` / `alert()` **kullanılmaz** (sayfa üst kenarında çıkar,
tema uyumsuz). Custom modal: ortalanmış card + danger ikon + Vazgeç / Sil
butonları. Promise tabanlı `showConfirm({ title, message, okLabel })` helper.
Referans: `Views/PriceList/Report.cshtml` → `.plr-modal-backdrop`.

## Referans implementasyonlar

Yeni ekran yazarken birinden başlayıp kopyala/uyarla:

- **`Views/Production/PersonnelEdit.cshtml`** — sol tab + dinamik widget
  grupları + audit alanları
- **`Views/Logistics/MachineEdit.cshtml`** — uniqueness validation + lokasyon
  ağacı seçimi + dinamik widget
- **`Views/Production/ShiftEdit.cshtml`** — sol tab pattern + iç içe alt-tab
  (Vardiya + Molalar ayrı sekmelerde)
- **`Views/ApprovalFlow/Edit.cshtml`** — sticky action bar + KITT flash + toast
- **`Views/Admin/Parameters.cshtml`** — saf sol tab (sadece sabit sekmeler,
  dinamik widget yok); minimal başlangıç noktası

## Kontrol listesi (yeni ekran yazarken)

- [ ] `.st-modal-body--tabbed` container
- [ ] Sol `.st-tabs` — sabit sekmeler + WidgetMas grupları
- [ ] Sağ `.st-tab-pane` — `is-active` toggle JS
- [ ] Üst sticky `.ap-action-bar` — Geri / ad / Kaydet
- [ ] Boolean alanlar **switch**, native checkbox YOK
- [ ] CSS değişken sistemi — hardcoded hex YOK
- [ ] Silme / destruktif → custom modal (Promise helper)
- [ ] Backend: dinamik widget değerlerini `WidgetTra` üzerinden save
- [ ] Toast (`#toast` veya `.afe-toast`) — başarı / hata
- [ ] KITT flash — save başarılı animasyonu
- [ ] Light + dark tema kontrolü
