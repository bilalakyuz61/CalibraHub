---
name: sutun-paneli
description: |
  Herhangi bir liste/tablo ekranına sütun ayarları paneli ekler: görünürlük
  göz toggle, sürükle-bırak sıralama, başlık rename, genişlik, hizalama, arama
  kutusu. Ayarlar user_settings tablosunda kullanıcı bazlı saklanır.
  Kullanıcı "sütun ayarları ekle", "kolon görünürlüğü", "sütunları
  özelleştirilebilir yap", "kolonu gizle/göster" gibi ifadeler kullandığında
  tetikle. Referans implementasyon: Views/PendingApproval/Index.cshtml (pa- prefix).
---

# sutun-paneli

Bir liste/tablo `.cshtml` ekranına tam sütun ayarları paneli ekler.

## Temel kavramlar

| Kavram | Açıklama |
|--------|----------|
| **prefix** | Kısa 2-3 harf (örn. `pa`, `ik`, `po`). Tüm CSS class'ları, DOM id'leri ve JS fonksiyon isimleri bu prefix'i taşır. |
| **settingsKey** | `user_settings` tablosundaki kayıt anahtarı: `ui.{prefix}.col-cfg` |
| **_colDefs** | `{ key, label, visible, align, width, fixed }` dizisi — çalışma zamanında tutulur |
| **DEFAULT_DEFS** | Kod içinde tanımlı başlangıç sütun listesi |

## Adım 1 — Controller C# (2 endpoint)

```csharp
// Dosya: XxxController.cs
using CalibraHub.Application.Abstractions.Persistence;
using System.Security.Claims;

private readonly IUserSettingRepository _userSettingRepo;
private const string ColCfgKey = "ui.{prefix}.col-cfg";   // ← prefix değiştir

// Constructor'a IUserSettingRepository enjekte et:
// public XxxController(IXxxService service, IUserSettingRepository userSettingRepo)

[HttpGet]
public async Task<IActionResult> GetColConfig(CancellationToken ct)
{
    var uid = CurrentUserId();
    if (!uid.HasValue) return Json(new { config = (string?)null });
    var json = await _userSettingRepo.GetAsync(uid.Value, ColCfgKey, ct);
    return Json(new { config = json });
}

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SaveColConfig([FromBody] SaveColConfigRequest request, CancellationToken ct)
{
    var uid = CurrentUserId();
    if (!uid.HasValue) return Json(new { ok = false });
    await _userSettingRepo.SetAsync(uid.Value, ColCfgKey, request.Config, ct);
    return Json(new { ok = true });
}

private int? CurrentUserId()
    => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
```

`SaveColConfigRequest` record'u controller dosyasının sonuna veya ayrı bir yere:
```csharp
public sealed record SaveColConfigRequest(string? Config);
```

> `IUserSettingRepository` zaten DI'ye kayıtlı — `Program.cs`'e dokunma.

---

## Adım 2 — CSS (`@section Styles`)

Aşağıda `{P}` → seçilen prefix (örn. `pa`, `ik`).  
`body.app-theme-dark` override bloğunu da dahil et.

