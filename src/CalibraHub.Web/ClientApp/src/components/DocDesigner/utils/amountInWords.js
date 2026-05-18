const TR_ONES = ['', 'bir', 'iki', 'üç', 'dört', 'beş', 'altı', 'yedi', 'sekiz', 'dokuz']
const TR_TENS = ['', 'on', 'yirmi', 'otuz', 'kırk', 'elli', 'altmış', 'yetmiş', 'seksen', 'doksan']
const SCALES  = ['', 'bin', 'milyon', 'milyar', 'trilyon']

function group3(n) {
  if (n === 0) return ''
  let s = ''
  if (n >= 100) {
    const h = Math.floor(n / 100)
    s += (h === 1 ? '' : TR_ONES[h]) + 'yüz'
    n %= 100
  }
  if (n >= 10) { s += TR_TENS[Math.floor(n / 10)]; n %= 10 }
  s += TR_ONES[n]
  return s
}

/**
 * Sayıyı Türkçe yazıya çevirir.
 * Örnek: convertNumberToText(1234.56) → "bin ikiyüz otuz dört Türk Lirası elli altı kuruş"
 */
export function convertNumberToText(amount, currencyLabel = 'Türk Lirası') {
  if (amount == null || isNaN(Number(amount))) return ''
  const negative = Number(amount) < 0
  const num      = Math.abs(Number(amount))
  const intPart  = Math.floor(num)
  const fracPart = Math.round((num - intPart) * 100)

  let n = intPart
  let scale = 0
  const segments = []

  if (n === 0) {
    segments.push('sıfır')
  } else {
    while (n > 0) {
      const g = n % 1000
      if (g !== 0) {
        // Özel kural: 1000 → "bin" (NOT "bir bin"), 1_000_000 → "bir milyon"
        const prefix = scale === 1 && g === 1 ? '' : group3(g)
        const label  = SCALES[scale] ? ' ' + SCALES[scale] : ''
        segments.unshift(prefix + label)
      }
      n = Math.floor(n / 1000)
      scale++
    }
  }

  let result = (negative ? 'eksi ' : '') + segments.join(' ').replace(/\s+/g, ' ').trim()
  result += ' ' + currencyLabel
  if (fracPart > 0) result += ' ' + group3(fracPart) + ' kuruş'
  return result
}

/** Mock veri için sample tutar metni üretir */
export function mockAmountInWords() {
  return convertNumberToText(12345.67)
}
