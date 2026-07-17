/**
 * page-comments-admin.js — /PageComments yönetim ekranı.
 * page-comments-common.js'e bağımlıdır (önce o yüklenmeli).
 * Bespoke (SmartBoard KULLANILMAZ) — CLAUDE.md C-Grid header düzeni elle taklit edilir.
 */
(function () {
    'use strict';

    var CPC = window.CalibraPageComments;
    if (!CPC) return;

    var csrfToken = CPC.getCsrfToken(null);
    var authorName = document.body.getAttribute('data-user-name') || 'Kullanıcı';

    var searchInput   = document.getElementById('pcmSearch');
    var statusFilter  = document.getElementById('pcmStatusFilter');
    var refreshBtn    = document.getElementById('pcmRefreshBtn');
    var summaryEl     = document.getElementById('pcmSummary');
    var groupsEl      = document.getElementById('pcmGroups');
    var activityBody  = document.getElementById('pcmActivityBody');
    var activityPanel = document.getElementById('pcmActivityPanel');

    var allData = {}; // { screenKey: { comments: [...] } }

    function iconTrash() {
        return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16"></path><path d="M9 7V5.5A1.5 1.5 0 0 1 10.5 4h3A1.5 1.5 0 0 1 15 5.5V7"></path><path d="M7.5 7 8 19a1 1 0 0 0 1 .96h6a1 1 0 0 0 1-.96L16.5 7"></path><path d="M10 11v5"></path><path d="M14 11v5"></path></svg>';
    }

    // ── Durum filtre dropdown'u doldur ─────────────────────────────────
    (function buildStatusFilterOptions() {
        CPC.STATUS_LIST.forEach(function (s) {
            var meta = CPC.statusMeta(s);
            var opt = document.createElement('option');
            opt.value = s;
            opt.textContent = meta.label;
            statusFilter.appendChild(opt);
        });
    })();

    function loadAll() {
        groupsEl.innerHTML = '<div class="pcm-empty">Yükleniyor…</div>';
        CPC.apiFetch('', { method: 'GET' }, csrfToken).then(function (data) {
            allData = data || {};
            render();
        }).catch(function (err) {
            groupsEl.innerHTML = '<div class="pcm-empty">Yorumlar yüklenemedi: ' + CPC.escapeHtml(err.message) + '</div>';
        });
    }

    function loadActivity() {
        if (!activityPanel) return;
        CPC.apiFetch('/activity', { method: 'GET' }, csrfToken).then(function (data) {
            var items = [];
            if (Array.isArray(data)) items = data;
            else if (data && Array.isArray(data.items)) items = data.items;
            else if (data && Array.isArray(data.activity)) items = data.activity;
            if (!items.length) { activityPanel.hidden = true; return; }
            activityPanel.hidden = false;
            activityBody.innerHTML = items.slice(0, 30).map(function (it) {
                return '<div class="pcm-activity__item">' + CPC.escapeHtml(describeActivity(it)) + '</div>';
            }).join('');
        }).catch(function () {
            activityPanel.hidden = true; // aktivite paneli opsiyonel — sessiz geç
        });
    }

    function describeActivity(it) {
        if (typeof it === 'string') return it;
        if (!it || typeof it !== 'object') return JSON.stringify(it);
        var who = it.user || it.userName || it.author || '—';
        var what = it.action || it.type || it.message || it.text || 'işlem';
        var where = it.screenKey || it.key || it.pageTitle || '';
        var when = CPC.fmtDateTime(it.createdAt || it.ts || it.timestamp);
        var parts = [who, what];
        if (where) parts.push('— ' + where);
        if (when) parts.push('(' + when + ')');
        return parts.join(' ');
    }

    function matchesFilter(c, searchTerm, statusVal) {
        if (statusVal && String(c.status || '').toLowerCase() !== statusVal) return false;
        if (!searchTerm) return true;
        var t = searchTerm.trim().toLowerCase();
        var seqStr = String(c.seq != null ? c.seq : '');
        if (t.charAt(0) === '#') t = t.slice(1);
        if (t && seqStr === t) return true;
        var hay = [c.text, c.screenKey, c.pageTitle, c.author, c.devNote].filter(Boolean).join(' ').toLowerCase();
        return hay.indexOf(t) !== -1;
    }

    function render() {
        var searchTerm = (searchInput.value || '').trim();
        var statusVal = statusFilter.value || '';

        var keys = Object.keys(allData || {});
        var totalComments = 0, matchedComments = 0, matchedGroups = 0;
        var html = '';

        keys.sort(function (a, b) {
            var la = ((allData[a] && allData[a].comments) || []).length;
            var lb = ((allData[b] && allData[b].comments) || []).length;
            return lb - la;
        });

        keys.forEach(function (key) {
            var comments = (allData[key] && allData[key].comments) || [];
            totalComments += comments.length;
            var filtered = comments.filter(function (c) { return matchesFilter(c, searchTerm, statusVal); });
            if (!filtered.length) return;
            matchedGroups++;
            matchedComments += filtered.length;

            filtered = filtered.slice().sort(function (a, b) {
                var ta = Date.parse(a.ts || '') || 0, tb = Date.parse(b.ts || '') || 0;
                if (tb !== ta) return tb - ta;
                return (b.seq || 0) - (a.seq || 0);
            });

            var pageTitle = filtered[0].pageTitle || comments[0].pageTitle || '';
            html += '<details class="pcm-group" open>' +
                '<summary>' +
                    '<span class="pcm-group__key">' + CPC.escapeHtml(key) + '</span>' +
                    (pageTitle ? '<span class="pcm-group__title">' + CPC.escapeHtml(pageTitle) + '</span>' : '') +
                    '<span class="pcm-group__count">' + filtered.length + '</span>' +
                '</summary>' +
                '<div class="pcm-group__body">' +
                    filtered.map(renderCard).join('') +
                '</div>' +
            '</details>';
        });

        groupsEl.innerHTML = html || '<div class="pcm-empty">Filtreye uyan yorum bulunamadı.</div>';
        wireCardEvents();

        summaryEl.textContent = keys.length
            ? ('Toplam ' + totalComments + ' yorum · ' + keys.length + ' ekran' +
               (searchTerm || statusVal ? (' — filtre: ' + matchedComments + ' yorum / ' + matchedGroups + ' ekran') : ''))
            : 'Henüz yorum yok.';
    }

    function renderCard(c) {
        var meta = CPC.statusMeta(c.status);
        var devInfo = '';
        if (c.devNote || c.appliedVersion || c.resolvedBy) {
            devInfo = '<div class="pcm-revision-item" style="margin-bottom:8px;">' +
                (c.devNote ? ('<div>Not: ' + CPC.escapeHtml(c.devNote) + '</div>') : '') +
                (c.appliedVersion ? ('<div>Sürüm: <strong>' + CPC.escapeHtml(c.appliedVersion) + '</strong></div>') : '') +
                (c.resolvedBy ? ('<div>Çözümleyen: <strong>' + CPC.escapeHtml(c.resolvedBy) + '</strong>' +
                    (c.resolvedAt ? (' · ' + CPC.fmtDateTime(c.resolvedAt)) : '') + '</div>') : '') +
            '</div>';
        }

        var imagesHtml = '';
        if (Array.isArray(c.images) && c.images.length) {
            imagesHtml = '<div class="pcm-card__images">' + c.images.map(function (img) {
                return '<div class="pcm-image-thumb"><img src="' + CPC.imageUrl(img.id) + '" alt="' +
                    CPC.escapeHtml(img.name || 'görsel') + '" loading="lazy" data-full="' + CPC.imageUrl(img.id) + '" /></div>';
            }).join('') + '</div>';
        }

        var revisionsHtml = '';
        if (Array.isArray(c.revisions) && c.revisions.length) {
            revisionsHtml = '<div class="pcm-revisions">' + c.revisions.map(function (r) {
                var rMeta = CPC.statusMeta(r.status);
                return '<div class="pcm-revision-item"><strong>' + CPC.escapeHtml(rMeta.label) + '</strong>' +
                    (r.version ? (' · ' + CPC.escapeHtml(r.version)) : '') +
                    (r.userName ? (' · ' + CPC.escapeHtml(r.userName)) : '') +
                    (r.createdAt ? (' · ' + CPC.fmtDateTime(r.createdAt)) : '') +
                    (r.note ? ('<br/>' + CPC.escapeHtml(r.note)) : '') +
                '</div>';
            }).join('') + '</div>';
        }

        var statusOptions = CPC.STATUS_LIST.map(function (s) {
            var m = CPC.statusMeta(s);
            var sel = String(c.status || '').toLowerCase() === s ? ' selected' : '';
            return '<option value="' + s + '"' + sel + '>' + CPC.escapeHtml(m.label) + '</option>';
        }).join('');

        return '<div class="pcm-card" data-comment-id="' + CPC.escapeHtml(c.id) + '" data-screen-key="' + CPC.escapeHtml(c.screenKey || '') + '">' +
            '<div class="pcm-card__top">' +
                '<span class="pc-badge ' + meta.cls + '">' + CPC.escapeHtml(meta.label) + '</span>' +
                '<span class="pc-seq">#' + (c.seq != null ? c.seq : '—') + '</span>' +
                '<span class="pcm-card__author">' + CPC.escapeHtml(c.author || '') + '</span>' +
                '<span class="pcm-card__ts">' + CPC.fmtDateTime(c.ts) + '</span>' +
            '</div>' +
            '<div class="pcm-card__text">' + CPC.escapeHtml(c.text || '') + '</div>' +
            imagesHtml + devInfo + revisionsHtml +
            '<div class="pcm-devnote-form">' +
                '<div><label>Durum</label><select data-f="status">' +
                    '<option value="">— Değiştirme —</option>' + statusOptions +
                '</select></div>' +
                '<div><label>Uygulanan Sürüm</label><input type="text" data-f="version" placeholder="ör. v1.2.3" value="' + CPC.escapeHtml(c.appliedVersion || '') + '" /></div>' +
                '<div class="pcm-span2"><label>Geliştirici Notu</label><textarea data-f="note" rows="2" placeholder="Yanıt / açıklama…">' + CPC.escapeHtml(c.devNote || '') + '</textarea></div>' +
                '<div class="pcm-span2" style="display:flex;justify-content:flex-end;">' +
                    '<button type="button" class="pc-btn pc-btn--primary pc-btn--sm" data-act="save-status">Yanıtı Kaydet</button>' +
                '</div>' +
            '</div>' +
            '<div class="pcm-card__actions">' +
                '<button type="button" class="pc-icon-btn pc-icon-btn--danger" data-act="delete" title="Sil">' + iconTrash() + '</button>' +
            '</div>' +
        '</div>';
    }

    function wireCardEvents() {
        groupsEl.querySelectorAll('.pcm-card').forEach(function (card) {
            var id = card.getAttribute('data-comment-id');
            var key = card.getAttribute('data-screen-key');

            card.querySelectorAll('.pcm-image-thumb img').forEach(function (img) {
                img.addEventListener('click', function () {
                    window.open(img.getAttribute('data-full') || img.src, '_blank', 'noopener');
                });
            });

            var saveBtn = card.querySelector('[data-act="save-status"]');
            if (saveBtn) {
                saveBtn.addEventListener('click', function () {
                    var statusVal = card.querySelector('[data-f="status"]').value;
                    var versionVal = card.querySelector('[data-f="version"]').value.trim();
                    var noteVal = card.querySelector('[data-f="note"]').value.trim();
                    if (!statusVal) {
                        CPC.toast('error', 'Kaydetmeden önce bir durum seçin.');
                        return;
                    }
                    saveBtn.disabled = true;
                    CPC.apiFetch('/comment/status', {
                        method: 'POST',
                        body: JSON.stringify({
                            key: key, id: id, status: statusVal,
                            version: versionVal || undefined, note: noteVal || undefined, user: authorName
                        })
                    }, csrfToken).then(function () {
                        CPC.toast('success', 'Durum güncellendi.');
                        loadAll();
                        loadActivity();
                    }).catch(function (err) {
                        CPC.toast('error', 'Güncellenemedi: ' + err.message);
                    }).finally(function () { saveBtn.disabled = false; });
                });
            }

            var delBtn = card.querySelector('[data-act="delete"]');
            if (delBtn) {
                delBtn.addEventListener('click', function () {
                    var textPreview = card.querySelector('.pcm-card__text').textContent || '';
                    window.showConfirm({
                        title: 'Yorum silinsin mi?',
                        message: '"' + textPreview.slice(0, 140) + (textPreview.length > 140 ? '…' : '') + '" silinecek. Bu işlem geri alınamaz.',
                        okLabel: 'Sil', cancelLabel: 'Vazgeç', danger: true
                    }).then(function (ok) {
                        if (!ok) return;
                        CPC.apiFetch('/comment/delete', {
                            method: 'POST',
                            body: JSON.stringify({ key: key, id: id })
                        }, csrfToken).then(function () {
                            CPC.toast('success', 'Yorum silindi.');
                            loadAll();
                            loadActivity();
                        }).catch(function (err) {
                            CPC.toast('error', 'Silinemedi: ' + err.message);
                        });
                    });
                });
            }
        });
    }

    var searchDebounce = 0;
    searchInput.addEventListener('input', function () {
        clearTimeout(searchDebounce);
        searchDebounce = setTimeout(render, 150);
    });
    statusFilter.addEventListener('change', render);
    refreshBtn.addEventListener('click', function () { loadAll(); loadActivity(); });

    loadAll();
    loadActivity();
})();