```css
/* ── Sütun ayarları paneli ─────────────────────────── */
.{P}-col-btn {
    width: 30px; height: 30px; padding: 0; border-radius: 7px;
    border: 1px solid transparent; background: transparent;
    display: flex; align-items: center; justify-content: center;
    cursor: pointer; color: #94a3b8; flex-shrink: 0;
    transition: background .1s, color .1s, border-color .1s;
}
.{P}-col-btn:hover, .{P}-col-btn.active {
    background: rgba(99,102,241,.1); color: #6366f1;
    border-color: rgba(99,102,241,.25);
}
body.app-theme-dark .{P}-col-btn { color: #64748b; }
body.app-theme-dark .{P}-col-btn:hover,
body.app-theme-dark .{P}-col-btn.active {
    background: rgba(99,102,241,.18); color: #818cf8; border-color: rgba(99,102,241,.3);
}
.{P}-col-btn svg { width: 16px; height: 16px; }

#{}P}-col-panel {
    position: absolute; top: 44px; right: 8px; z-index: 120;
    width: 320px; max-height: 520px;
    background: var(--app-surface, #fff);
    border: 1px solid var(--app-border, #e2e8f0);
    border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,.12);
    display: none; flex-direction: column; overflow: hidden;
}
#{}P}-col-panel.open { display: flex; }

.{P}-cfg-hdr {
    padding: 10px 14px 8px; font-size: 11px; font-weight: 600;
    text-transform: uppercase; letter-spacing: .05em;
    color: #64748b; border-bottom: 1px solid var(--app-border, #e2e8f0);
    flex-shrink: 0;
}
#{}P}-col-rows { overflow-y: auto; flex: 1; padding: 4px 0; }

.{P}-cfg-row {
    display: flex; align-items: center; gap: 6px;
    padding: 4px 10px; cursor: grab; border-radius: 6px; margin: 1px 4px;
    transition: background .1s;
}
.{P}-cfg-row:hover { background: rgba(99,102,241,.06); }
.{P}-cfg-row.is-fixed { cursor: default; opacity: .55; }
.{P}-cfg-row.is-hidden { opacity: .45; }
.{P}-cfg-row.drag-over { outline: 2px solid #6366f1; outline-offset: -2px; }

.{P}-cfg-grip { color: #94a3b8; flex-shrink: 0; display: flex; cursor: grab; }
.{P}-cfg-grip svg { width: 14px; height: 14px; }

.{P}-cfg-eye {
    width: 20px; height: 20px; padding: 0; border: none; background: transparent;
    cursor: pointer; display: flex; align-items: center; justify-content: center;
    color: #6366f1; border-radius: 4px; flex-shrink: 0;
    transition: background .1s;
}
.{P}-cfg-eye.{P}-eye-off { color: #94a3b8; }
.{P}-cfg-eye:hover { background: rgba(99,102,241,.1); }
.{P}-cfg-eye svg { width: 14px; height: 14px; }

.{P}-cfg-label { flex: 1; font-size: 12px; font-weight: 500; color: var(--app-text, #0f172a); }
body.app-theme-dark .{P}-cfg-label { color: #e2e8f0; }

.{P}-cfg-exp {
    width: 20px; height: 20px; padding: 0; border: none; background: transparent;
    cursor: pointer; display: flex; align-items: center; justify-content: center;
    color: #94a3b8; border-radius: 4px; flex-shrink: 0; transition: background .1s;
}
.{P}-cfg-exp:hover { background: rgba(99,102,241,.1); color: #6366f1; }
.{P}-cfg-exp svg { width: 12px; height: 12px; transition: transform .15s; }
.{P}-cfg-exp.open svg { transform: rotate(180deg); }

.{P}-cfg-detail {
    display: none; flex-direction: column; gap: 6px;
    padding: 6px 10px 8px 38px;
    border-bottom: 1px solid var(--app-border, #e2e8f0);
    margin: 0 4px 2px;
}
.{P}-cfg-detail.open { display: flex; }

.{P}-cfg-field-row { display: flex; align-items: center; gap: 8px; }
.{P}-cfg-field-label { width: 68px; font-size: 11px; color: #64748b; flex-shrink: 0; }
.{P}-cfg-field-input {
    flex: 1; height: 26px; padding: 0 8px; border-radius: 5px;
    border: 1px solid rgba(148,163,184,.3);
    background: var(--app-bg, #f8fafc); font-size: 12px;
    color: var(--app-text, #0f172a); outline: none;
}
.{P}-cfg-field-input:focus { border-color: rgba(99,102,241,.5); }
body.app-theme-dark .{P}-cfg-field-input {
    background: rgba(255,255,255,.05); color: #e2e8f0; border-color: rgba(255,255,255,.1);
}
.{P}-cfg-width-input { width: 70px; flex: none; text-align: right; }
.{P}-cfg-field-unit { font-size: 11px; color: #94a3b8; }

/* Hizalama butonları */
.{P}-cfg-align { display: flex; gap: 2px; flex: 1; }
.{P}-cfg-align-btn {
    flex: 1; height: 28px;
    display: flex; align-items: center; justify-content: center;
    border-radius: 5px; border: 1px solid rgba(148,163,184,.2);
    cursor: pointer; color: #94a3b8; background: transparent; padding: 0;
    transition: all 0.1s;
}
.{P}-cfg-align-btn svg { width: 14px; height: 14px; }
.{P}-cfg-align-btn:hover { background: rgba(99,102,241,.1); color: #6366f1; }
.{P}-cfg-align-btn.{P}-align-active { background: rgba(99,102,241,.15); color: #6366f1; border-color: rgba(99,102,241,.3); }
body.app-theme-dark .{P}-cfg-align-btn.{P}-align-active { background: rgba(99,102,241,.25); color: #818cf8; }
```

