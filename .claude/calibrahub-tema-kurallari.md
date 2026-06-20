# CalibraHub — CSS & Tema Kuralları

CalibraHub iki temayı destekler: **light** ve **dark**. Yeni CSS veya JSX yazarken bu kuralların tamamına uy.

---

## Tema mimarisi

- `<body>` üzerinde `app-theme-light` / `app-theme-dark` class'ı (server-side Razor ile set edilir)
- `<html>` üzerinde `html.dark` (Shell.jsx JavaScript, React'ın `.dark` selector'u için)
- CSS değişken zinciri: `tokens.css` → `site.css :root` → `body.app-theme-dark { --app-* override }`
- Shell.jsx her tema değişiminde `html.style.colorScheme = isDark ? 'dark' : 'light'` set eder; bu iFrame'lere de yansır.

---

## Kural 1 — Tek dark selector: `body.app-theme-dark`

Koyu tema override'ları için **yalnızca** `body.app-theme-dark` kullanılır.

| ❌ YANLIŞ | ✅ DOĞRU |
|-----------|---------|
| `.dark .my-el` | `body.app-theme-dark .my-el` |
| `html.dark .my-el` | `body.app-theme-dark .my-el` |
| `[data-theme="dark"] .my-el` | `body.app-theme-dark .my-el` |

**Sebep:** Tailwind `.dark`, Daisy UI `[data-theme]` ile karışmaz; tek noktadan grep ile bulunur.

---

## Kural 2 — Light = default pattern

Tüm CSS renk değişkenleri **light değerler** olarak tanımlanır; `body.app-theme-dark` onları ezer.

```css
/* ✅ Doğru: Light default → Dark override */
html body {
  --my-bg: #ffffff;
  --my-text: #0f172a;
}
body.app-theme-dark {
  --my-bg: #1e293b;
  --my-text: #e2e8f0;
}

/* ❌ Yanlış: Dark default → Light override (anti-pattern) */
html body {
  --my-bg: #1e293b;   /* tema sınıfı olmayan sayfalarda koyu çıkar */
}
body.app-theme-light {
  --my-bg: #ffffff;
}
```

---

## Kural 3 — React bileşenlerinde tema: `className` + CSS değişkenleri

JSX inline style içinde hardcoded hex veya rgba **kullanılmaz**. İki zararı var:
1. Light modda bileşen hep koyu görünür (tema sınıfından etkilenmez).
2. `color-scheme` sinyali olmadığında native form kontrolleri (checkbox, input, scrollbar) tarayıcı tarafından light modda render edilir.

**Doğru pattern:**

```css
/* ComponentName.css */
.cn-root {
  color-scheme: light;        /* native kontroller light mod */
  --cn-bg: #f1f5f9;
  --cn-text: #0f172a;
  background: var(--cn-bg);
  color: var(--cn-text);
}
body.app-theme-dark .cn-root {
  color-scheme: dark;         /* native kontroller dark mod */
  --cn-bg: #0b1220;
  --cn-text: #e2e8f0;
}
```

```jsx
/* ComponentName.jsx */
<div className="cn-root" style={{ height: '100%' }}>  {/* layout-only inline */}
```

Referans implementasyon: `IntegrationQueue.jsx` + `IntegrationWizard.css` → `.iq-root`

---

## Kural 4 — `color-scheme` zorunluluğu

Native form kontrolleri (`<input>`, `<select>`, `<checkbox>`, scrollbar) `color-scheme: dark` olmadan **her zaman** beyaz/light render eder — `body.app-theme-dark` class'ı tek başına yetmez.

- Shell.jsx `html.style.colorScheme` set eder → iframe'lere yansır (iframe'ler için ayrıca eklemeye gerek yok)
- **Standalone React bileşeni** yazarken kendi root elementine `color-scheme: light` ve dark override'da `color-scheme: dark` ekle (Kural 3 pattern'i)

---

## Kural 5 — `.cshtml` içi CSS yazım kuralları

Razor `@` karakteri atlatma:
```css
/* ✅ */
@@keyframes fadeIn { ... }
@@media (max-width: 768px) { ... }

/* ❌ */
@keyframes fadeIn { ... }
@media (max-width: 768px) { ... }
```

Renk değerleri için CSS değişken fallback kullan, hardcoded hex yazma:
```css
/* ✅ */
background: var(--app-surface, #fff);
color: var(--bs-body-color, #0f172a);
border: 1px solid var(--app-border, #e2e8f0);

/* ❌ */
background: #fff;
color: #1e293b;
```

---

## Kural 6 — font-weight geçerli değerleri

CSS spec yalnızca **100 basamaklı** değerleri tanır: 100 200 300 400 500 600 700 800 900.  
`560`, `620`, `640`, `650` gibi ara değerler **geçersizdir** — tarayıcı yuvarlar ama tutarsızlığa yol açar.

| Kullanım | Değer |
|----------|-------|
| Normal metin | 400 |
| Orta vurgu | 500 |
| Yarı-kalın | 600 |
| Kalın başlık | 700 |

---

## Kural 7 — Monospace font stack

Kod, ID, timestamp alanları için:
```css
font-family: ui-monospace, Menlo, Consolas, monospace;
```
`Courier New` veya tek başına `monospace` kullanılmaz.

---

## Kural 8 — Kasıtlı beyaz istisnalar (dokunma)

- `Approval/ViewPayload.cshtml` → `#iframeContainer { background: white }` — A4 kağıt simülasyonu, dark modda da beyaz kalmalı.
- `Layout = null` olan yazdırma belgeleri (`AssignmentDocument.cshtml` vb.) — tüm renkler hardcoded light, kasıtlı, değiştirme.

---

## Denetim metodolojisi — yeni ekran veya audit

Yeni bir ekran geliştirirken veya mevcut ekranı denetlerken sırayla kontrol et:

### Adım 1 — Hardcoded hex tarama

```bash
# Razor views — beyaz/açık arka plan
grep -rn "background: #fff\|background: white\|background:#fff" Views/

# React inline style — hardcoded hex
grep -rn "background.*#[0-9a-fA-F]\{6\}" ClientApp/src/
```

### Adım 2 — rgba near-white tarama (grep'e takılmayan)

```bash
# site.css içinde near-white rgba arka planlar
grep -n "background:.*rgba(2[0-9][0-9],[2-9][0-9][0-9]" wwwroot/css/site.css
grep -n "background:.*linear-gradient" wwwroot/css/site.css
```

### Adım 3 — Yanlış dark selector tarama

```bash
# React/CSS kaynak dosyaları — hepsi body.app-theme-dark olmalı
grep -rn "\.dark \." ClientApp/src/
grep -rn "html\.dark" ClientApp/src/
```

### Adım 4 — `color-scheme` kontrolü

Standalone React bileşeni ise root elementinde `color-scheme: light` (ve dark override'da `color-scheme: dark`) var mı?

### Adım 5 — Bundle rebuild (React CSS değişikliği sonrası)

```bash
# ClientApp/src/**/*.css değiştirildiyse bundle yenilenmeli
npm run build     # ClientApp/ dizininden
# ardından server restart
```

---

## site.css kör noktası

`site.css` iki bölümden oluşur:
- **Eski bölüm (satır 1–6137):** Glassmorphism dark-first; `.integrator-*`, `.workspace-*` sınıfları
- **Yeni bölüm (satır 6214+):** Multi-theme light-first; `--app-*` CSS değişken pattern'i

Yeni bölüme `body.app-theme-dark` bloğu eklemek, eski bölümdeki hardcoded renkleri **etkilemez**. Eski bölümdeki sınıfların kendi dark override'larının olması gerekir.
