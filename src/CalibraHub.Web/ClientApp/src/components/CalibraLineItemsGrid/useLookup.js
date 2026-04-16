/**
 * useLookup — Lookup / options fetch + cache hook
 *
 * text-lookup ve select kolon tipleri icin C#'tan gelen URL'den veri ceker.
 * Sonuclari modul-level cache'de tutar (ayni URL tekrar fetch edilmez).
 *
 * URL template desteklenir: "/Sales/GetMaterialUnits?materialCode={materialCode}"
 *   → row objesindeki materialCode degeriyle replace edilir.
 */
import { useState, useEffect } from 'react'

var cache = {}
var inflight = {}

function resolveUrl(template, row) {
  if (!template) return ''
  return template.replace(/\{(\w+)\}/g, function(_, key) {
    var v = row && row[key]
    return v == null ? '' : encodeURIComponent(String(v))
  })
}

function fetchOnce(url) {
  if (cache[url]) return Promise.resolve(cache[url])
  if (inflight[url]) return inflight[url]

  var p = fetch(url, { credentials: 'same-origin' })
    .then(function(r) { return r.ok ? r.json() : [] })
    .then(function(data) {
      cache[url] = Array.isArray(data) ? data : []
      delete inflight[url]
      return cache[url]
    })
    .catch(function(e) {
      console.error('[useLookup] fetch error:', url, e)
      delete inflight[url]
      return []
    })

  inflight[url] = p
  return p
}

/**
 * @param {string} urlTemplate — "/Sales/GetMaterials" veya "/Sales/GetX?code={materialCode}"
 * @param {object} row — URL template substitusyonu icin
 * @returns {{ options: Array, loading: boolean }}
 */
export function useLookup(urlTemplate, row) {
  var [options, setOptions] = useState([])
  var [loading, setLoading] = useState(false)

  var url = resolveUrl(urlTemplate, row)

  useEffect(function() {
    if (!url) { setOptions([]); return }
    if (cache[url]) { setOptions(cache[url]); return }

    setLoading(true)
    var cancelled = false
    fetchOnce(url).then(function(data) {
      if (!cancelled) {
        setOptions(data)
        setLoading(false)
      }
    })
    return function() { cancelled = true }
  }, [url])

  return { options: options, loading: loading }
}

/** Manuel cache temizleme — testler icin */
export function clearLookupCache() {
  cache = {}
  inflight = {}
}
