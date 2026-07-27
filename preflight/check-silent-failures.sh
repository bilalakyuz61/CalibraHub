#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# CalibraHub preflight — "sessiz başarısızlık" tarayıcı (statik, DB'siz)
#
# NEDEN: 2026-07-20'de üç ayrı bug'ın kökü aynıydı — derleme temiz, kod "doğru"
# görünüyor, ama runtime'da sessizce kırık. Derleyici yakalamıyor. Bu script
# mekanik yakalanabilenleri tarar; SQL kolon uyumsuzluğu için ayrı script var
# (check-sql-columns.ps1, canlı şema gerektirir).
#
# ÇALIŞTIRMA: bash preflight/check-silent-failures.sh
# Çıktı "gözden geçir" listesidir — sıfır-false-negative hedefiyle geniş tarar.
# ─────────────────────────────────────────────────────────────────────────────
set -u
cd "$(dirname "$0")/.." || exit 2
SRC=src
HITS=0
hr() { printf '%s\n' "────────────────────────────────────────────────────────"; }
section() { hr; printf '  %s\n' "$1"; hr; }

# ── SINIF 2a — Sessiz catch (ÖZET; proje geneli desen, liste değil) ───────────
# İş emri kaydetme bug'ında (2026-07-20) gerçek hata görülemedi çünkü
# `catch (Exception ex) { return generic }` ex'i yutuyordu. Bu desen projede
# YAYGIN → tek tek liste gürültü olur. Yalnız SAYIM verilir; kural: YENİ mutasyon
# (kaydet/sil/güncelle) endpoint'i yazarken exception'ı logla (bkz. CLAUDE.md).
section "SINIF 2a — Sessiz catch (loglamayan) — ÖZET"
catch_n=$(grep -rniE "catch \((Exception|SqlException|[A-Za-z]+Exception) ([a-z][A-Za-z0-9]*)\)" "$SRC" --include=*.cs | wc -l)
printf '  %s adet yakalanan-exception bloğu var. Çoğu loglamıyor (proje deseni).\n' "$catch_n"
printf '  Bu bir liste DEĞİL — yeni yazdığın mutasyon endpoint\x27inde exception logla.\n\n'

# ── SINIF 2b — Sessiz continue/skip (DAR — gerçekten riskli) ───────────────────
# Depo + üretim sarfında `if (qty <= 0) continue;` satırı sessizce düşürüyordu
# (2026-07-20 düzeltildi). Toplu döngüde bir öğeyi "atlandı" demeden geçen
# continue → veri sessizce kaybolur. throw/hata içerenler zaten güvenli, elenir.
section "SINIF 2b — Sessiz continue (miktar/id kontrolüyle satır atlama)"
grep -rniE "(qty|quantity|miktar|amount)[a-z]* *(<=|<|==) *0[m]? *\) *(continue|return;)" "$SRC" --include=*.cs \
  | grep -viE "throw|error|hata|geçersiz|sıfırdan|return \(false" > /tmp/pf_continue.txt || true
cat /tmp/pf_continue.txt
c=$(grep -c . /tmp/pf_continue.txt || echo 0); HITS=$((HITS + c))
printf '  → %s aday\n\n' "$c"

# ── SINIF 4 — Query'siz kök-relatif redirect (iframe embed düşürebilir) ────────
# Tasarım Kuralları çift-şerit (2026-07-20): iframe içindeki
# `window.location.href = '/DocumentNumberRule'` embed=1'i düşürüyordu.
# Query taşımayan kök-relatif redirect'ler iframe-embed ekranlarında risklidir.
section "SINIF 4 — Query'siz kök-relatif redirect (.cshtml)"
grep -rniE "location\.(href|assign|replace)\s*[=(]\s*['\"]/[A-Za-z]" "$SRC/CalibraHub.Web/Views" --include=*.cshtml \
  | grep -viE "embed|workspace|entity=|\?|reload|\.href = '/'" > /tmp/pf_embed.txt || true
cat /tmp/pf_embed.txt
c=$(grep -c . /tmp/pf_embed.txt || echo 0); HITS=$((HITS + c))
printf '  → %s aday (parametreli redirect\x27ler elendi)\n\n' "$c"

hr
printf '  DAR sınıflar toplam: %s aday (catch özeti hariç). Her biri bug DEĞİL.\n' "$HITS"
printf '  SQL kolon uyumsuzluğu için: pwsh preflight/check-sql-columns.ps1\n'
hr
[ "$HITS" -gt 0 ] && exit 1 || exit 0
