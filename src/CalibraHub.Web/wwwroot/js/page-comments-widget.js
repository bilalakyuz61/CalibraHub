/**
 * page-comments-widget.js — Sayfa-içi Yorum sistemi: her sayfada sağ-altta
 * floating ✏️ buton + panel (admin-only). _Layout.cshtml içinde yalnızca
 * DepartmentManager|SystemAdmin rolü için (server-side, !isWorkspaceFrame)
 * mount edilen kökten (#pcWidgetRoot) yüklenir.
 *
 * Ekran anahtarı (screenKey) otomatik hesaplanır: kullanıcı hiçbir yerde
 * elle key yazmaz. CalibraHub React Shell mimarisinde gerçek sayfa içeriği
 * çoğunlukla bir workspace-tab iframe'i İÇİNDEDİR (bkz. Shell.jsx) — bu widget
 * dış (top-level) pencerede mount edildiği için, panel her açıldığında/paste
 * anında AKTİF (görünür) iframe'i bulup oradan gerçek pathname/title/formCode
 * okur; iframe yoksa (Ana Sayfa paneli veya Shell'siz sayfalar) doğrudan bu
 * pencerenin kendi konumunu kullanır.
 *
 * Bilinen kısıt: Dashboard'a (Ana Sayfa) bir tab AÇIKKEN dönülürse, Shell
 * dış pencere URL'ini yalnızca aktif tab değiştiğinde replaceState ile
 * günceller (Dashboard'a dönüşte tetiklenmez) — bu durumda widget son aktif
 * tab'ın screenKey'ini gösterebilir. Düzeltmek için Shell.jsx'e dokunmak
 * gerekir; bu görev kapsamında React bundle'a dokunulmadı (bkz. rapor).
 */
