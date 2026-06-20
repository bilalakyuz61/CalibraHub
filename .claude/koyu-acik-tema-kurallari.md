# Koyu / Açık Tema Kuralları

Bu proje iki temayı destekler: **light** ve **dark**. Yeni CSS veya JSX/TSX yazarken aşağıdaki kurallara uy.

---

## Tema mimarisi

- `<body>` üzerinde `app-theme-light` / `app-theme-dark` class'ı (server tarafında set edilir)
- `<html>` üzerinde `html.dark` (JavaScript ile, React'ın `.dark` selector'u için)
- CSS değişken zinciri: `tokens.css` → `:root` → `body.app-theme-dark { override }`
- Tema değişiminde `html.style.colorScheme = isDark ? 'dark' : 'light'` set edilmeli — iframe'lere de yansır.

---

## Kural 1 — Tek dark selector: `body.app-theme-dark`

Koyu tema override'ları için **yalnızca** `body.app-theme-dark` kullanılır.

| ❌ YANLIŞ | ✅ DOĞRU |
|-----------|---------|
| `.dark .my-el` | `body.app-theme-dark .my-el` |
| `html.dark .my-el` | `body.app-theme-dark .my-el` |
| `[data-theme="dark"] .my-el` | `body.app-theme-dark .my-el` |

**Sebep:** Tailwind `.dark`, Daisy UI `[data-theme]` gibi üçüncü parti seçicilerle karışmaz; tek noktadan grep ile bulunur.

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

JSX/TSX inline style içinde hardcoded hex veya rgba **kullanılmaz**. İki zararı var:
1. Light modda bileşen hep koyu görünür — tema sınıfından etkilenmez.
2. `color-scheme` sinyali olmadığında native form kontrolleri (checkbox, input, scrollbar) tarayıcı tarafından light modda render edilir.

**Doğru pattern:**

```css
/* MyComponent.css */
.mc-root {
  color-scheme: light;        /* native form kontrolleri light mod */
  --mc-bg: #f1f5f9;
  --mc-text: #0f172a;
  --mc-border: #e2e8f0;
  background: var(--mc-bg);
  color: var(--mc-text);
}
body.app-theme-dark .mc-root {
  color-scheme: dark;         /* native form kontrolleri dark mod */
  --mc-bg: #0b1220;
  --mc-text: #e2e8f0;
  --mc-border: rgba(255, 255, 255, 0.08);
}
```

```jsx
/* MyComponent.jsx */
<div className="mc-root" style={{ height: '100%' }}>  {/* sadece layout için inline */}
```

---

## Kural 4 — `color-scheme` zorunluluğu

Native form kontrolleri (`<input>`, `<select>`, `<checkbox>`, scrollbar) `color-scheme: dark` olmadan **her zaman** beyaz/light render eder — `body.app-theme-dark` class'ı tek başına yetmez.

- Global olarak `html.style.colorScheme` set edilmeli; bu iframe'lere de yansır.
- Standalone React bileşeni yazarken kendi root elementine `color-scheme: light` ekle; dark override'da `color-scheme: dark` yap.

---

## Kural 5 — font-weight geçerli değerleri

CSS spec yalnızca **100 basamaklı** değerleri tanır: 100 200 300 400 500 600 700 800 900.  
`560`, `620`, `640`, `650` gibi ara değerler **geçersizdir** — tarayıcı yuvarlar ama tutarsızlığa yol açar.

| Kullanım | Değer |
|----------|-------|
| Normal metin | 400 |
| Orta vurgu | 500 |
| Yarı-kalın | 600 |
| Kalın başlık | 700 |

---

## Kural 6 — Monospace font stack

Kod, ID, timestamp alanları için:
```css
font-family: ui-monospace, Menlo, Consolas, monospace;
```
`Courier New` veya tek başına `monospace` kullanılmaz.

---

## Kural 7 — Kasıtlı beyaz istisnalar

Yazdırma belgeleri ve A4 önizleme alanları hardcoded light renk kullanabilir — dark modda da beyaz kalmalıdır. Bu dosyalara dark override ekleme.

---

## Denetim metodolojisi

Yeni ekran geliştirirken veya mevcut ekranı denetlerken sırayla kontrol et:

### Adım 1 — Hardcoded renk tarama

```bash
# Razor / HTML views — beyaz/açık hardcoded arka plan
grep -rn "background: #fff\|background: white\|background:#fff" Views/

# React inline style — hardcoded hex
grep -rn "background.*#[0-9a-fA-F]\{6\}" src/components/
```

### Adım 2 — rgba near-white tarama (grep'e takılmayan)

```bash
grep -n "background:.*rgba(2[0-9][0-9],[2-9][0-9][0-9]" src/
grep -n "background:.*linear-gradient" src/
```

### Adım 3 — Yanlış dark selector tarama

```bash
grep -rn "\.dark \." src/
grep -rn "html\.dark" src/
# → hepsi body.app-theme-dark olmalı
```

### Adım 4 — `color-scheme` kontrolü

Standalone React bileşeninde root elementinde `color-scheme: light` (ve dark override'da `color-scheme: dark`) var mı?

### Adım 5 — Bundle rebuild

React CSS kaynak dosyası değiştirildiyse bundle'ı yeniden derle; eski bundle cache'deki eski CSS'i döndürür.
