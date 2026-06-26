// DB Schema Explorer — aktif sirket DB'sinin fiziksel semasi icin okuma arayuzu.
// Sadece metadata (sys.* introspection). Ornek veri/PII OKUNMAZ.

(function () {
    'use strict';

    const API_BASE = '/admin/db-schema/api';
    const ELS = {
        search: document.getElementById('sch-search'),
        count: document.getElementById('sch-count'),
        list: document.getElementById('sch-list'),
        detailTitle: document.getElementById('sch-detail-title'),
        detailMeta: document.getElementById('sch-detail-meta'),
        tabs: document.getElementById('sch-tabs'),
        tabBody: document.getElementById('sch-tab-body'),
        colCount: document.getElementById('sch-col-count'),
        idxCount: document.getElementById('sch-idx-count'),
        fkCount: document.getElementById('sch-fk-count'),
        exportBtn: document.getElementById('sch-export-btn'),
        exportMenu: document.getElementById('sch-export-menu'),
    };

    const state = {
        tables: [],
        views: [],
        filteredItems: [],
        selected: null,
        selectedType: null, // 'table' | 'view'
        detail: null,
        activeTab: 'columns',
        segment: 'tables', // 'tables' | 'views'
    };

    init();

    async function init() {
        bindEvents();
        await Promise.all([loadTables(), loadViews()]);
    }

    function bindEvents() {
        ELS.search.addEventListener('input', onSearch);
        ELS.exportBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            ELS.exportMenu.classList.toggle('open');
        });
        document.addEventListener('click', () => ELS.exportMenu.classList.remove('open'));

        document.querySelectorAll('.sch-tab').forEach(tab => {
            tab.addEventListener('click', () => switchTab(tab.dataset.tab));
        });

        document.querySelectorAll('.sch-segment').forEach(btn => {
            btn.addEventListener('click', () => switchSegment(btn.dataset.segment));
        });
    }

    function switchSegment(seg) {
        state.segment = seg;
        state.selected = null;
        state.selectedType = null;
        state.detail = null;

        document.querySelectorAll('.sch-segment').forEach(b => {
            b.classList.toggle('active', b.dataset.segment === seg);
        });

        ELS.search.value = '';
        ELS.search.placeholder = seg === 'tables' ? 'Tablo ara...' : 'View ara...';
        applyFilter();
        resetDetailPanel();
    }

    async function loadTables() {
        try {
            const res = await fetch(`${API_BASE}/tables`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            state.tables = await res.json();
            if (state.segment === 'tables') applyFilter();
        } catch (err) {
            if (state.segment === 'tables') {
                ELS.list.innerHTML = `<div style="padding:12px;color:var(--sch-warn-fg);font-size:0.82rem;">Tablolar yuklenemedi: ${escapeHtml(err.message)}</div>`;
            }
        }
    }

    async function loadViews() {
        try {
            const res = await fetch(`${API_BASE}/views`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            state.views = await res.json();
            if (state.segment === 'views') applyFilter();
        } catch (err) {
            if (state.segment === 'views') {
                ELS.list.innerHTML = `<div style="padding:12px;color:var(--sch-warn-fg);font-size:0.82rem;">View'lar yuklenemedi: ${escapeHtml(err.message)}</div>`;
            }
        }
    }

    function applyFilter() {
        const q = ELS.search.value.trim().toLowerCase();
        const source = state.segment === 'tables' ? state.tables : state.views;
        if (!q) {
            state.filteredItems = source.slice();
        } else {
            const tokens = q.split(/\s+/).filter(Boolean);
            state.filteredItems = source.filter(item => {
                const hay = [
                    item.name,
                    item.usedIn,
                    item.description,
                    item.userDescription,
                ].filter(Boolean).join(' ').toLowerCase();
                return tokens.every(tok => hay.includes(tok));
            });
        }
        renderList();
    }

    function onSearch() {
        applyFilter();
    }

    function renderList() {
        const total = state.segment === 'tables' ? state.tables.length : state.views.length;
        ELS.count.textContent = `${state.filteredItems.length} / ${total}`;

        if (state.filteredItems.length === 0) {
            ELS.list.innerHTML = `<div style="padding:12px;color:var(--sch-empty-fg);font-size:0.82rem;">Eslesen ${state.segment === 'tables' ? 'tablo' : 'view'} bulunamadi.</div>`;
            return;
        }

        if (state.segment === 'tables') {
            ELS.list.innerHTML = state.filteredItems.map(t => {
                const key = `table:${t.schema}.${t.name}`;
                const active = state.selected === key ? ' active' : '';
                const tooltip = t.description ? escapeAttr(t.description) : '';
                const hasDesc = t.description ? ' has-desc' : '';
                return `
                    <div class="sch-table-row${active}${hasDesc}" data-type="table" data-schema="${escapeAttr(t.schema)}" data-name="${escapeAttr(t.name)}" title="${tooltip}">
                        <span class="sch-table-name">${escapeHtml(t.name)}</span>
                        <span class="sch-row-count">${formatNumber(t.rowCount)}</span>
                    </div>`;
            }).join('');
        } else {
            ELS.list.innerHTML = state.filteredItems.map(v => {
                const key = `view:${v.name}`;
                const active = state.selected === key ? ' active' : '';
                const customBadge = v.isCustomizable ? '<span class="sch-view-custom-badge">özelleştirilebilir</span>' : '';
                const absentBadge = !v.existsInDb ? '<span class="sch-view-absent">yok</span>' : '';
                return `
                    <div class="sch-table-row${active}" data-type="view" data-name="${escapeAttr(v.name)}" title="${escapeAttr(v.usedIn)}">
                        <span class="sch-table-name">${escapeHtml(v.name)}</span>
                        <span>${customBadge}${absentBadge}</span>
                    </div>`;
            }).join('');
        }

        ELS.list.querySelectorAll('.sch-table-row').forEach(row => {
            row.addEventListener('click', () => {
                if (row.dataset.type === 'table') {
                    selectTable(row.dataset.schema, row.dataset.name);
                } else {
                    selectView(row.dataset.name);
                }
            });
        });
    }

    // ── Table detail ────────────────────────────────────────────────────────

    async function selectTable(schema, name) {
        state.selected = `table:${schema}.${name}`;
        state.selectedType = 'table';
        renderList();
        ELS.detailTitle.textContent = `${schema}.${name}`;
        ELS.detailMeta.textContent = 'Yukleniyor...';
        ELS.tabs.style.display = 'none';
        ELS.tabBody.innerHTML = '<div class="sch-empty">Yukleniyor...</div>';
        try {
            const res = await fetch(`${API_BASE}/tables/${encodeURIComponent(schema)}/${encodeURIComponent(name)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            state.detail = await res.json();
            renderTableDetail();
        } catch (err) {
            ELS.tabBody.innerHTML = `<div class="sch-empty" style="color:var(--sch-warn-fg);">Detay yuklenemedi: ${escapeHtml(err.message)}</div>`;
        }
    }

    function renderTableDetail() {
        const d = state.detail;
        if (!d) return;
        const metaLine = `Satir: ${formatNumber(d.rowCount)} • Kolon: ${d.columns.length} • Indeks: ${d.indexes.length} • FK (giden): ${d.outgoingForeignKeys.length} • FK (gelen): ${d.incomingForeignKeys.length}`;
        ELS.detailMeta.innerHTML = d.description
            ? `<div class="sch-table-summary">${escapeHtml(d.description)}</div><div class="sch-meta-line">${escapeHtml(metaLine)}</div>`
            : `<div class="sch-meta-line">${escapeHtml(metaLine)}</div>`;
        ELS.colCount.textContent = `(${d.columns.length})`;
        ELS.idxCount.textContent = `(${d.indexes.length})`;
        ELS.fkCount.textContent = `(${d.outgoingForeignKeys.length + d.incomingForeignKeys.length})`;
        ELS.tabs.style.display = 'flex';
        renderTabBody();
    }

    // ── View detail ─────────────────────────────────────────────────────────

    function selectView(name) {
        const v = state.views.find(x => x.name === name);
        if (!v) return;
        state.selected = `view:${name}`;
        state.selectedType = 'view';
        state.detail = null;
        renderList();
        ELS.tabs.style.display = 'none';

        const catalogBadge = v.isInCatalog
            ? ''
            : ' <span class="sch-view-custom-badge" style="font-size:0.65rem;vertical-align:middle;">kullanıcı view</span>';
        const customBadge = (v.isCustomizable && v.isInCatalog)
            ? ' <span class="sch-view-custom-badge" style="font-size:0.65rem;vertical-align:middle;">özelleştirilebilir</span>'
            : '';
        ELS.detailTitle.innerHTML = `${escapeHtml(v.name)}${catalogBadge}${customBadge}`;

        ELS.detailMeta.innerHTML = v.existsInDb
            ? `<div class="sch-meta-line">Kolon: ${v.columns.length} &nbsp;•&nbsp; <span style="color:var(--sch-badge-ix-fg,#166534);">Veritabanında mevcut</span></div>`
            : `<div class="sch-meta-line" style="color:var(--sch-warn-fg);">⚠ Veritabanında mevcut değil — kurulum tamamlanmamış olabilir</div>`;

        ELS.tabBody.innerHTML = renderViewDetail(v);
        bindViewDescSave(v.name);
    }

    function renderViewDetail(v) {
        // Katalog açıklaması (sadece system view'lar için)
        const catalogDescBlock = v.description
            ? `<div class="sch-table-summary" style="margin-bottom:14px;">${escapeHtml(v.description)}</div>`
            : '';

        // Kullanıldığı yer (katalog view'ları için)
        const usedInBlock = v.usedIn ? `
            <div style="margin-bottom:16px;">
                <div style="font-size:0.7rem;font-weight:600;color:var(--app-text-muted,#64748b);text-transform:uppercase;letter-spacing:0.05em;margin-bottom:4px;">Kullanıldığı Yer</div>
                <div style="font-size:0.82rem;">${escapeHtml(v.usedIn)}</div>
            </div>` : '';

        // Açıklama editörü (tüm view'lar için düzenlenebilir)
        const editableDesc = `
            <div style="margin-bottom:16px;">
                <div style="font-size:0.7rem;font-weight:600;color:var(--app-text-muted,#64748b);text-transform:uppercase;letter-spacing:0.05em;margin-bottom:6px;">Açıklama</div>
                <textarea id="view-desc-input"
                    placeholder="Bu view'ın ne yaptığını ve nerede kullanıldığını yazın…"
                    style="width:100%;min-height:72px;padding:8px 10px;border:1px solid var(--app-border,#e2e8f0);border-radius:6px;font-size:0.82rem;resize:vertical;background:var(--app-surface,#fff);color:var(--app-text,#0f172a);font-family:inherit;"
                >${escapeHtml(v.userDescription || '')}</textarea>
                <div style="display:flex;gap:8px;margin-top:6px;align-items:center;">
                    <button id="view-desc-save" style="padding:4px 14px;font-size:0.78rem;border:1px solid var(--app-accent,#6366f1);border-radius:5px;background:transparent;color:var(--app-accent,#6366f1);cursor:pointer;">Kaydet</button>
                    <span id="view-desc-status" style="font-size:0.75rem;color:var(--app-text-muted,#64748b);"></span>
                </div>
            </div>`;

        let colBlock = '';
        if (!v.existsInDb) {
            colBlock = `<div style="color:var(--sch-warn-fg);font-size:0.82rem;font-style:italic;">Bu view veritabanında bulunamadı — CalibraHub başlatılırken otomatik oluşturulur.</div>`;
        } else if (v.columns.length === 0) {
            colBlock = `<div style="color:var(--sch-empty-fg);font-size:0.82rem;font-style:italic;">Kolon bilgisi alınamadı.</div>`;
        } else {
            const colRows = v.columns.map(c =>
                `<div class="sch-view-col-item"><code>${escapeHtml(c)}</code></div>`
            ).join('');
            colBlock = `
                <div>
                    <div style="font-size:0.7rem;font-weight:600;color:var(--app-text-muted,#64748b);text-transform:uppercase;letter-spacing:0.05em;margin-bottom:6px;">Kolonlar (${v.columns.length})</div>
                    <div class="sch-view-col-list">${colRows}</div>
                </div>`;
        }

        return `<div style="padding:4px 0;">${catalogDescBlock}${usedInBlock}${editableDesc}${colBlock}</div>`;
    }

    function bindViewDescSave(viewName) {
        const saveBtn = document.getElementById('view-desc-save');
        const input = document.getElementById('view-desc-input');
        const status = document.getElementById('view-desc-status');
        if (!saveBtn || !input) return;

        saveBtn.addEventListener('click', async () => {
            saveBtn.disabled = true;
            status.textContent = 'Kaydediliyor…';
            try {
                const res = await fetch(`/admin/db-schema/api/views/${encodeURIComponent(viewName)}/description`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ description: input.value }),
                    credentials: 'same-origin',
                });
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                // state güncelle
                const v = state.views.find(x => x.name === viewName);
                if (v) v.userDescription = input.value;
                status.textContent = 'Kaydedildi ✓';
            } catch (e) {
                status.textContent = 'Hata: ' + e.message;
            } finally {
                saveBtn.disabled = false;
            }
        });
    }

    // ── Shared ──────────────────────────────────────────────────────────────

    function resetDetailPanel() {
        ELS.detailTitle.textContent = 'Tablo veya view secin';
        ELS.detailMeta.innerHTML = '';
        ELS.tabs.style.display = 'none';
        ELS.tabBody.innerHTML = '<div class="sch-empty">Sol panelden bir tablo veya view secin — metadata burada goruntulenir.</div>';
    }

    function switchTab(tab) {
        state.activeTab = tab;
        document.querySelectorAll('.sch-tab').forEach(el => {
            el.classList.toggle('active', el.dataset.tab === tab);
        });
        renderTabBody();
    }

    function renderTabBody() {
        if (!state.detail) return;
        switch (state.activeTab) {
            case 'columns': ELS.tabBody.innerHTML = renderColumns(state.detail.columns); break;
            case 'indexes': ELS.tabBody.innerHTML = renderIndexes(state.detail.indexes); break;
            case 'fks': ELS.tabBody.innerHTML = renderForeignKeys(state.detail.outgoingForeignKeys, state.detail.incomingForeignKeys); break;
        }
    }

    function renderColumns(cols) {
        if (cols.length === 0) return '<div class="sch-empty">Kolon bulunamadi.</div>';
        const rows = cols.map(c => {
            const badges = [
                c.isPrimaryKey ? '<span class="sch-badge pk">PK</span>' : '',
                c.isForeignKey ? '<span class="sch-badge fk">FK</span>' : '',
                c.isIdentity ? '<span class="sch-badge idx">IDENTITY</span>' : '',
                c.isNullable ? '<span class="sch-badge nullable">NULL</span>' : '',
            ].filter(Boolean).join('');
            const typeDisplay = formatType(c);
            const fkTarget = c.foreignKeyTarget ? `<div style="color:var(--app-text-muted,#64748b);font-size:0.72rem;">→ ${escapeHtml(c.foreignKeyTarget)}</div>` : '';
            const def = c.defaultDefinition ? `<code>${escapeHtml(c.defaultDefinition)}</code>` : '<span style="color:var(--sch-muted-border);">—</span>';
            const dictionary = renderDictionary(c);
            return `
                <tr>
                    <td>${c.ordinalPosition}</td>
                    <td><strong>${escapeHtml(c.name)}</strong>${fkTarget}</td>
                    <td>${escapeHtml(typeDisplay)}</td>
                    <td>${badges || '<span style="color:var(--sch-muted-border);">—</span>'}</td>
                    <td>${def}</td>
                    <td class="sch-dict-cell">${dictionary}</td>
                </tr>`;
        }).join('');
        return `
            <table class="sch-data-table">
                <thead>
                    <tr>
                        <th style="width:40px;">#</th>
                        <th>Kolon</th>
                        <th>Tip</th>
                        <th>Ozellikler</th>
                        <th>Default</th>
                        <th>Aciklama / Sozluk</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
    }

    function renderDictionary(c) {
        const parts = [];

        if (c.description) {
            parts.push(`<div class="sch-desc">${escapeHtml(c.description)}</div>`);
        }

        if (Array.isArray(c.enumValues) && c.enumValues.length > 0) {
            const items = c.enumValues.map(ev => {
                const label = ev.description ? ` — ${escapeHtml(ev.description)}` : '';
                return `<li><code>${ev.value}</code> <strong>${escapeHtml(ev.name)}</strong>${label}</li>`;
            }).join('');
            parts.push(`<details class="sch-enum-details"${c.enumValues.length <= 5 ? ' open' : ''}>
                <summary>Enum: ${escapeHtml(c.clrPropertyName || '')} (${c.enumValues.length} deger)</summary>
                <ul class="sch-enum-list">${items}</ul>
            </details>`);
        }

        if (parts.length === 0) {
            const marker = c.clrPropertyName
                ? `<span style="color:var(--sch-muted-border);font-size:0.72rem;">(${escapeHtml(c.clrPropertyName)} — aciklama yok)</span>`
                : '<span style="color:var(--sch-warn-soft-fg);font-size:0.72rem;" title="C# Entity property eslesmesi bulunamadi">⚠ Entity eslesmesi yok</span>';
            return marker;
        }

        return parts.join('');
    }

    function renderIndexes(indexes) {
        if (indexes.length === 0) return '<div class="sch-empty">Bu tabloda indeks yok.</div>';
        const rows = indexes.map(ix => {
            const flags = [];
            if (ix.isPrimaryKey) flags.push('<span class="sch-badge pk">PK</span>');
            if (ix.isUnique && !ix.isPrimaryKey) flags.push('<span class="sch-badge idx">UNIQUE</span>');
            flags.push(`<span style="color:var(--app-text-muted,#64748b);font-size:0.72rem;">${escapeHtml(ix.type)}</span>`);
            return `
                <tr>
                    <td><strong>${escapeHtml(ix.name)}</strong></td>
                    <td>${flags.join(' ')}</td>
                    <td>${escapeHtml(ix.columns.join(', '))}</td>
                </tr>`;
        }).join('');
        return `
            <table class="sch-data-table">
                <thead>
                    <tr><th>Ad</th><th>Tip</th><th>Kolonlar</th></tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
    }

    function renderForeignKeys(outgoing, incoming) {
        const outRows = outgoing.map(fk => `
            <tr>
                <td><code>${escapeHtml(fk.fromColumn)}</code></td>
                <td>→</td>
                <td><code>${escapeHtml(fk.toTable)}.${escapeHtml(fk.toColumn)}</code></td>
                <td>${escapeHtml(fk.constraintName)}</td>
                <td><span style="color:var(--app-text-muted,#64748b);font-size:0.72rem;">${escapeHtml(fk.deleteAction)}</span></td>
            </tr>`).join('');
        const inRows = incoming.map(fk => `
            <tr>
                <td><code>${escapeHtml(fk.fromTable)}.${escapeHtml(fk.fromColumn)}</code></td>
                <td>→</td>
                <td><code>${escapeHtml(fk.toColumn)}</code></td>
                <td>${escapeHtml(fk.constraintName)}</td>
                <td><span style="color:var(--app-text-muted,#64748b);font-size:0.72rem;">${escapeHtml(fk.deleteAction)}</span></td>
            </tr>`).join('');

        const outSection = outgoing.length > 0
            ? `<h4 style="margin:0 0 8px 0;font-size:0.85rem;">Giden FK'ler (bu tablo → diger tablolar)</h4>
               <table class="sch-data-table" style="margin-bottom:20px;">
                   <thead><tr><th>Kolon</th><th></th><th>Hedef</th><th>Constraint</th><th>Delete</th></tr></thead>
                   <tbody>${outRows}</tbody>
               </table>`
            : '<p style="color:var(--sch-empty-fg);font-size:0.82rem;">Giden FK yok.</p>';

        const inSection = incoming.length > 0
            ? `<h4 style="margin:16px 0 8px 0;font-size:0.85rem;">Gelen FK'ler (diger tablolardan bu tabloya)</h4>
               <table class="sch-data-table">
                   <thead><tr><th>Kaynak</th><th></th><th>Bu Tablo Kolon</th><th>Constraint</th><th>Delete</th></tr></thead>
                   <tbody>${inRows}</tbody>
               </table>`
            : '<p style="color:var(--sch-empty-fg);font-size:0.82rem;">Bu tabloya atifta bulunan FK yok.</p>';

        return outSection + inSection;
    }

    function formatType(c) {
        if (c.precision && c.scale !== null) return `${c.sqlType}(${c.precision},${c.scale})`;
        if (c.maxLength === -1) return `${c.sqlType}(MAX)`;
        if (c.maxLength) return `${c.sqlType}(${c.maxLength})`;
        return c.sqlType;
    }

    function formatNumber(n) {
        if (typeof n !== 'number') return '—';
        return n.toLocaleString('tr-TR');
    }

    function escapeHtml(s) {
        if (s === null || s === undefined) return '';
        return String(s).replace(/[&<>"']/g, ch => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[ch]));
    }

    function escapeAttr(s) {
        return escapeHtml(s);
    }
})();
