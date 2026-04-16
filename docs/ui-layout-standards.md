# CalibraHub — Kurumsal UI Layout Standartları

> **Belge Türü:** Mimari Talimat & Tasarım Standartları  
> **Versiyon:** 1.0  
> **Tarih:** 2026-03-22  
> **Kapsam:** Tüm CalibraHub modülleri (Lojistik, Satın Alma, Üretim, vb.)

---

## Referans Ekran Görüntüsü

Aşağıdaki ekran görüntüsü, tüm alan numaralarının gerçek uygulamadaki karşılığını göstermektedir:

![CalibraHub Layout Alanları](../src/CalibraHub.Web/wwwroot/docs/calibrahub-layout-reference.png)

> **Not:** Referans sayfa: **Malzeme Kartları** (MaterialCards.cshtml)  
> Bu sayfanın görsel ve kod yapısı, yeni tüm modüller için **altın standart (golden standard)** olarak kabul edilir.

---

## Alan Haritası

```
┌─────────────────────────────────────────────────────┐
│  ②  Header / Navbar (CalibraHub + Kullanıcı Bilgisi) │
├──────────┬──────────────────────────────────────────┤
│          │  ③  Multi-Tab Şeridi                      │
│  ①       ├──────────────────────────────────────────┤
│  Sidebar │  ④  Standart Aksiyon Barı (Kaydet/Sil/Yeni)│
│  Menü    ├──────────────────────────────────────────┤
│          │                                          │
│          │   ┌─────────────────────────────────┐   │
│          │   │  İÇERİK ALANI (Kırmızı Çerçeve) │   │
│          │   │                                  │   │
│          │   │  Üst: Form (Detay, Görsel, Tab)  │   │
│          │   │  Alt: Grid / Liste               │   │
│          │   │                                  │   │
│          │   └─────────────────────────────────┘   │
└──────────┴──────────────────────────────────────────┘
```

---

## Alan 1 — Navigasyon ve Sidebar

**Referans Görseldeki Konum:** Sol kenar, dikey menü

### Kurallar
- Sol menü **hiyerarşik ağaç yapısında** olmalıdır (sonsuz derinlik, collapse/expand)
- Menü taşması durumunda **kendi Y-ekseni içinde scroll** sağlanmalı, dış layout asla bozulmamalıdır
- **Minify/Expand** durumu global state üzerinden kontrol edilmelidir
- İçerik alanı (Alan 5), menünün açık/kapalı durumuna göre **dinamik olarak genişlemelidir**
- Her modülün alt menü girişleri tıklandığında, ilgili sekme **Alan 3**'te açılmalıdır

### Mevcut Uygulama
```html
<!-- _MainMenu.cshtml içindeki pattern -->
<nav class="app-sidebar">
  <div class="app-nav" ...>        <!-- Scroll kapsayıcı -->
    <ul class="nav-tree">          <!-- Hiyerarşik menü -->
      <li class="nav-group">...
```

---

## Alan 2 — Header / Navbar

**Referans Görseldeki Konum:** En üstte sabit bar

### Kurallar
- Yükseklik `--layout-header-height` CSS değişkeniyle tanımlanmalıdır
- Tüm yükseklik hesaplamalarında (`calc(100dvh - ...)`) bu değişken kullanılmalıdır
- Header içeriği: Logo, uygulama adı, sağda kullanıcı bilgisi

---

## Alan 3 — Multi-Tab Şeridi

**Referans Görseldeki Konum:** Header'ın hemen altında yatay sekme çubuğu (örn. "Malzeme Kartları")

### Kurallar
- **Singleton Pattern:** Her sayfa sistemde yalnızca bir kez açılabilir
- Sol menüden tekrar tıklama → yeni instance değil, mevcut sekmeye odaklan (`activeTab`)  
- Her sekme **Unique ID** ile takip edilmelidir
- Sekmeler arası geçişte veri kaybı olmaması için sayfa durumları hafızada tutulmalıdır
- Yükseklik `--workspace-tabs-sticky-height` CSS değişkeniyle tanımlanmalıdır

---

## Alan 4 — Standart Aksiyon Barı

**Referans Görseldeki Konum:** Tab şeridinin hemen altında buton çubuğu (Kaydet, Sil, Yeni)

### Kurallar
- **Kesin Yerleşim:** Aksiyon butonları **her zaman** bu standarrt "Action Zone" içinde yer almalıdır
- **Yasak:** Sayfaya özgü butonlar içerik alanına (kırmızı çerçeve) asla dağıtılmamalıdır
- Aksiyon barının görünürlüğü ve buton listesi, aktif sekmeye göre **Interface / Event Bus** aracılığıyla dinamik olarak güncellenmelidir
- Stil sabittir: `btn-primary` (Kaydet), `btn-outline-danger` (Sil), `btn-secondary` (Yeni)
- Yükseklik `--workspace-action-bar-height` CSS değişkeniyle tanımlanmalıdır