---

## Adım 3 — HTML

### Listeyi tutan header'a toggle butonu ve arama ekle

```html
<div class="{P}-list-header">
    <div style="flex-shrink:0">
        <div class="{P}-list-title" id="{P}ListTitle">...</div>
        <div class="{P}-list-count" id="{P}ListCount"></div>
    </div>
    <!-- Arama kutusu -->
    <div class="{P}-search-wrap">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
        </svg>
        <input type="text" id="{P}Search" class="{P}-search-input" placeholder="Hızlı ara…" autocomplete="off">
    </div>
    <!-- Sütun paneli toggle -->
    <button type="button" class="{P}-col-btn" id="{P}ColBtn"
            onclick="{P}ToggleColPanel(event)" title="Sütun Ayarları">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="4" y1="6" x2="20" y2="6"/>
            <line x1="4" y1="12" x2="14" y2="12"/>
            <circle cx="18" cy="12" r="2"/>
            <line x1="4" y1="18" x2="11" y2="18"/>
            <circle cx="15" cy="18" r="2"/>
        </svg>
    </button>
    <!-- Panel -->
    <div id="{P}ColPanel" class="{P}-col-panel">
        <div class="{P}-cfg-hdr">Sütun Ayarları</div>
        <div id="{P}ColRows"></div>
    </div>
</div>
```

> `.{P}-list-header` için CSS: `display:flex; align-items:center; gap:8px; position:relative`

### Arama kutusu CSS

```css
.{P}-search-wrap {
    flex: 1 1 auto; max-width: 210px; position: relative; display: flex; align-items: center;
}
.{P}-search-input {
    width: 100%; height: 30px; padding: 0 8px 0 30px;
    border: 1px solid rgba(148,163,184,.22); border-radius: 7px;
    background: transparent; font-size: 12px;
    color: var(--app-text, #0f172a); outline: none; transition: border-color .1s;
}
.{P}-search-input:focus { border-color: rgba(99,102,241,.5); }
.{P}-search-input::placeholder { color: #94a3b8; }
.{P}-search-wrap svg { position: absolute; left: 8px; color: #94a3b8; pointer-events: none; }
body.app-theme-dark .{P}-search-input { color: #e2e8f0; border-color: rgba(255,255,255,.1); }
body.app-theme-dark .{P}-search-input::placeholder { color: #475569; }
```

---

## Adım 4 — JavaScript (tam şablon)

`@section Scripts { <script>` bloğu içine koy. `{P}` = prefix, `{CTRL}` = controller route adı (örn. `PendingApproval`, `Fulfillment`).

