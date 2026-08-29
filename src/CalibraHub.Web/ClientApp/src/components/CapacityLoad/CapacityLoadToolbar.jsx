import { Gauge, RefreshCw, Search, X, Filter, Download, Loader2 } from 'lucide-react'

/**
 * Kapasite / Yük Raporu şeridi — C-Grid sayfa standardı (2026-08-29).
 *
 * Sıra sabittir ve diğer liste ekranlarıyla aynıdır:
 *   [ikon] Başlık / alt başlık → [arama] → [rapor parametreleri] → [Filtre] [Excel] [Yenile]
 *
 * Rapor parametreleri (zaman kovası + tarih aralığı) şeritte AÇIKTA durur; bunlar
 * gizlenebilir bir filtre değil, raporun neyi gösterdiğini belirleyen girdilerdir —
 * panele saklanırsa kullanıcı hangi aralığa baktığını göremez.
 *
 * Renk efsanesi şeridin ALTINA alındı: şerit araç sırasının okunurluğunu bozuyordu.
 */
export default function CapacityLoadToolbar({
  bucket, onBucketChange, dateFrom, dateTo, onDateFromChange, onDateToChange,
  onRefresh, refreshing,
  search, onSearchChange,
  subtitle, filterCount, onOpenFilter, onExport, exportDisabled,
}) {
  return (
    <div className="cap-toolbar">
      <div className="cap-toolbar__id">
        <div className="cap-toolbar__icon"><Gauge size={18} strokeWidth={2} /></div>
        <div style={{ minWidth: 0 }}>
          <div className="cap-toolbar__title">Kapasite / Yük Raporu</div>
          <div className="cap-toolbar__sub">{subtitle}</div>
        </div>
      </div>

      <div className="cap-search">
        <Search size={13} />
        <input
          type="text"
          value={search}
          placeholder="Makine ara…"
          onChange={function (e) { onSearchChange(e.target.value) }}
        />
        {search && (
          <button type="button" className="cap-search__clear" title="Aramayı temizle"
                  onClick={function () { onSearchChange('') }}>
            <X size={12} />
          </button>
        )}
      </div>

      <div className="cap-toolbar-group">
        <span className="cap-toolbar-label">Zaman Kovası</span>
        <div className="cap-bucket-toggle">
          <button
            type="button"
            className={'cap-bucket-btn' + (bucket === 'day' ? ' is-active' : '')}
            onClick={function () { onBucketChange('day') }}
          >
            Gün
          </button>
          <button
            type="button"
            className={'cap-bucket-btn' + (bucket === 'week' ? ' is-active' : '')}
            onClick={function () { onBucketChange('week') }}
          >
            Hafta
          </button>
        </div>
      </div>

      <div className="cap-toolbar-group">
        <span className="cap-toolbar-label">Başlangıç</span>
        <input
          type="date"
          className="cap-date-input"
          value={dateFrom}
          onChange={function (e) { onDateFromChange(e.target.value) }}
        />
        <span className="cap-toolbar-label">Bitiş</span>
        <input
          type="date"
          className="cap-date-input"
          value={dateTo}
          onChange={function (e) { onDateToChange(e.target.value) }}
        />
      </div>

      {/* SIRA SmartBoard ile BIREBIR AYNI (2026-08-29 kullanici kurali):
          arama → Yenile → Filtre → Excel. Ayni isi yapan buton her ekranda ayni
          yerde durmali. Bu ekranda "yeni kayit" gibi bir ana eylem yok; Yenile
          diger board'lardaki gibi IKON butondur — primary gorunum verilseydi
          konumu da degisirdi (primary hep en sagdadir). */}
      <div className="cap-toolbar__tools">
        <button type="button" className="cap-icon-btn" title="Raporu yeniden hesapla"
                onClick={onRefresh} disabled={refreshing}>
          {refreshing ? <Loader2 size={15} className="cap-spin" /> : <RefreshCw size={15} />}
        </button>
        <button
          type="button"
          className={'cap-icon-btn' + (filterCount > 0 ? ' cap-icon-btn--active' : '')}
          title={filterCount > 0 ? (filterCount + ' filtre aktif') : 'Filtreleme'}
          onClick={onOpenFilter}
        >
          <Filter size={15} />
          {filterCount > 0 && <span className="cap-icon-btn__badge">{filterCount}</span>}
        </button>
        <button type="button" className="cap-icon-btn" title="Excel'e Aktar"
                onClick={onExport} disabled={exportDisabled}>
          <Download size={15} />
        </button>
      </div>
    </div>
  )
}
