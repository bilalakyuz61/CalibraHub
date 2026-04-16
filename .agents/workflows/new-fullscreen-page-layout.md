---
description: Yeni tam-ekran sayfa layout oluşturma (MaterialCards şablonu)
---

# Yeni Tam-Ekran Sayfa Layout (MaterialCards Pattern)

Bu projede yeni bir tam ekran sayfa oluştururken **daima MaterialCards (`MaterialCards.cshtml`) şablonunu baz al**.
Flex zinciri veya height hesapları için asla tahmin yürütme; aşağıdaki kanıtlanmış kalıbı kullan.

---

## 1. Body Class Tanımla

Razor sayfasının en üstüne:

```razor
@{
    ViewData["Title"] = ViewBag.Title;
    ViewData["BodyClass"] = "page-my-screen";   // TEK benzersiz class
}
```

---

## 2. Styles Section — Sabit CSS Bloğu

```css
/* 1. Tüm sayfa scroll'unu kilitle */
html:has(body.page-my-screen) { height: 100dvh; overflow: hidden; }
.page-my-screen                { height: 100dvh; overflow: hidden; }

/* 2. app-shell: header + footer dışındaki alanı doldur */
.page-my-screen .app-shell {
    height: calc(100dvh - var(--layout-header-height) - var(--layout-footer-height));
    min-height: 0;
    overflow: hidden;
}

/* 3. workspace-tabs: flex içinde, sticky değil */
.page-my-screen .workspace-tabs {
    position: relative; top: unset;
    flex: 0 0 auto;
    border-bottom: 1px solid var(--app-border, #dde3ee);
    z-index: 2;
}
.page-my-screen .workspace-tabs[hidden] {
    display: block !important; min-height: 42px;
    visibility: hidden; pointer-events: none;
}

/* 4. workspace-action-bar: gizle (sayfaya özgü action-bar kullanılıyor) */
.page-my-screen .workspace-action-bar { display: none !important; }

/* 5. app-main: flex sütun */
.page-my-screen .app-main {
    display: flex; flex-direction: column;
    height: 100%; min-height: 0; overflow: hidden;
}

/* 6. app-content-area: kalan alanı kapla */
.page-my-screen .app-content-area {
    flex: 1 1 auto; padding: 0 !important;
    min-height: 0; overflow: hidden;
}

/* 7. integrator-dashboard sarmalayıcı */
.page-my-screen .integrator-dashboard { height: 100%; min-height: 0; }

/* 8. __content: kesin yükseklik (MaterialCards ile birebir) */
.page-my-screen .integrator-dashboard__content {
    width: auto !important; max-width: none !important;
    height: calc(100dvh
                - var(--layout-header-height)
                - var(--layout-footer-height)
                - var(--workspace-tabs-sticky-height, 0px)
                - var(--workspace-action-bar-height, 46px)
                - 2px);
    min-height: 0;
    padding: 0 14px !important;
    margin: 0 !important;
    overflow: hidden;
}
```

---

## 3. HTML Yapısı — Üst Form + Alt Grid (Dikey Split)

```html
<section class="integrator-dashboard integrator-dashboard--wide page-my-screen">
  <div class="integrator-dashboard__content">
    <div class="ms-workspace">   <!-- flex-column, height:100% -->

      <!-- ÜST: Form kartı (sabit yükseklik) -->
      <div class="ms-form-card">
        <div class="ms-action-bar"> ... Kaydet / Sil / Yeni ... </div>
        <div class="px-4 py-3"> ... form alanları ... </div>
      </div>

      <!-- ALT: Grid kartı (kalan tüm yükseklik) -->
      <div class="ms-grid-card">
        <div class="ms-grid-card__bar"> ... Ekle butonu ... </div>
        <div class="ms-grid-card__wrap">  <!-- overflow-y:auto, thead sticky -->
          <table>...</table>
        </div>
      </div>

    </div>
  </div>
</section>
```

### Gerekli CSS (ms-* prefix):

```css
.ms-workspace   { display:flex; flex-direction:column; width:100%; height:100%; min-height:0; gap:8px; }
.ms-form-card   { flex:0 0 auto; background:#fff; border:1px solid var(--app-border,#dde3ee); border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(15,23,42,.06); }
.ms-action-bar  { display:flex; align-items:center; gap:8px; padding:9px 16px; border-bottom:2px solid var(--theme-accent,#3b6fd4); background:#fff; }
.ms-grid-card   { flex:1 1 0; min-height:0; display:flex; flex-direction:column; background:#fff; border:1px solid var(--app-border,#dde3ee); border-radius:12px; overflow:hidden; box-shadow:0 2px 8px rgba(15,23,42,.06); }
.ms-grid-card__bar  { flex:0 0 auto; display:flex; align-items:center; gap:8px; padding:7px 12px; background:var(--app-muted-surface,#f8fafc); border-bottom:1px solid var(--app-border,#dde3ee); }
.ms-grid-card__wrap { flex:1 1 0; min-height:0; overflow-y:auto; overflow-x:auto; }
.ms-grid-card__wrap thead th { position:sticky; top:0; z-index:3; background:#f8f9fa; border-bottom:2px solid #dee2e6; }
```

---

## 4. Dikkat Edilecekler

- `flex: 1 1 0` kullan (auto değil) — `min-height: 0` ile birlikte çalışır
- `overflow: hidden` tüm wrapper'larda zorunlu — `auto` yazarsan zincir kırılır
- Grid wrap'a `min-height: 0` şart — yoksa `flex:1` çalışmaz
- `workspace-action-bar` gizlendiğinde `--workspace-action-bar-height` için CSS variable değeri sıfırlanmaz; `calc()` içinde `46px` varsayılan değeri kullan

---

## 5. Referans Dosya

`d:\JetBrainsRider\Projeler\CalibraHub\src\CalibraHub.Web\Views\Logistics\MaterialCards.cshtml`
— Satır 85-174 arası CSS kalıbı. Yeni sayfa oluştururken bu satırları baz al.
