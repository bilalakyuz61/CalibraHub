// CalibraHub – Turkish → English UI translation layer
// Injected by _Layout.cshtml only when the user's language preference is en-US.
// Uses MutationObserver so dynamically-rendered content (SmartBoard, Permissions page JS, etc.)
// is translated automatically without touching individual views.
(function () {
  'use strict';

  /* ────────────────────────────────────────────────────────────
     DICTIONARY  –  exact TR trimmed-string → EN replacement
  ──────────────────────────────────────────────────────────── */
  var T = {
    /* Actions / Buttons */
    'Kaydet': 'Save',
    'İptal': 'Cancel',
    'Vazgeç': 'Cancel',
    'Sil': 'Delete',
    'Düzenle': 'Edit',
    'Yeni': 'New',
    'Ekle': 'Add',
    'Tamam': 'OK',
    'Kapat': 'Close',
    'Onayla': 'Approve',
    'Reddet': 'Reject',
    'Geri': 'Back',
    'İleri': 'Next',
    'Seç': 'Select',
    'Temizle': 'Clear',
    'Ara': 'Search',
    'Filtre': 'Filter',
    'Filtrele': 'Filter',
    'Güncelle': 'Update',
    'Oluştur': 'Create',
    'Kopyala': 'Copy',
    'Yapıştır': 'Paste',
    'Yazdır': 'Print',
    'Gönder': 'Send',
    'Uygula': 'Apply',
    'Sıfırla': 'Reset',
    'Devam': 'Continue',
    'Yeni Ekle': 'Add New',
    'Tümünü Sil': 'Delete All',
    'Tümünü Seç': 'Select All',
    'Seçimi Temizle': 'Clear Selection',
    'Tümünü Temizle': 'Clear All',
    'Sayfayı Yenile': 'Refresh Page',
    'Yenile': 'Refresh',
    'Tamamla': 'Complete',
    'Onaya Gönder': 'Send for Approval',
    'Onayı Geri Al': 'Withdraw Approval',
    'Taşı': 'Move',
    'İndir': 'Download',
    'Yükle': 'Upload',
    'Önizle': 'Preview',
    'Görüntüle': 'View',
    'Aç': 'Open',
    'Göster': 'Show',
    'Gizle': 'Hide',
    'Dışa Aktar': 'Export',
    'İçe Aktar': 'Import',
    'Değiştir': 'Change',
    'Kopyala ve Düzenle': 'Copy & Edit',
    'Detaylar': 'Details',
    'Özet': 'Summary',
    'Toplu İşlem': 'Bulk Action',
    'Rapor Al': 'Get Report',
    'Excel İndir': 'Download Excel',
    'Evet': 'Yes',
    'Hayır': 'No',

    /* Permissions page */
    'Yetki Yönetimi': 'Permission Management',
    'Miras': 'Inherit',
    'Ver': 'Grant',
    'Hedef': 'Target',
    'HEDEF': 'TARGET',
    '— Seçiniz —': '— Select —',
    'Seçiniz': 'Select',
    'Varsayılan': 'Default',
    'Kaydediliyor...': 'Saving...',
    'Yükleniyor...': 'Loading...',
    'Kaydedildi': 'Saved',
    'Kaydedilemedi.': 'Save failed.',
    'Yukarıdan hedef seçin.': 'Select a target above.',
    'Soldan bir form seçin': 'Select a form from the left',
    'Sonuç yok.': 'No results.',
    'Form yok.': 'No forms.',
    'Görüntüle gerekli': 'View required',
    'Form ara…': 'Search form…',
    'Yetki ara…': 'Search permission…',

    /* Permission action labels (seeded, same in every deployment) */
    'Kendi Kayıtlarını Düzenle': 'Edit Own Records',
    'Tüm Kayıtları Düzenle': 'Edit All Records',
    'Kendi Kayıtlarını Sil': 'Delete Own Records',
    'Tüm Kayıtları Sil': 'Delete All Records',
    'Kendi Kayıtlarını Görüntüle': 'View Own Records',
    'Tüm Kayıtları Görüntüle': 'View All Records',

    /* Source badges */
    'Departman': 'Department',
    'Kullanıcı': 'User',

    /* System form names (Permissions left panel) */
    'Alan ve Widget Tanımlamaları': 'Field & Widget Definitions',
    'Belge Şablonları': 'Document Templates',
    'ERP Bağlantı Ayarları': 'ERP Connection Settings',
    'Entegrasyon Tanımları': 'Integration Definitions',
    'Entegratör Ayarları': 'Integrator Settings',
    'Kart Grupları': 'Card Groups',
    'Gruplar': 'Groups',
    'Satış Teklifi': 'Sales Quote',
    'Satış Siparişi': 'Sales Order',
    'Satın Alma Teklifi': 'Purchase Quote',
    'Satın Alma Siparişi': 'Purchase Order',
    'Cari Kart': 'Account Card',
    'Malzeme Kartı': 'Material Card',
    'İş Emri': 'Work Order',
    'Fiyat Grubu': 'Price Group',
    'e-Fatura': 'e-Invoice',
    'e-İrsaliye': 'e-Dispatch',
    'e-Arşiv': 'e-Archive',
    'Entegrasyon': 'Integration',
    'Entegrasyonlar': 'Integrations',

    /* Page/section titles */
    'Şirket Ayarları': 'Company Settings',
    'Şirket Parametreleri': 'Company Parameters',
    'Kullanıcı Tanımlamaları': 'User Definitions',
    'Departman Tanımlamaları': 'Department Definitions',
    'Makine Tanımlamaları': 'Machine Definitions',
    'Operasyon Tanımlamaları': 'Operation Definitions',
    'Personel Tanımlamaları': 'Personnel Definitions',
    'Rota Tanımlamaları': 'Routing Definitions',
    'Malzeme Kartları': 'Material Cards',
    'İş Emirleri': 'Work Orders',
    'Satış Teklifleri': 'Sales Quotes',
    'Satış Siparişleri': 'Sales Orders',
    'Satın Alma Teklifleri': 'Purchase Quotes',
    'Satın Alma Siparişleri': 'Purchase Orders',
    'Cari Hesaplar': 'Accounts',
    'Fiyat Listesi': 'Price List',
    'Döviz Tanımlamaları': 'Currency Definitions',
    'Lokasyon Tanımlamaları': 'Location Definitions',
    'Ölçü Birimleri': 'Measure Units',
    'Grup Tanımlamaları': 'Group Definitions',
    'Satış Temsilcileri': 'Sales Representatives',
    'Belge Tasarımcısı': 'Document Designer',
    'Tasarım Kuralları': 'Design Rules',
    'Onay Akış Tanımları': 'Approval Flow Definitions',
    'Onayda Bekleyenler': 'Pending Approvals',
    'Elektronik Belgeler': 'Electronic Documents',
    'Üretim Tanımlamaları': 'Production Definitions',
    'Üretim Terminali': 'Production Terminal',
    'Ürün Ağacı': 'Product Tree',
    'Alan Rehberi': 'Field Guide',
    'Veritabanı Haritası': 'Database Map',
    'Zamanlanmış Görevler': 'Scheduled Tasks',
    'Sistem Sağlık Kontrolü': 'System Health Check',
    'Sistem Yönetimi': 'System Management',
    'Organizasyon Şeması': 'Organization Chart',
    'Toplu Mail': 'Bulk Mail',
    'Panolar': 'Dashboards',
    'Ürün Konfigürasyonu': 'Product Configuration',
    'Kombinasyon Tanımları': 'Combination Definitions',
    'Tanımlamalar': 'Definitions',
    'Entegrasyon Wizard': 'Integration Wizard',
    'Notlar': 'Notes',

    /* Form labels */
    'Ad': 'Name',
    'Adı': 'Name',
    'Kod': 'Code',
    'Açıklama': 'Description',
    'Durum': 'Status',
    'Tarih': 'Date',
    'Başlangıç Tarihi': 'Start Date',
    'Bitiş Tarihi': 'End Date',
    'Başlangıç': 'Start',
    'Bitiş': 'End',
    'Tür': 'Type',
    'Miktar': 'Quantity',
    'Fiyat': 'Price',
    'Tutar': 'Amount',
    'Birim': 'Unit',
    'Grup': 'Group',
    'Kategori': 'Category',
    'Şirket': 'Company',
    'E-posta': 'E-mail',
    'E-Posta': 'E-mail',
    'Telefon': 'Phone',
    'Adres': 'Address',
    'Not': 'Note',
    'Renk': 'Color',
    'Sıra': 'Order',
    'Referans': 'Reference',
    'Para Birimi': 'Currency',
    'Vergi Oranı': 'Tax Rate',
    'İskonto': 'Discount',
    'Toplam': 'Total',
    'KDV': 'VAT',
    'Genel Toplam': 'Grand Total',
    'Açık': 'Open',
    'Kapalı': 'Closed',

    /* Status */
    'Aktif': 'Active',
    'Pasif': 'Inactive',
    'Bekliyor': 'Pending',
    'Onaylandı': 'Approved',
    'Reddedildi': 'Rejected',
    'Tamamlandı': 'Completed',
    'İptal Edildi': 'Cancelled',
    'Taslak': 'Draft',

    /* Empty / error states */
    'Kayıt bulunamadı': 'No records found',
    'Veri yok': 'No data',
    'Ara...': 'Search...',
    'Hızlı ara…': 'Quick search…',
    'Emin misiniz?': 'Are you sure?',
    'Bu işlem geri alınamaz.': 'This action cannot be undone.',
    'Board config bulunamadı': 'Board config not found',
    'SmartBoard mount fonksiyonu bulunamadı': 'SmartBoard mount function not found',
    'Bundle yüklenemedi': 'Bundle could not be loaded',

    /* Table / grid headers */
    'İşlem': 'Action',
    'İşlemler': 'Actions',
    'Oluşturulma Tarihi': 'Created Date',
    'Güncellenme Tarihi': 'Updated Date',
    'Oluşturan': 'Created By',
    'Güncelleyen': 'Updated By',

    /* Common section headers */
    'Genel Bilgiler': 'General Information',
    'Özellikler': 'Properties',
    'Filtreler': 'Filters',
    'Sonuçlar': 'Results',
    'Ayarlar': 'Settings',

    /* Misc labels */
    'Genel': 'General',
    'Raporlar': 'Reports',
    'Onay İşlemleri': 'Approval Processes',
    'Lojistik': 'Logistics',
    'Üretim': 'Production',
    'Finans': 'Finance',
    'Tasarım': 'Design',
    'Personel': 'Personnel',
    'Makine': 'Machine',
    'Operasyon': 'Operation',
    'Rota': 'Routing',
  };

  /* ────────────────────────────────────────────────────────────
     PATTERNS  –  regex for dynamic strings with numeric parts
  ──────────────────────────────────────────────────────────── */
  var PATTERNS = [
    // "5 / 10 yetki"  →  "5 / 10 permissions"
    { re: /^(\d+)\s*\/\s*(\d+)\s*yetki$/, fn: function (_, a, b) { return a + ' / ' + b + ' permissions'; } },
    // "Kaydedildi: 5 satır."  →  "Saved: 5 rows."
    { re: /^Kaydedildi:\s*(\d+)\s*satır\.$/, fn: function (_, n) { return 'Saved: ' + n + ' rows.'; } },
    // "Yükleme hatası: …"
    { re: /^Yükleme hatası:\s*(.*)$/, fn: function (_, e) { return 'Load error: ' + e; } },
    // "Hedef listesi yüklenemedi: …"
    { re: /^Hedef listesi yüklenemedi:\s*(.*)$/, fn: function (_, e) { return 'Could not load targets: ' + e; } },
    // "Hata: …"
    { re: /^Hata:\s*(.*)$/, fn: function (_, e) { return 'Error: ' + e; } },
    // "25 departman / makine / etc"
    { re: /^(\d+)\s*(departman|makine|birim|kayıt|personel|operasyon|rota|kök|kullanıcı)$/, fn: function (_, n, w) {
        var map = { departman:'departments', makine:'machines', birim:'units', kayıt:'records',
                    personel:'personnel', operasyon:'operations', rota:'routings', kök:'roots', kullanıcı:'users' };
        return n + ' ' + (map[w] || w);
    }},
  ];

  /* ────────────────────────────────────────────────────────────
     TARGET SELECTORS
     Only translate elements that are clearly UI chrome, NOT
     data cells (td), input values, entity names, etc.
  ──────────────────────────────────────────────────────────── */
  var QUERY = [
    /* Standard UI elements */
    'button',
    'label',
    'th',
    'legend',
    'option',
    'h1', 'h2', 'h3', 'h4', 'h5',
    /* Permissions page specific classes */
    '.pm-topbar__title',
    '.pm-aside-empty',
    '.pm-empty',
    '.pm-status',
    '.pm-action-row__note',
    '.pm-content__title',
    '.pm-content__sub',
    '.pm-form-item__name',
    '.pm-action-row__label',
    '.pm-source',
    /* SmartBoard / C-Grid page-level error states */
    '.sb-error-msg',
    '.sb-empty-msg',
    /* Modal titles in Bootstrap/custom modals */
    '.modal-title',
    /* Generic opt-in attribute */
    '[data-tr]',
  ].join(', ');

  /* ────────────────────────────────────────────────────────────
     CORE TRANSLATION HELPERS
  ──────────────────────────────────────────────────────────── */
  function tr(text) {
    if (!text) return text;
    var trimmed = text.trim();
    if (!trimmed) return text;
    if (Object.prototype.hasOwnProperty.call(T, trimmed)) {
      // Preserve leading/trailing whitespace
      return text.replace(trimmed, T[trimmed]);
    }
    for (var i = 0; i < PATTERNS.length; i++) {
      var m = trimmed.match(PATTERNS[i].re);
      if (m) return PATTERNS[i].fn.apply(null, m);
    }
    return text;
  }

  function translateEl(el) {
    var nodes = el.childNodes;
    for (var i = 0; i < nodes.length; i++) {
      var n = nodes[i];
      if (n.nodeType === 3 && n.nodeValue && n.nodeValue.trim()) {
        var v = tr(n.nodeValue);
        if (v !== n.nodeValue) n.nodeValue = v;
      }
    }
    if (el.placeholder) { var p = tr(el.placeholder); if (p !== el.placeholder) el.placeholder = p; }
    if (el.title)       { var t = tr(el.title);       if (t !== el.title)       el.title = t; }
  }

  function translateIn(root) {
    if (!root || !root.querySelectorAll) return;
    // Translate root element itself if it matches
    try {
      if (root.matches && root.matches(QUERY)) translateEl(root);
    } catch (e) { /* ignore */ }
    var found = root.querySelectorAll(QUERY);
    for (var i = 0; i < found.length; i++) translateEl(found[i]);
  }

  /* ────────────────────────────────────────────────────────────
     INITIAL RUN
  ──────────────────────────────────────────────────────────── */
  function runInitial() { translateIn(document.body); }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', runInitial);
  } else {
    runInitial();
  }

  /* ────────────────────────────────────────────────────────────
     MUTATIONOBSERVER  –  handles all dynamically inserted content
     (SmartBoard cards, Permissions page JS renders, modals, etc.)
  ──────────────────────────────────────────────────────────── */
  new MutationObserver(function (muts) {
    for (var mi = 0; mi < muts.length; mi++) {
      var added = muts[mi].addedNodes;
      for (var ni = 0; ni < added.length; ni++) {
        var n = added[ni];
        if (n.nodeType === 1) {
          // Element node: translate it and its descendants
          translateIn(n);
        } else if (n.nodeType === 3 && n.nodeValue && n.nodeValue.trim() && n.parentNode) {
          // Text node added directly to a target element
          // (e.g. statusEl.textContent = 'Yükleniyor...' creates a text node child)
          try {
            if (n.parentNode.matches && n.parentNode.matches(QUERY)) {
              var v2 = tr(n.nodeValue);
              if (v2 !== n.nodeValue) n.nodeValue = v2;
            }
          } catch (e) { /* ignore */ }
        }
      }
    }
  }).observe(document.documentElement, { childList: true, subtree: true });

  // Expose for manual override / testing
  window.__uiTr = tr;
})();