(function () {
    'use strict';

    if (window.__pcWidgetBooted) return;
    window.__pcWidgetBooted = true;

    function boot() {
        var CPC = window.CalibraPageComments;
        var root = document.getElementById('pcWidgetRoot');
        if (!CPC || !root) return;

        var csrfToken = CPC.getCsrfToken(root);
        var authorName = root.getAttribute('data-user-name') || 'Kullanıcı';

        // ── DOM iskeleti ────────────────────────────────────────────────
        var wrap = document.createElement('div');
        wrap.innerHTML =
            '<button type="button" id="pcFab" class="pc-fab" title="Sayfa Yorumları" aria-haspopup="dialog" aria-expanded="false">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>' +
            '</button>' +
            '<div id="pcPanel" class="pc-panel" role="dialog" aria-label="Sayfa Yorumları" hidden>' +
                '<div class="pc-panel__header">' +
                    '<div class="pc-panel__title">Sayfa Yorumları</div>' +
                    '<button type="button" id="pcCloseBtn" class="pc-panel__close" aria-label="Kapat">&times;</button>' +
                '</div>' +
                '<div class="pc-panel__context" id="pcContextLine">Bu sayfa: —</div>' +
                '<div class="pc-panel__compose">' +
                    '<textarea id="pcComposeText" class="pc-textarea" rows="3" placeholder="Bu sayfa hakkında yorum yazın… (Ctrl+V ile görsel ekleyebilirsiniz)"></textarea>' +
                    '<div class="pc-compose-row">' +
                        '<span class="pc-compose-hint" id="pcComposeHint"></span>' +
                        '<button type="button" id="pcSaveBtn" class="pc-btn pc-btn--primary">Kaydet</button>' +
                    '</div>' +
                    '<div class="pc-compose-error" id="pcComposeError" hidden></div>' +
                '</div>' +
                '<div class="pc-panel__list" id="pcCommentList"><div class="pc-empty">Yükleniyor…</div></div>' +
            '</div>';
        while (wrap.firstChild) root.appendChild(wrap.firstChild);

        var fabBtn      = document.getElementById('pcFab');
        var panelEl     = document.getElementById('pcPanel');
        var closeBtn    = document.getElementById('pcCloseBtn');
        var contextLine = document.getElementById('pcContextLine');
        var composeText = document.getElementById('pcComposeText');
        var composeHint = document.getElementById('pcComposeHint');
        var saveBtn     = document.getElementById('pcSaveBtn');
        var composeError = document.getElementById('pcComposeError');
        var listEl      = document.getElementById('pcCommentList');

        // Ctrl+V yapıştırmanın hedef alacağı yorum id'si. Tasarım kararı: görsel yalnızca
        // ZATEN KAYDEDİLMİŞ (sunucuda var olan) bir yoruma iliştirilir — henüz sunucuya
        // gitmemiş taslağa deferred/pending attach YAPILMAZ (id henüz gerçek bir kayda
        // bağlı değilken image POST'u backend'de yetim/hatalı referansa yol açabilir).
        // Yeni yorum kaydı başarıyla dönünce activeCommentId otomatik o yoruma set edilir.
        var activeCommentId = null;
        var currentCtx = null;
        var currentList = [];

        // ── Bağlam (screenKey / pageTitle / formCode) ──────────────────
        // Sayfadaki tüm görünür + erişilebilir (same-origin) iframe dokümanlarını döner.
        // Shell mimarisinde workspace-tab iframe'leri HEP mounted kalır (yalnız display:
        // block/none ile gösterilir/gizlenir — bkz. Shell.jsx), bu yüzden "görünür" filtresi
        // (display !== 'none') pratikte aktif tab'ı seçer. Cross-origin iframe'ler try/catch
        // ile sessizce atlanır (contentDocument erişimi SecurityError fırlatır).
        function getAccessibleFrameDocuments() {
            var result = [];
            var frames;
            try { frames = document.querySelectorAll('iframe'); } catch (e) { return result; }
            for (var i = 0; i < frames.length; i++) {
                var f = frames[i];
                try {
                    if (f.style && f.style.display === 'none') continue;
                    var d = f.contentDocument;
                    if (d && d.body) result.push(d);
                } catch (e) { /* cross-origin — atla */ }
            }
            return result;
        }

        function getActiveContentDocument() {
            var docs = getAccessibleFrameDocuments();
            return docs.length ? docs[0] : null;
        }

        function getPageContext() {
            var doc = getActiveContentDocument();
            var win = (doc && doc.defaultView) || window;
            var body = doc ? doc.body : document.body;
            var rawPath = '/';
            try { rawPath = win.location.pathname || '/'; } catch (e) { /* cross-origin fallback */ }
            var rawTitle = (doc ? doc.title : document.title) || 'CalibraHub';
            var pageTitle = rawTitle.replace(/\s*-\s*CalibraHub\s*$/i, '').trim() || rawTitle;
            var formCode = body ? body.getAttribute('data-form-code') : null;
            if (!formCode) formCode = null;
            return {
                key: CPC.normalizeKey(rawPath),
                pageTitle: pageTitle,
                formCode: formCode
            };
        }

        // ── Render ──────────────────────────────────────────────────────
        function renderContext() {
            currentCtx = getPageContext();
            contextLine.textContent = 'Bu sayfa: ' + currentCtx.key;
        }

        function iconEdit() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>';
        }
        function iconTrash() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16"></path><path d="M9 7V5.5A1.5 1.5 0 0 1 10.5 4h3A1.5 1.5 0 0 1 15 5.5V7"></path><path d="M7.5 7 8 19a1 1 0 0 0 1 .96h6a1 1 0 0 0 1-.96L16.5 7"></path><path d="M10 11v5"></path><path d="M14 11v5"></path></svg>';
        }
        function iconClip() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.44 11.05 12.25 20.24a6 6 0 0 1-8.49-8.49L12.95 2.56a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"/></svg>';
        }

        function renderList(items) {
            currentList = items || [];
            if (!currentList.length) {
                listEl.innerHTML = '<div class="pc-empty">Bu sayfa için henüz yorum yok. İlk yorumu siz ekleyin.</div>';
                return;
            }
            // En yeni üstte
            var sorted = currentList.slice().sort(function (a, b) {
                var ta = Date.parse(a.ts || '') || 0, tb = Date.parse(b.ts || '') || 0;
                if (tb !== ta) return tb - ta;
                return (b.seq || 0) - (a.seq || 0);
            });
            listEl.innerHTML = '';
            sorted.forEach(function (c) { listEl.appendChild(renderItem(c)); });
        }

        function renderItem(c) {
            var meta = CPC.statusMeta(c.status);
            var el = document.createElement('div');
            el.className = 'pc-item' + (c.id === activeCommentId ? ' is-active' : '');
            el.setAttribute('data-comment-id', c.id);

            var devNoteHtml = '';
            if (c.devNote || c.appliedVersion) {
                devNoteHtml = '<div class="pc-item__devnote">' +
                    (c.devNote ? CPC.escapeHtml(c.devNote) : '') +
                    (c.appliedVersion ? ' <strong>(' + CPC.escapeHtml(c.appliedVersion) + ')</strong>' : '') +
                    '</div>';
            }

            var imagesHtml = '';
            if (Array.isArray(c.images) && c.images.length) {
                imagesHtml = '<div class="pc-item__images">' + c.images.map(function (img) {
                    return '<div class="pc-item-thumb"><img src="' + CPC.imageUrl(img.id) + '" alt="' +
                        CPC.escapeHtml(img.name || 'görsel') + '" loading="lazy" /></div>';
                }).join('') + '</div>';
            }

            el.innerHTML =
                '<div class="pc-item__top">' +
                    '<span class="pc-badge ' + meta.cls + '">' + CPC.escapeHtml(meta.label) + '</span>' +
                    '<span class="pc-seq">#' + (c.seq != null ? c.seq : '—') + '</span>' +
                    '<span class="pc-author">' + CPC.escapeHtml(c.author || '') + '</span>' +
                    '<span class="pc-ts">' + CPC.fmtDateTime(c.ts) + '</span>' +
                '</div>' +
                '<div class="pc-item__text">' + CPC.escapeHtml(c.text || '') + '</div>' +
                devNoteHtml + imagesHtml +
                '<div class="pc-item__actions">' +
                    '<button type="button" class="pc-icon-btn' + (c.id === activeCommentId ? ' is-on' : '') + '" data-act="attach" title="Görsel eklemek için aktif yap, sonra Ctrl+V yapıştırın">' + iconClip() + '</button>' +
                    '<button type="button" class="pc-icon-btn" data-act="edit" title="Düzenle">' + iconEdit() + '</button>' +
                    '<button type="button" class="pc-icon-btn pc-icon-btn--danger" data-act="delete" title="Sil">' + iconTrash() + '</button>' +
                '</div>';

            el.querySelector('[data-act="attach"]').addEventListener('click', function () {
                activeCommentId = (activeCommentId === c.id) ? null : c.id;
                renderList(currentList);
                updateComposeHint();
            });
            el.querySelector('[data-act="edit"]').addEventListener('click', function () { startEdit(el, c); });
            el.querySelector('[data-act="delete"]').addEventListener('click', function () { deleteComment(c); });

            return el;
        }

        function startEdit(itemEl, c) {
            if (itemEl.querySelector('.pc-item__edit-box')) return; // zaten düzenleme modunda
            var box = document.createElement('div');
            box.className = 'pc-item__edit-box';
            box.innerHTML =
                '<textarea class="pc-textarea" rows="3">' + CPC.escapeHtml(c.text || '') + '</textarea>' +
                '<div class="pc-item__edit-actions">' +
                    '<button type="button" class="pc-btn pc-btn--primary pc-btn--sm" data-act="save">Güncelle</button>' +
                    '<button type="button" class="pc-btn pc-btn--ghost pc-btn--sm" data-act="cancel">Vazgeç</button>' +
                '</div>';
            itemEl.querySelector('.pc-item__text').style.display = 'none';
            itemEl.insertBefore(box, itemEl.querySelector('.pc-item__actions'));
            var ta = box.querySelector('textarea');
            ta.focus();
            box.querySelector('[data-act="cancel"]').addEventListener('click', function () {
                box.remove();
                itemEl.querySelector('.pc-item__text').style.display = '';
            });
            box.querySelector('[data-act="save"]').addEventListener('click', function () {
                var newText = ta.value.trim();
                if (!newText) { ta.focus(); return; }
                CPC.apiFetch('/comment/edit', {
                    method: 'POST',
                    body: JSON.stringify({ key: currentCtx.key, id: c.id, text: newText, user: authorName })
                }, csrfToken).then(function () {
                    CPC.toast('success', 'Yorum güncellendi.');
                    loadComments();
                }).catch(function (err) {
                    CPC.toast('error', 'Güncellenemedi: ' + err.message);
                });
            });
        }

        function deleteComment(c) {
            window.showConfirm({
                title: 'Yorum silinsin mi?',
                message: '"' + (c.text || '').slice(0, 140) + (c.text && c.text.length > 140 ? '…' : '') + '" silinecek. Bu işlem geri alınamaz.',
                okLabel: 'Sil',
                cancelLabel: 'Vazgeç',
                danger: true
            }).then(function (ok) {
                if (!ok) return;
                CPC.apiFetch('/comment/delete', {
                    method: 'POST',
                    body: JSON.stringify({ key: currentCtx.key, id: c.id })
                }, csrfToken).then(function () {
                    if (activeCommentId === c.id) activeCommentId = null;
                    CPC.toast('success', 'Yorum silindi.');
                    loadComments();
                }).catch(function (err) {
                    CPC.toast('error', 'Silinemedi: ' + err.message);
                });
            });
        }

        function updateComposeHint() {
            if (activeCommentId) {
                var target = currentList.filter(function (c) { return c.id === activeCommentId; })[0];
                composeHint.textContent = target
                    ? ('Görsel yapıştırma hedefi: #' + (target.seq != null ? target.seq : '—') + ' (Ctrl+V)')
                    : '';
            } else {
                composeHint.textContent = '';
            }
        }

        // ── Veri yükleme ────────────────────────────────────────────────
        function loadComments() {
            listEl.innerHTML = '<div class="pc-empty">Yükleniyor…</div>';
            CPC.apiFetch('', { method: 'GET' }, csrfToken).then(function (data) {
                var forKey = (data && currentCtx && data[currentCtx.key] && data[currentCtx.key].comments) || [];
                renderList(forKey);
            }).catch(function (err) {
                listEl.innerHTML = '<div class="pc-empty">Yorumlar yüklenemedi: ' + CPC.escapeHtml(err.message) + '</div>';
            });
        }

        // ── Panel aç/kapat ──────────────────────────────────────────────
        // Esc / dışarı-tık kapsamı: gerçek sayfa içeriği çoğunlukla bir workspace-tab
        // IFRAME'i İÇİNDEDİR (bkz. dosya başı açıklaması). document.addEventListener
        // yalnız ÜST dokümana takılırsa, kullanıcı sayfa içeriğine (iframe'e) tıkladıktan
        // sonra klavye odağı iframe'de olur — Esc keydown'u ya da mousedown'u üst dokümana
        // HİÇ ULAŞMAZ (frame sınırını bubble/capture ile aşmaz) → panel kapanmaz. Çözüm:
        // panel açıkken görünür + erişilebilir (same-origin) iframe dokümanlarına da AYNI
        // dinleyicileri geçici olarak takıyoruz; panel kapanınca hepsi sökülür (leak yok).
        var attachedFrameDocs = [];
        var frameWatchTimer = null;

        function attachFrameListeners() {
            var docs = getAccessibleFrameDocuments();
            docs.forEach(function (d) {
                if (attachedFrameDocs.indexOf(d) !== -1) return;
                try {
                    d.addEventListener('keydown', onKeydown, true);
                    d.addEventListener('mousedown', onOutsideClick, true);
                    attachedFrameDocs.push(d);
                } catch (e) { /* erişilemeyen döküman — atla */ }
            });
            // Artık görünür/erişilebilir olmayan (tab değişti, iframe içinde sayfa yeniden
            // yüklendi) dokümanları bırak — stale referans birikmesin.
            attachedFrameDocs = attachedFrameDocs.filter(function (d) {
                if (docs.indexOf(d) !== -1) return true;
                try {
                    d.removeEventListener('keydown', onKeydown, true);
                    d.removeEventListener('mousedown', onOutsideClick, true);
                } catch (e) { /* yok say */ }
                return false;
            });
        }
        function detachFrameListeners() {
            attachedFrameDocs.forEach(function (d) {
                try {
                    d.removeEventListener('keydown', onKeydown, true);
                    d.removeEventListener('mousedown', onOutsideClick, true);
                } catch (e) { /* yok say */ }
            });
            attachedFrameDocs = [];
        }

        function openPanel() {
            renderContext();
            panelEl.hidden = false;
            fabBtn.setAttribute('aria-expanded', 'true');
            loadComments();
            setTimeout(function () { composeText.focus(); }, 30);
            document.addEventListener('keydown', onKeydown, true);
            document.addEventListener('mousedown', onOutsideClick, true);
            attachFrameListeners();
            // Panel açıkken kullanıcı workspace sekmesi değiştirebilir (başka bir iframe
            // görünür olur) ya da iframe içinde sayfa yeniden yüklenebilir (yeni
            // contentDocument) — kısa aralıklarla yeniden tara ki Esc/dışarı-tık her zaman
            // o an görünür iframe'e bağlı kalsın. Basit tutuldu: tam bir mutation-observer
            // yerine 1sn'lik poll (widget'ın mevcut ensureDocked deseniyle tutarlı).
            frameWatchTimer = setInterval(attachFrameListeners, 1000);
        }
        function closePanel() {
            panelEl.hidden = true;
            fabBtn.setAttribute('aria-expanded', 'false');
            document.removeEventListener('keydown', onKeydown, true);
            document.removeEventListener('mousedown', onOutsideClick, true);
            if (frameWatchTimer) { clearInterval(frameWatchTimer); frameWatchTimer = null; }
            detachFrameListeners();
        }
        function onKeydown(e) {
            if (e.key === 'Escape') closePanel();
        }
        function onOutsideClick(e) {
            if (panelEl.hidden) return;
            // panelEl/fabBtn üst dokümana ait; iframe içinden gelen event'in target'ı başka
            // bir dokümana ait olduğundan contains() doğal olarak false döner (hata fırlatmaz)
            // — yani iframe içi HER tıklama zaten doğru şekilde "dışarı" sayılır.
            if (panelEl.contains(e.target) || fabBtn.contains(e.target)) return;
            closePanel();
        }

        fabBtn.addEventListener('click', function () {
            if (panelEl.hidden) openPanel(); else closePanel();
        });
        closeBtn.addEventListener('click', closePanel);

        // ── Shell header'a yerleşme (zil simgesinin yanı) ───────────────
        // React Shell üst çubuğu #pcHeaderSlot yuvasını render eder (Shell.jsx).
        // Yuva bulununca buton + panel oraya taşınır (DOM taşıma listener'ları korur);
        // Shell'siz top-level sayfalarda yüzer (fixed) mod olduğu gibi kalır.
        // Interval sürekli çalışır: React, header'ı unmount/remount ederse yuva boş
        // yeniden doğar — buton kopmuş kalmasın diye her turda yeniden yerleşilir.
        function ensureDocked() {
            var slot = document.getElementById('pcHeaderSlot');
            if (slot) {
                if (fabBtn.parentNode !== slot) {
                    slot.appendChild(fabBtn);
                    slot.appendChild(panelEl);
                    fabBtn.classList.add('pc-fab--header');
                    panelEl.classList.add('pc-panel--header');
                }
            } else if (fabBtn.parentNode !== root) {
                root.appendChild(fabBtn);
                root.appendChild(panelEl);
                fabBtn.classList.remove('pc-fab--header');
                panelEl.classList.remove('pc-panel--header');
            }
        }
        ensureDocked();
        setInterval(ensureDocked, 500);

        // ── Yeni yorum kaydet (optimistik) ─────────────────────────────
        function showComposeError(msg) {
            if (!msg) { composeError.hidden = true; composeError.textContent = ''; return; }
            composeError.hidden = false;
            composeError.textContent = msg;
        }

        function submitComment() {
            var text = composeText.value.trim();
            if (!text) { composeText.focus(); return; }
            showComposeError(null);

            var id = CPC.uuid();
            var optimistic = {
                id: id, seq: null, screenKey: currentCtx.key, pageTitle: currentCtx.pageTitle,
                formCode: currentCtx.formCode, text: text, ts: new Date().toISOString(),
                author: authorName, status: null, images: []
            };
            currentList = [optimistic].concat(currentList);
            renderList(currentList);

            saveBtn.disabled = true;
            saveBtn.textContent = 'Kaydediliyor…';

            CPC.apiFetch('/comment', {
                method: 'POST',
                body: JSON.stringify({
                    key: currentCtx.key, id: id, text: text, ts: optimistic.ts, author: authorName,
                    pageTitle: currentCtx.pageTitle, formCode: currentCtx.formCode
                })
            }, csrfToken).then(function () {
                composeText.value = '';
                activeCommentId = id;
                CPC.toast('success', 'Yorum kaydedildi.');
                loadComments();
            }).catch(function (err) {
                // optimistik geri al
                currentList = currentList.filter(function (c) { return c.id !== id; });
                renderList(currentList);
                showComposeError('Kaydedilemedi: ' + err.message);
            }).finally(function () {
                saveBtn.disabled = false;
                saveBtn.textContent = 'Kaydet';
                updateComposeHint();
            });
        }
        saveBtn.addEventListener('click', submitComment);
        composeText.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); submitComment(); }
        });

        // ── Ctrl+V görsel yapıştırma (panel açıkken, aktif yoruma iliştir) ──
        function handlePaste(e) {
            if (panelEl.hidden) return;
            var items = (e.clipboardData && e.clipboardData.items) || [];
            var imageItem = null;
            for (var i = 0; i < items.length; i++) {
                if (items[i].type && items[i].type.indexOf('image/') === 0) { imageItem = items[i]; break; }
            }
            if (!imageItem) return; // normal metin yapıştırması — dokunma
            e.preventDefault();

            if (!activeCommentId) {
                showComposeError('Görsel eklemeden önce bir yorumu kaydedin ya da listede 📎 ile aktif hedef seçin.');
                return;
            }
            var targetId = activeCommentId;
            var blob = imageItem.getAsFile();
            if (!blob) return;

            var previewUrl = URL.createObjectURL(blob);
            var thumbHost = findImagesHostFor(targetId);
            var tempThumb = document.createElement('div');
            tempThumb.className = 'pc-item-thumb';
            tempThumb.innerHTML = '<img src="' + previewUrl + '" alt="yapıştırılan görsel" />';
            if (thumbHost) thumbHost.appendChild(tempThumb);

            var imgId = CPC.uuid();
            CPC.fileToBase64(blob).then(function (base64) {
                return CPC.apiFetch('/comment/image', {
                    method: 'POST',
                    body: JSON.stringify({
                        commentId: targetId, id: imgId, mime: blob.type || 'image/png',
                        dataBase64: base64, name: 'yapistirilan-' + Date.now() + '.png'
                    })
                }, csrfToken);
            }).then(function () {
                CPC.toast('success', 'Görsel eklendi.');
                loadComments();
            }).catch(function (err) {
                if (tempThumb.parentNode) tempThumb.parentNode.removeChild(tempThumb);
                CPC.toast('error', 'Görsel eklenemedi: ' + err.message);
            });
        }
        function findImagesHostFor(commentId) {
            var itemEl = listEl.querySelector('.pc-item[data-comment-id="' + CSS.escape(String(commentId)) + '"]');
            if (!itemEl) return null;
            var host = itemEl.querySelector('.pc-item__images');
            if (!host) {
                host = document.createElement('div');
                host.className = 'pc-item__images';
                itemEl.insertBefore(host, itemEl.querySelector('.pc-item__actions'));
            }
            return host;
        }
        document.addEventListener('paste', handlePaste);

        updateComposeHint();
    }

    function ensureDeps(cb) {
        if (window.CalibraPageComments && typeof window.showConfirm === 'function') { cb(); return; }
        var tries = 0;
        var timer = setInterval(function () {
            if ((window.CalibraPageComments && typeof window.showConfirm === 'function') || ++tries >= 60) {
                clearInterval(timer);
                cb();
            }
        }, 50);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { ensureDeps(boot); });
    } else {
        ensureDeps(boot);
    }
})();
