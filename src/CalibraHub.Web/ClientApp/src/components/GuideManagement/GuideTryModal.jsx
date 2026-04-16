/**
 * GuideTryModal — Rehberi canlı test etme modalı
 *
 * Açılınca /api/guides/{guideCode} endpoint'ini çağırır.
 * Arama kutusundan debounce ile filtre yapılır.
 * ValueColumn ve DisplayColumn farkı tablo başlıklarında badge ile
 * görsel olarak açıklanır.
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { X, Search, Loader2, FlaskConical, Info, Database, Tag } from 'lucide-react'
import { searchGuide } from '../../services/guideManagementService'

var PAGE_SIZE = 30

export default function GuideTryModal(props) {
  var isOpen  = props.isOpen
  var onClose = props.onClose
  var guide   = props.guide || null

  var [searchTerm, setSearchTerm] = useState('')
  var [rows,       setRows]       = useState([])
  var [columns,    setColumns]    = useState([])
  var [loading,    setLoading]    = useState(false)
  var [hasMore,    setHasMore]    = useState(false)
  var [page,       setPage]       = useState(1)
  var [error,      setError]      = useState(null)

  var debounceRef  = useRef(null)
  var searchRef    = useRef(null)

  var doSearch = useCallback(function(term, pg, reset) {
    if (!guide) return
    setLoading(true)
    setError(null)
    searchGuide(guide.guideCode, { search: term, page: pg, pageSize: PAGE_SIZE })
      .then(function(result) {
        var newRows = Array.isArray(result.rows) ? result.rows : []
        if (reset) {
          setRows(newRows)
        } else {
          setRows(function(prev) { return prev.concat(newRows) })
        }
        if (Array.isArray(result.columns) && result.columns.length > 0) {
          setColumns(result.columns)
        } else if (Array.isArray(guide.columns) && guide.columns.length > 0) {
          setColumns(guide.columns)
        }
        setHasMore(result.hasMore === true)
        setPage(pg)
      })
      .catch(function(err) {
        setError('Arama hatası: ' + err.message)
      })
      .finally(function() { setLoading(false) })
  }, [guide])

  // Modal açılınca sıfırla + ilk yükleme
  useEffect(function() {
    if (!isOpen || !guide) return
    setSearchTerm('')
    setRows([])
    setColumns(Array.isArray(guide.columns) ? guide.columns : [])
    setPage(1)
    setHasMore(false)
    setError(null)
    doSearch('', 1, true)
    // İlk açılışta arama inputuna focus
    setTimeout(function() {
      if (searchRef.current) searchRef.current.focus()
    }, 80)
  }, [isOpen, guide])

  // Debounced search — searchTerm değişince
  useEffect(function() {
    if (!isOpen || !guide) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(function() {
      doSearch(searchTerm, 1, true)
    }, 300)
    return function() { clearTimeout(debounceRef.current) }
  }, [searchTerm, isOpen])

  // ESC ile kapat
  useEffect(function() {
    if (!isOpen) return
    function onKey(e) { if (e.key === 'Escape') onClose && onClose() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  if (!isOpen || !guide) return null

  var valueCol   = guide.valueColumn   || ''
  var displayCol = guide.displayColumn || valueCol
  var isSameCol  = valueCol === displayCol

  return (
    <div
      className="guide-try-backdrop"
      onClick={function(e) { if (e.target === e.currentTarget) onClose && onClose() }}
    >
      <div className="guide-try-modal">

        {/* ── Başlık ── */}
        <div className="guide-try-header">
          <div className="guide-try-header-icon">
            <FlaskConical size={16} />
          </div>
          <div className="guide-try-header-text">
            <span className="guide-try-header-title">Rehberi Dene</span>
            <span className="guide-try-header-sub">{guide.guideLabel}</span>
          </div>
          <button type="button" className="guide-modal-close" onClick={onClose} aria-label="Kapat">
            <X size={18} />
          </button>
        </div>

        {/* ── Kolon açıklama bandı ── */}
        <div className="guide-try-info">
          <Info size={13} className="guide-try-info-ico" />
          <span className="guide-try-info-text">
            <span className="guide-try-col-chip guide-try-col-chip--val">
              <Database size={11} /> {valueCol}
            </span>
            &nbsp;veritabanına kaydedilir
            {!isSameCol && (
              <>
                &nbsp;·&nbsp;
                <span className="guide-try-col-chip guide-try-col-chip--disp">
                  <Tag size={11} /> {displayCol}
                </span>
                &nbsp;kullanıcıya gösterilir
              </>
            )}
            {isSameCol && (
              <span className="guide-try-same-note">&nbsp;(tek kolon — hem kaydedilir hem gösterilir)</span>
            )}
          </span>
        </div>

        {/* ── Arama kutusu ── */}
        <div className="guide-try-search-wrap">
          <Search size={15} className="guide-try-search-ico" />
          <input
            ref={searchRef}
            type="search"
            className="guide-try-search"
            placeholder="Kayıtlar arasında ara…"
            value={searchTerm}
            onChange={function(e) { setSearchTerm(e.target.value) }}
          />
          {loading && (
            <Loader2 size={15} className="gm-spin guide-try-search-spin" />
          )}
          {!loading && searchTerm && (
            <button
              type="button"
              className="gm-search-clear"
              onClick={function() { setSearchTerm('') }}
            >×</button>
          )}
        </div>

        {/* ── Tablo ── */}
        <div className="guide-try-table-wrap">
          {error ? (
            <div className="guide-try-error">{error}</div>
          ) : !loading && rows.length === 0 ? (
            <div className="guide-try-empty">
              {searchTerm
                ? '"' + searchTerm + '" ile eşleşen kayıt bulunamadı'
                : 'Kayıt bulunamadı'}
            </div>
          ) : (
            <table className="guide-try-table">
              <thead>
                <tr>
                  {columns.map(function(col) {
                    var isVal  = col === valueCol
                    var isDisp = !isSameCol && col === displayCol
                    return (
                      <th
                        key={col}
                        className={
                          'guide-try-th' +
                          (isVal  ? ' guide-try-th--val'  : '') +
                          (isDisp ? ' guide-try-th--disp' : '')
                        }
                      >
                        {col}
                        {isVal && (
                          <span className="guide-try-th-badge guide-try-th-badge--val">
                            <Database size={9} /> kaydedilir
                          </span>
                        )}
                        {isDisp && (
                          <span className="guide-try-th-badge guide-try-th-badge--disp">
                            <Tag size={9} /> gösterilir
                          </span>
                        )}
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {rows.map(function(row, idx) {
                  return (
                    <tr key={idx} className="guide-try-tr">
                      {columns.map(function(col) {
                        var isVal  = col === valueCol
                        var isDisp = !isSameCol && col === displayCol
                        var cell   = (row.cells && row.cells[col] != null)
                          ? String(row.cells[col])
                          : ''
                        return (
                          <td
                            key={col}
                            className={
                              'guide-try-td' +
                              (isVal  ? ' guide-try-td--val'  : '') +
                              (isDisp ? ' guide-try-td--disp' : '')
                            }
                          >
                            {cell}
                          </td>
                        )
                      })}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>

        {/* ── Footer: sayaç + daha fazla ── */}
        <div className="guide-try-footer">
          {rows.length > 0 && (
            <span className="guide-try-count">
              {loading ? '' : rows.length + ' kayıt'}
              {hasMore && !loading && ' (daha fazlası var)'}
            </span>
          )}
          <div className="guide-try-footer-right">
            {hasMore && !loading && (
              <button
                type="button"
                className="gm-btn gm-btn--ghost"
                style={{ padding: '5px 12px', fontSize: '.78rem' }}
                onClick={function() { doSearch(searchTerm, page + 1, false) }}
              >
                Daha fazla yükle
              </button>
            )}
            {loading && rows.length > 0 && (
              <span className="guide-try-count">
                <Loader2 size={12} className="gm-spin" style={{ display:'inline' }} /> Yükleniyor…
              </span>
            )}
          </div>
        </div>

      </div>
    </div>
  )
}
