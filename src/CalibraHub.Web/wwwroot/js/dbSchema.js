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
        filteredTables: [],
        selected: null,
        detail: null,
        activeTab: 'columns',
    };

    init();

    async function init() {
        bindEvents();
        await loadTables();
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
    }

    async function loadTables() {
        try {
            const res = await fetch(`${API_BASE}/tables`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            state.tables = await res.json();
            state.filteredTables = state.tables.slice();
            renderList();
        } catch (err) {
            ELS.list.innerHTML = `<div style="padding:12px;color:#dc2626;font-size:0.82rem;">Tablolar yuklenemedi: ${escapeHtml(err.message)}</div>`;
        }
    }

    function renderList() {
        ELS.count.textContent = `${state.filteredTables.length} / ${state.tables.length}`;
        if (state.filteredTables.length === 0) {
            ELS.list.innerHTML = '<div style="padding:12px;color:#94a3b8;font-size:0.82rem;">Eslesen tablo bulunamadi.</div>';
            return;
        }

        const html = state.filteredTables.map(t => {
            const key = `${t.schema}.${t.name}`;
            const active = state.selected === key ? ' active' : '';
            return `
                <div class="sch-table-row${active}" data-schema="${escapeAttr(t.schema)}" data-name="${escapeAttr(t.name)}">
                    <span class="sch-table-name">${escapeHtml(t.name)}</span>
                    <span class="sch-row-count">${formatNumber(t.rowCount)}</span>
                </div>`;
        }).join('');
        ELS.list.innerHTML = html;

        ELS.list.querySelectorAll('.sch-table-row').forEach(row => {
            row.addEventListener('click', () => {
                const schema = row.dataset.schema;
                const name = row.dataset.name;
                selectTable(schema, name);
            });
        });
    }

    function onSearch() {
        const q = ELS.search.value.trim().toLowerCase();
        if (!q) {
            state.filteredTables = state.tables.slice();
        } else {
            // Token arama (ornegin "doc ln" -> DocumentLine)
            const tokens = q.split(/\s+/).filter(Boolean);
            state.filteredTables = state.tables.filter(t => {
                const hay = (t.schema + '.' + t.name).toLowerCase();
                return tokens.every(tok => hay.includes(tok));
            });
        }
        renderList();
    }

    async function selectTable(schema, name) {
        state.selected = `${schema}.${name}`;
        renderList();
        ELS.detailTitle.textContent = `${schema}.${name}`;
        ELS.detailMeta.textContent = 'Yukleniyor...';
        ELS.tabs.style.display = 'none';
        ELS.tabBody.innerHTML = '<div class="sch-empty">Yukleniyor...</div>';
        try {
            const res = await fetch(`${API_BASE}/tables/${encodeURIComponent(schema)}/${encodeURIComponent(name)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            state.detail = await res.json();
            renderDetail();
        } catch (err) {
            ELS.tabBody.innerHTML = `<div class="sch-empty" style="color:#dc2626;">Detay yuklenemedi: ${escapeHtml(err.message)}</div>`;
        }
    }

    function renderDetail() {
        const d = state.detail;
        if (!d) return;
        ELS.detailMeta.textContent = `Satir: ${formatNumber(d.rowCount)} • Kolon: ${d.columns.length} • Indeks: ${d.indexes.length} • FK (giden): ${d.outgoingForeignKeys.length} • FK (gelen): ${d.incomingForeignKeys.length}`;
        ELS.colCount.textContent = `(${d.columns.length})`;
        ELS.idxCount.textContent = `(${d.indexes.length})`;
        ELS.fkCount.textContent = `(${d.outgoingForeignKeys.length + d.incomingForeignKeys.length})`;
        ELS.tabs.style.display = 'flex';
        renderTabBody();
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
            const fkTarget = c.foreignKeyTarget ? `<div style="color:#64748b;font-size:0.72rem;">→ ${escapeHtml(c.foreignKeyTarget)}</div>` : '';
            const def = c.defaultDefinition ? `<code>${escapeHtml(c.defaultDefinition)}</code>` : '<span style="color:#cbd5e1;">—</span>';
            return `
                <tr>
                    <td>${c.ordinalPosition}</td>
                    <td><strong>${escapeHtml(c.name)}</strong>${fkTarget}</td>
                    <td>${escapeHtml(typeDisplay)}</td>
                    <td>${badges || '<span style="color:#cbd5e1;">—</span>'}</td>
                    <td>${def}</td>
                </tr>`;
        }).join('');
        return `
            <table class="sch-data-table">
                <thead>
                    <tr><th style="width:40px;">#</th><th>Kolon</th><th>Tip</th><th>Ozellikler</th><th>Default</th></tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
    }

    function renderIndexes(indexes) {
        if (indexes.length === 0) return '<div class="sch-empty">Bu tabloda indeks yok.</div>';
        const rows = indexes.map(ix => {
            const flags = [];
            if (ix.isPrimaryKey) flags.push('<span class="sch-badge pk">PK</span>');
            if (ix.isUnique && !ix.isPrimaryKey) flags.push('<span class="sch-badge idx">UNIQUE</span>');
            flags.push(`<span style="color:#64748b;font-size:0.72rem;">${escapeHtml(ix.type)}</span>`);
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
                <td><span style="color:#64748b;font-size:0.72rem;">${escapeHtml(fk.deleteAction)}</span></td>
            </tr>`).join('');
        const inRows = incoming.map(fk => `
            <tr>
                <td><code>${escapeHtml(fk.fromTable)}.${escapeHtml(fk.fromColumn)}</code></td>
                <td>→</td>
                <td><code>${escapeHtml(fk.toColumn)}</code></td>
                <td>${escapeHtml(fk.constraintName)}</td>
                <td><span style="color:#64748b;font-size:0.72rem;">${escapeHtml(fk.deleteAction)}</span></td>
            </tr>`).join('');

        const outSection = outgoing.length > 0
            ? `<h4 style="margin:0 0 8px 0;font-size:0.85rem;">Giden FK'ler (bu tablo → diger tablolar)</h4>
               <table class="sch-data-table" style="margin-bottom:20px;">
                   <thead><tr><th>Kolon</th><th></th><th>Hedef</th><th>Constraint</th><th>Delete</th></tr></thead>
                   <tbody>${outRows}</tbody>
               </table>`
            : '<p style="color:#94a3b8;font-size:0.82rem;">Giden FK yok.</p>';

        const inSection = incoming.length > 0
            ? `<h4 style="margin:16px 0 8px 0;font-size:0.85rem;">Gelen FK'ler (diger tablolardan bu tabloya)</h4>
               <table class="sch-data-table">
                   <thead><tr><th>Kaynak</th><th></th><th>Bu Tablo Kolon</th><th>Constraint</th><th>Delete</th></tr></thead>
                   <tbody>${inRows}</tbody>
               </table>`
            : '<p style="color:#94a3b8;font-size:0.82rem;">Bu tabloya atifta bulunan FK yok.</p>';

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
