# CalibraHub — Preflight (deploy-öncesi denetim)

**Neden:** 2026-07-20'de dört ayrı bug'ın kökü aynıydı — **derleme temiz, kod
"doğru" görünüyor, ama runtime'da sessizce kırık.** Derleyici bunları yakalamaz.
Bu klasördeki tarayıcılar, mekanik yakalanabilen "sessiz kırık" sınıflarını
deploy'dan ÖNCE yüzeye çıkarır. Semantik olanları (kontrat uyuşmazlığı, karmaşık
SQL) `code-review` ajanı / insan tarar — ikisi tamamlayıcıdır.

## Çalıştırma (deploy öncesi)

```bash
bash preflight/check-silent-failures.sh        # statik, DB gerektirmez
pwsh preflight/check-sql-columns.ps1           # canlı şemaya karşı SQL kolon/tablo
```

Her ikisi de bulgu varsa exit 1 döner. Çıktı bir **"gözden geçir" listesidir** —
her satır bir bug DEĞİL. Tasarım: sıfır false-negative (gerçek bug'ı kaçırma),
false-positive'e tolerans.

## Hangi hata sınıfını yakalar

| Tarayıcı | Sınıf | Bugün tetikleyen bug |
|---|---|---|
| `check-sql-columns.ps1` | **SQL kolon/tablo uyumsuzluğu** — inline SQL'de şemada olmayan `alias.[Kolon]` veya `FROM/JOIN [.].[Tablo]` | FulfillmentLedger `Items.[MaterialCode]`→`Code`; PurchaseController `[MeasureUnits]`→`[Unit]` |
| `check-silent-failures.sh` | **Sessiz continue** (miktar/id kontrolüyle satır atlama) | Depo+üretim sarfı `if (qty<=0) continue` |
| `check-silent-failures.sh` | **Query'siz kök-relatif redirect** (iframe embed düşürebilir) | Tasarım Kuralları çift-şerit (`embed=1` düşüyordu) |
| `check-silent-failures.sh` | **Sessiz catch** (ÖZET; sayım verir, liste değil) | İş emri kaydetme — `catch(ex){generic}` gerçek hatayı yuttu |

## check-sql-columns'ın SINIRI (kritik — bunu bil)

Guard, `MaterialCode` gibi **başka sorgularda `AS [MaterialCode]` alias'ı olan**
bir adı "bilinen" sayar → o adın BAŞKA bir sorguda uydurma-fiziksel-kolon olarak
kullanımını **kaçırır** (global-alias false-negative). 2026-07-20'de
`PurchaseController.AllOpenRequestLines` bug'ını guard bu yüzden kaçırdı, ajan
semantik yakaladı. **Guard tek başına yetmez — mutasyon/SQL değişikliğinde
`code-review` ajanına da tarat.**

## Bilinen baseline false-positive'ler (bunlar bug DEĞİL)

Yeni bir gerçek bulgu göze çarpsın diye, mevcut (gözden geçirilmiş) false-positive'ler:

- **SQL kolon:** `LocId`, `Amount`, `Sign`, `Kod`, `idColumn`, `currency_code`,
  `item_id`, `ConfigurationCode`, `ComponentConfigCode`, `ParentMaterialCode`,
  `ComponentMaterialCode` — hepsi CTE/computed/dinamik-SQL alias'ı (fiziksel kolon değil).
- **SQL tablo:** `note_shares`, `note_attachments`, `note_reminder_targets`,
  `dynamic_field_values`, `material_card_field_options` — `IF OBJECT_ID ... IS NOT NULL`
  korumalı legacy cleanup / migration (tablo bilerek yok, kod korumalı).

Bu listedeki BİR ad çıkarsa yok say. **Listede OLMAYAN yeni bir ad çıkarsa incele** —
muhtemelen gerçek "Invalid column/object name" bug'ı.

## İlkeler (CLAUDE.md'de de var)

1. Yeni inline SQL yazınca kolon/tablo adlarını **INFORMATION_SCHEMA'dan doğrula**
   — `MaterialCode`/`MaterialName` FİZİKSEL kolon değil, yalnız alias; gerçek: `Code`/`Name`.
2. `catch`'te exception'ı **mutlaka logla** (mutasyon endpoint'lerinde), istemciye jenerik dön.
3. Bir satırı/kaydı **sessiz `continue` ile atlama** — kullanıcıya "şu atlandı" de veya reddet.
4. iframe-embed ekranlarında (`_DesignRulesTabs` deseni) navigasyon url'leri **gerekli query'yi koru**.