### Örnek CSS Hesabı
```css
/* Tüm sayfalarda içerik alanı yüksekliği bu formülle hesaplanmalıdır */
.integrator-dashboard__content {
    height: calc(100dvh
        - var(--layout-header-height)
        - var(--layout-footer-height)
        - var(--workspace-tabs-sticky-height, 0px)
        - var(--workspace-action-bar-height, 46px)
        - 2px);
}
```

---

## İçerik Alanı — Kırmızı Çerçeve (Master-Detail View)

**Referans Görseldeki Konum:** Geri kalan tüm ekran

### Yapı Kuralları
1. **İçerik alanı, Alan 4'ün hemen altından başlar ve footer'a kadar uzanır**
2. İç bileşenler (form, grid, tab) **ana iskeletin sınırlarını asla ihlal etmemeli**, dış scroll yapısını bozmamalıdır
3. Tüm sayfalarda `overflow: hidden` uygulanmalı, **yalnızca iç bileşenler kendi içinde scroll** sağlamalıdır

### Standart İç Yapı (Dikey Bölünme)

```
İçerik Alanı (flex: column)
├── ÜST BÖLÜM — Form Kartı (flex: 0 0 auto — sabit yükseklik)
│   ├── Sol: Metin alanları (form fields)
│   └── Sağ: Görsel önizleme / medie alanı
│
└── ALT BÖLÜM — Grid / Liste Kartı (flex: 1 1 0 — kalan tüm alan)
    ├── Bar: İşlem butonları (Ekle, Filtrele vb.)
    └── Tablo: Sticky header, overflow-y auto — footer'a kadar
```

### Standart CSS Pattern (Referans: MaterialCards.cshtml)

```css
/* Sayfa kilit */
html:has(body.page-XXX) { height: 100dvh; overflow: hidden; }
.page-XXX { height: 100dvh; overflow: hidden; }

/* Layout zinciri */
.page-XXX .app-shell    { height: calc(100dvh - var(--layout-header-height) - var(--layout-footer-height)); min-height: 0; overflow: hidden; }
.page-XXX .app-main     { display: flex; flex-direction: column; height: 100%; min-height: 0; overflow: hidden; }
.page-XXX .app-content-area { flex: 1 1 auto; padding: 0 !important; min-height: 0; overflow: hidden; }
.page-XXX .integrator-dashboard { height: 100%; min-height: 0; }
.page-XXX .integrator-dashboard__content {
    height: calc(100dvh - var(--layout-header-height) - var(--layout-footer-height)
        - var(--workspace-tabs-sticky-height, 0px) - var(--workspace-action-bar-height, 46px) - 2px);
    min-height: 0; padding: 0 14px !important; overflow: hidden;
    display: flex !important; flex-direction: column !important;
}

/* İç bileşenler */
.xxx-form-card  { flex: 0 0 auto; }                      /* Sabit yükseklik */
.xxx-grid-card  { flex: 1 1 0; min-height: 0; }           /* Kalan alanı doldur */
.xxx-grid-wrap  { flex: 1 1 0; min-height: 0; overflow-y: auto; } /* Scroll */
.xxx-grid-wrap thead th { position: sticky; top: 0; z-index: 3; } /* Sticky header */
```

---

## Teknik Mimari Prensipler

### Composition over Inheritance
- Ana layout bir `MainShell` bileşeni olmalı
- Sekmeler shell içindeki bir **Slot / Router-Outlet** yapısına enjekte edilmeli
- Aksiyon butonları, aktif bileşenden gelen `ActionConfiguration` objesine göre render edilmeli

### CSS Değişkenleri (Global)
| Değişken | Açıklama | Tipik Değer |
|---|---|---|
| `--layout-header-height` | Üst navbar yüksekliği | `56px` |
| `--layout-footer-height` | Alt footer yüksekliği | `44px` |
| `--workspace-tabs-sticky-height` | Tab şerit yüksekliği | `42px` |
| `--workspace-action-bar-height` | Aksiyon barı yüksekliği | `46px` |

### Geliştirici Kontrol Listesi (Yeni Sayfa Eklerken)
- [ ] `page-XXX` body class eklendi mi?
- [ ] `html:has(body.page-XXX)` overflow kilidi var mı?
- [ ] `integrator-dashboard__content` yüksekliği hesapta doğru değişkenler kullanılıyor mu?
- [ ] Alan 4 aksiyon barı içerik alanında değil, standart konumda mı?
- [ ] Form kartı `flex: 0 0 auto`, grid kartı `flex: 1 1 0` mu?
- [ ] Grid sarmalında sticky header ve `overflow-y: auto` var mı?
- [ ] Lookup dropdown'ları için `overflow: visible` ayarlandı mı?

---

## Altın Standart Referans Sayfalar

| Sayfa | Dosya | Özellik |
|---|---|---|
| Malzeme Kartları | `MaterialCards.cshtml` | Form + Grid, Tabs, Görsel, Lookup |
| Ürün Ağacı | `ProductTrees.cshtml` | Form + Bileşen Grid, Görsel |
| E-Fatura | `EFatura.cshtml` | Çift grid (üst fatura, alt satırlar) |