```javascript
(async function() {
    'use strict';

    // ── Varsayılan sütun tanımları ──────────────────────────────────────────
    var DEFAULT_DEFS = [
        { key: 'col1', label: 'Sütun 1', visible: true,  align: 'left',   width: 0,   fixed: false },
        { key: 'col2', label: 'Sütun 2', visible: true,  align: 'center', width: 120, fixed: false },
        // ... ekranın sütunlarını buraya yaz
    ];

    var _colDefs = DEFAULT_DEFS.map(function(d) { return Object.assign({}, d); });
    var _lastItems = null;
    var _expandedKeys = new Set();
    var _searchQuery = '';

    // ── SVG sabitler ──────────────────────────────────────────────────────
    var GRIP = '<svg viewBox="0 0 16 16" fill="currentColor"><circle cx="5" cy="4" r="1.2"/><circle cx="5" cy="8" r="1.2"/><circle cx="5" cy="12" r="1.2"/><circle cx="11" cy="4" r="1.2"/><circle cx="11" cy="8" r="1.2"/><circle cx="11" cy="12" r="1.2"/></svg>';
    var EYE    = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>';
    var EYEOFF = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>';
    var CHEV   = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="6 9 12 15 18 9"/></svg>';
    var ALIGN_ICONS = {
        left:   '<svg viewBox="0 0 16 14" fill="currentColor"><rect x="0" y="0" width="16" height="2" rx="1"/><rect x="0" y="4" width="10" height="2" rx="1"/><rect x="0" y="8" width="13" height="2" rx="1"/><rect x="0" y="12" width="8" height="2" rx="1"/></svg>',
        center: '<svg viewBox="0 0 16 14" fill="currentColor"><rect x="0" y="0" width="16" height="2" rx="1"/><rect x="3" y="4" width="10" height="2" rx="1"/><rect x="1.5" y="8" width="13" height="2" rx="1"/><rect x="4" y="12" width="8" height="2" rx="1"/></svg>',
        right:  '<svg viewBox="0 0 16 14" fill="currentColor"><rect x="0" y="0" width="16" height="2" rx="1"/><rect x="6" y="4" width="10" height="2" rx="1"/><rect x="2.5" y="8" width="13" height="2" rx="1"/><rect x="8" y="12" width="8" height="2" rx="1"/></svg>',
    };

    // ── Yardımcı ─────────────────────────────────────────────────────────
    function escapeHtml(s) {
        return String(s || '')
            .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
    function csrf() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }
    function _serializeColDefs() {
        return JSON.stringify(_colDefs.map(function(c) {
            return { key: c.key, visible: c.visible, align: c.align, width: c.width || 0, label: c.label };
        }));
    }
    function _saveColDefs() {
        var json = _serializeColDefs();
        try { localStorage.setItem('{P}-col-cfg-v2', json); } catch(e) {}
        fetch('/{CTRL}/SaveColConfig', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf() },
            body: JSON.stringify({ config: json })
        }).catch(function() {});
    }
    function _loadColDefs(savedJson) {
        if (!savedJson) return;
        try {
            var saved = JSON.parse(savedJson);
            saved.forEach(function(s) {
                var def = _colDefs.find(function(d) { return d.key === s.key; });
                if (!def) return;
                if (s.label  !== undefined) def.label   = s.label;
                if (s.width  !== undefined) def.width   = s.width;
                if (s.align  !== undefined) def.align   = s.align;
                if (s.visible!== undefined) def.visible = s.visible;
            });
            // Kaydedilen sırayı uygula
            var order = saved.map(function(s) { return s.key; });
            _colDefs.sort(function(a, b) {
                var ia = order.indexOf(a.key), ib = order.indexOf(b.key);
                if (ia === -1) return 1; if (ib === -1) return -1;
                return ia - ib;
            });
        } catch(e) {}
    }
    function _applyServerConfig(json) {
        if (!json) {
            try { var ls = localStorage.getItem('{P}-col-cfg-v2'); if (ls) _loadColDefs(ls); } catch(e) {}
            return;
        }
        _loadColDefs(json);
    }

    // ── Panel renderer ────────────────────────────────────────────────────
    function _renderColPanel() {
        var rowsEl = document.getElementById('{P}ColRows');
        if (!rowsEl) return;
        var html = '';
        _colDefs.forEach(function(c, i) {
            var fc = c.fixed   ? ' is-fixed'  : '';
            var hc = !c.visible ? ' is-hidden' : '';
            var isOpen = _expandedKeys.has(c.key);
            var oc = isOpen ? ' open' : '';
            html +=
                '<div class="{P}-cfg-row' + fc + hc + '" data-idx="' + i + '" data-key="' + c.key + '" draggable="' + (c.fixed ? 'false' : 'true') + '">' +
                    '<span class="{P}-cfg-grip">' + GRIP + '</span>' +
                    '<button type="button" class="{P}-cfg-eye' + (!c.visible ? ' {P}-eye-off' : '') + '" data-key="' + c.key + '">' + (c.visible ? EYE : EYEOFF) + '</button>' +
                    '<span class="{P}-cfg-label">' + escapeHtml(c.label) + '</span>' +
                    '<button type="button" class="{P}-cfg-exp' + oc + '" data-key="' + c.key + '" title="Detay ayarları">' + CHEV + '</button>' +
                '</div>' +
                '<div class="{P}-cfg-detail' + oc + '" data-key="' + c.key + '">' +
                    '<div class="{P}-cfg-field-row">' +
                        '<span class="{P}-cfg-field-label">Başlık</span>' +
                        '<input type="text" class="{P}-cfg-field-input" data-key="' + c.key + '" data-field="label" value="' + escapeHtml(c.label) + '">' +
                    '</div>' +
                    '<div class="{P}-cfg-field-row">' +
                        '<span class="{P}-cfg-field-label">Genişlik</span>' +
                        '<input type="number" class="{P}-cfg-field-input {P}-cfg-width-input" data-key="' + c.key + '" data-field="width" value="' + (c.width || '') + '" placeholder="Otomatik" min="40" max="600">' +
                        '<span class="{P}-cfg-field-unit">px</span>' +
                    '</div>' +
                    '<div class="{P}-cfg-field-row">' +
                        '<span class="{P}-cfg-field-label">Hizalama</span>' +
                        '<span class="{P}-cfg-align">' +
                            '<button type="button" class="{P}-cfg-align-btn' + (c.align === 'left'   ? ' {P}-align-active' : '') + '" data-key="' + c.key + '" data-align="left"   title="Sola hizala">'  + ALIGN_ICONS.left   + '</button>' +
                            '<button type="button" class="{P}-cfg-align-btn' + (c.align === 'center' ? ' {P}-align-active' : '') + '" data-key="' + c.key + '" data-align="center" title="Ortala">'        + ALIGN_ICONS.center + '</button>' +
                            '<button type="button" class="{P}-cfg-align-btn' + (c.align === 'right'  ? ' {P}-align-active' : '') + '" data-key="' + c.key + '" data-align="right"  title="Sağa hizala">'  + ALIGN_ICONS.right  + '</button>' +
                        '</span>' +
                    '</div>' +
                '</div>';
        });
        rowsEl.innerHTML = html;

        // Panel genelinde dragover → yasak simgesi gözükmesin
        var panelEl = document.getElementById('{P}ColPanel');
        if (panelEl) panelEl.ondragover = function(e) { e.preventDefault(); };

        // Göz toggle
        rowsEl.querySelectorAll('.{P}-cfg-eye').forEach(function(btn) {
            btn.addEventListener('click', function(e) {
                e.stopPropagation();
                var key = btn.getAttribute('data-key');
                var def = _colDefs.find(function(d) { return d.key === key; });
                if (def && !def.fixed) { def.visible = !def.visible; _saveColDefs(); _renderColPanel(); if (_lastItems) renderList(_lastItems); }
            });
        });

        // Hizalama
        rowsEl.querySelectorAll('.{P}-cfg-align-btn').forEach(function(btn) {
            btn.addEventListener('click', function(e) {
                e.stopPropagation();
                var key   = btn.getAttribute('data-key');
                var align = btn.getAttribute('data-align');
                var def   = _colDefs.find(function(d) { return d.key === key; });
                if (def) { def.align = align; _saveColDefs(); _renderColPanel(); if (_lastItems) renderList(_lastItems); }
            });
        });

        // Başlık düzenleme (focus kaybetmeden güncelle)
        rowsEl.querySelectorAll('input[data-field="label"]').forEach(function(inp) {
            inp.addEventListener('input', function() {
                var key = inp.getAttribute('data-key');
                var def = _colDefs.find(function(d) { return d.key === key; });
                if (def) {
                    def.label = inp.value;
                    _saveColDefs();
                    var span = rowsEl.querySelector('.{P}-cfg-row[data-key="' + key + '"] .{P}-cfg-label');
                    if (span) span.textContent = def.label;
                    if (_lastItems) renderList(_lastItems);
                }
            });
        });

        // Genişlik
        rowsEl.querySelectorAll('input[data-field="width"]').forEach(function(inp) {
            inp.addEventListener('change', function() {
                var key = inp.getAttribute('data-key');
                var def = _colDefs.find(function(d) { return d.key === key; });
                if (def) { def.width = parseInt(inp.value, 10) || 0; _saveColDefs(); if (_lastItems) renderList(_lastItems); }
            });
        });

        // Expand/collapse chevron
        rowsEl.querySelectorAll('.{P}-cfg-exp').forEach(function(btn) {
            btn.addEventListener('click', function(e) {
                e.stopPropagation();
                var key = btn.getAttribute('data-key');
                if (_expandedKeys.has(key)) _expandedKeys.delete(key); else _expandedKeys.add(key);
                _renderColPanel();
            });
        });

        // Drag-and-drop sıralama
        var dragSrcIdx = null;
        rowsEl.querySelectorAll('.{P}-cfg-row:not(.is-fixed)').forEach(function(row) {
            row.addEventListener('dragstart', function(e) {
                dragSrcIdx = parseInt(row.getAttribute('data-idx'), 10);
                e.dataTransfer.effectAllowed = 'move';
            });
            row.addEventListener('dragover', function(e) {
                e.preventDefault(); e.dataTransfer.dropEffect = 'move';
                rowsEl.querySelectorAll('.{P}-cfg-row').forEach(function(r) { r.classList.remove('drag-over'); });
                row.classList.add('drag-over');
            });
            row.addEventListener('dragleave', function() { row.classList.remove('drag-over'); });
            row.addEventListener('drop', function(e) {
                e.preventDefault(); row.classList.remove('drag-over');
                var targetIdx = parseInt(row.getAttribute('data-idx'), 10);
                if (dragSrcIdx === null || dragSrcIdx === targetIdx) return;
                var moved = _colDefs.splice(dragSrcIdx, 1)[0];
                _colDefs.splice(targetIdx, 0, moved);
                dragSrcIdx = null; _saveColDefs(); _renderColPanel(); if (_lastItems) renderList(_lastItems);
            });
        });
    }

    // ── Panel aç/kapat ────────────────────────────────────────────────────
    function {P}ToggleColPanel(e) {
        e.stopPropagation();
        var panel = document.getElementById('{P}ColPanel');
        var btn   = document.getElementById('{P}ColBtn');
        if (!panel) return;
        var opening = !panel.classList.contains('open');
        panel.classList.toggle('open', opening);
        btn && btn.classList.toggle('active', opening);
        if (opening) _renderColPanel();
    }
    document.addEventListener('click', function(e) {
        var panel = document.getElementById('{P}ColPanel');
        var btn   = document.getElementById('{P}ColBtn');
        if (panel && panel.classList.contains('open') && !panel.contains(e.target) && e.target !== btn) {
            panel.classList.remove('open');
            btn && btn.classList.remove('active');
        }
    });

    // ── Arama ─────────────────────────────────────────────────────────────
    var searchEl = document.getElementById('{P}Search');
    if (searchEl) {
        searchEl.addEventListener('input', function() {
            _searchQuery = searchEl.value.trim().toLowerCase();
            if (_lastItems) renderList(_lastItems);
        });
    }
    function _filterItems(items) {
        if (!_searchQuery) return items;
        return items.filter(function(it) {
            // Ekrana özgü alanları buraya yaz:
            return Object.values(it).some(function(v) {
                return v && String(v).toLowerCase().includes(_searchQuery);
            });
        });
    }

    // ── renderList: colgroup + thead + tbody ──────────────────────────────
    function renderList(items) {
        _lastItems = items;
        var filtered = _filterItems(items);
        var listEl = document.getElementById('{P}ListScroll');  // id'yi ayarla
        var countEl = document.getElementById('{P}ListCount');
        if (!filtered || filtered.length === 0) {
            listEl.innerHTML = '<div class="{P}-empty">' +
                (_searchQuery ? 'Arama sonucu bulunamadı.' : 'Kayıt yok.') + '</div>';
            if (countEl) countEl.textContent = _searchQuery ? (filtered.length + ' / ' + (items||[]).length) : '';
            return;
        }
        if (countEl) countEl.textContent = _searchQuery
            ? (filtered.length + ' / ' + items.length + ' kayıt')
            : (filtered.length + ' kayıt');

        var vis = _colDefs.filter(function(c) { return c.visible; });
        var html = '<table class="{P}-table"><colgroup>';
        vis.forEach(function(c) {
            html += c.width ? '<col style="width:' + c.width + 'px;min-width:' + c.width + 'px">' : '<col>';
        });
        html += '</colgroup><thead><tr>';
        vis.forEach(function(c) {
            var styleArr = ['text-align:' + (c.align || 'left')];
            if (c.width) styleArr.push('min-width:' + c.width + 'px');
            html += '<th style="' + styleArr.join(';') + '">' + escapeHtml(c.label) + '</th>';
        });
        html += '</tr></thead><tbody>';
        filtered.forEach(function(item) {
            html += '<tr>';
            vis.forEach(function(c) {
                var styleArr = ['text-align:' + (c.align || 'left')];
                if (c.width) styleArr.push('min-width:' + c.width + 'px');
                html += '<td style="' + styleArr.join(';') + '">' + escapeHtml(item[c.key] ?? '') + '</td>';
            });
            html += '</tr>';
        });
        html += '</tbody></table>';
        listEl.innerHTML = html;
    }

    // ── İlk yükleme ───────────────────────────────────────────────────────
    var cfgRes = await fetch('/{CTRL}/GetColConfig').then(function(r) { return r.json(); }).catch(function() { return {}; });
    _applyServerConfig(cfgRes.config);
    // → buradan sonra kendi veri yükleme fonksiyonunu çağır (örn. loadData())

})();
```

---

## Adım 5 — Tablo CSS (temel)

```css
.{P}-table { width: 100%; border-collapse: collapse; table-layout: fixed; font-size: 12.5px; }
.{P}-table th {
    padding: 8px 10px; font-size: 11px; font-weight: 600;
    letter-spacing: .03em; color: #64748b;
    border-bottom: 1px solid var(--app-border, #e2e8f0);
    white-space: nowrap; position: sticky; top: 0;
    background: var(--app-surface, #fff); z-index: 1;
}
.{P}-table td {
    padding: 9px 10px; border-bottom: 1px solid var(--app-border, #e2e8f0);
    color: var(--app-text, #0f172a); white-space: nowrap; overflow: hidden;
    text-overflow: ellipsis;
}
.{P}-table tbody tr:hover { background: rgba(99,102,241,.04); cursor: pointer; }
body.app-theme-dark .{P}-table th { background: var(--app-surface); color: #64748b; }
body.app-theme-dark .{P}-table td { color: #e2e8f0; }
```

---

## Kontrol listesi

- [ ] Controller: `GetColConfig` + `SaveColConfig` endpoints, `IUserSettingRepository` injection
- [ ] `SaveColConfigRequest` record
- [ ] CSS: tüm `{P}-cfg-*` sınıfları, `{P}-col-btn`, panel, tablo
- [ ] HTML: toggle butonu + panel container listHeader içinde, `position: relative`
- [ ] JS: `DEFAULT_DEFS` ekrana özgü sütunlarla dolu
- [ ] JS: `{P}ToggleColPanel` global fonksiyon (onclick'ten çağrılıyor)
- [ ] JS: `renderList` colgroup + `table-layout:fixed` içeriyor
- [ ] Arama: `_filterItems` filtreleme alanları doğru belirlendi
- [ ] Route: `{CTRL}` controller adı URL'de doğru (örn. `Fulfillment`, `PendingApproval`)
- [ ] settingsKey: `ui.{prefix}.col-cfg` — her ekrana özgü, çakışmasın

## Referans implementasyon

`Views/PendingApproval/Index.cshtml` — prefix `pa`, settingsKey `ui.pa.col-cfg`.  
Yeni ekran yazarken bu dosyayı referans al; şablon yukarıdaki `{P}` yerleşimlerini birebir takip eder.
