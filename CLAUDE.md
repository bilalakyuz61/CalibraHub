# CalibraHub – Agent Çalışma Kuralları

## Build & run sorumluluğu

Tüm geliştirme akışı Claude üzerinden yürür. Kod değişikliği yaptığında **build alıp gerekirse sunucuyu yeniden başlatmak senin işin** — kullanıcıdan bunu istemene gerek yok.

### Akış
- **C# / `.cshtml` değişikliği** → `dotnet build` veya doğrudan `dotnet run` ile yeniden başlat. Önce port 61001'i kontrol et (`netstat`); açıksa çalışan process'i durdurup yeniden başlat (yalnızca senin başlattığın process'lere dokun, yabancı process varsa kullanıcıya sor).
- **`.jsx` / CSS değişikliği** → `wwwroot/react/` bundle'ı `npm run build` ile üretilir. JSX'te değişiklik varsa build et, ardından backend'i yeniden başlat (yeni bundle'ın yüklenmesi için).
- **Run sonrası** → "Now listening on" log'unu bekle, smoke test yap (`curl localhost:61001/`), 200/302 görünce hazırdır.
- **Verification gerekiyorsa** → `preview_start` + browser snapshot/console serbest. Backend zaten 61001'de çalışıyorsa preview tool'una sadece tarayıcı kontrolü için ihtiyaç var.

### Önemli
- Background process'leri `run_in_background: true` ile başlat, output dosyasını Read ile takip et.
- Build sonrası warning'ler (CS8602, CA1416 gibi) projedeki mevcut nullable warning'lerdir — fix etme. Yalnızca senin değişikliğinle gelen yeni hatayı düzelt.
- Port 61001'de senin başlatmadığın bir process varsa, durdurmadan önce kullanıcıya sor (uncommitted state olabilir).

## Diğer kurallar

- DB tasarımında kısa tablo/kolon isimleri, INT PK/FK kullan; SQL entegrasyonu önceliklidir.
- Veri giriş ekranlarında sol tab menüsü + sağ seçili sekme içeriği standardı (`st-modal-body--tabbed`).
- Sebep net tespit edildiyse plan dökümanı yazma — direkt Edit + kısa açıklama.

## Silme onay standardı

Tüm silme/destruktif işlemler **ekranın ortasında** custom modal ile onay alır — browser native `confirm()` / `alert()` **kullanma** (sayfanın üst kenarında çıkar, tema uyumsuz).

**Modal yapısı (zorunlu):**
- Tam ekran backdrop (yarı şeffaf koyu, blur)
- Ortalanmış card: danger renkli ikon (kırmızı çöp/uyarı) + başlık + açıklama mesajı
- İki buton: **Vazgeç** (ghost/secondary) + **Sil** (`--danger` / kırmızı)
- Esc ile veya backdrop tıklamasıyla iptal, Enter ile onay
- Ok butonu varsayılan focus'ta

**Referans uygulama:** `Views/PriceList/Report.cshtml` → `showConfirm({ title, message, okLabel })` Promise tabanlı helper + `.plr-modal-backdrop` CSS. Yeni ekranlarda aynı pattern'i tekrarla; promise zincirinde `.then(ok => { if (!ok) return; ... })`.

Silme/destruktif fiil olmayan sıradan bilgilendirme/uyarılar için inline mesaj veya toast yeterlidir; modal sadece **kullanıcı onayı gerektiren** akışlarda zorunludur.

## ID tabanlı eşleştirme kuralı

**Tüm karşılaştırma / dedup / match / FK ilişkileri ID üzerinden yapılır.** String alanları (kod, ad, açıklama) sadece kullanıcıya gösterim içindir; runtime karşılaştırmada **kullanılmaz**.

### Uygulama
- **FK kolonları**: Tüm tablolarda referans verilen kolon **`int` PK** olur. String code/name FK olarak kullanılmaz (örn. `MaterialCode` yerine `ItemId`).
- **DTO'lar**: Karşılaştırma gerektiren her DTO ilgili `*Id` alanını taşır (örn. `CombinationFeatureValueDto.FeatureValueId`). Display için `Code`/`Name` ek olarak yer alabilir, ama karar logiclerinde kullanılmaz.
- **Service match/dedup**: `OrderBy(id) → array eşitliği` desenle. String NormKey/lowercase trick'ini kullanma — Türkçe karakter, whitespace, case farkları her zaman bug üretir.
- **API response**: `value`/`name`/`code`'a ek olarak `valueId`/`id` döndür. Frontend client-side kontrol veya cache key'leri için ID kullanır.
- **Yeni tablo tasarımı**: PK her zaman `INT IDENTITY`. Doğal anahtar (kod) UNIQUE INDEX ile tutulur, ama referanslar PK'ya verilir.

### Geriye dönük temizlik
Eski string-based match (NormKey, lowercase concatenation, Trim) gördüğünde bu kurala göre ID-tabanlı yeniden yazmaya çalış — DTO/Repository'ye gerekiyorsa ID alanı ekle.

### İstisna
Standart kod alanları (`GuideMas.ValueColumn = Code`) kullanıcıya gösterim için kalır. Ama o kod alanının başka tabloya FK olarak gitmesi yanlış — yeni tabloda referans `int FK_Id` olur.

## Standart rehber kuralı

Tüm standart rehberler (cbv_Guide_* view'larından beslenen) için **her ekranda aynı davranış**:

- **ValueColumn = `Code`** — rehberden satır seçilince input'a yazılan değer.
- **DisplayColumn = `Name`** — input yanında görüntülenen etiket (read-only label, fillTargets target'ı, vs.).

Standart rehber view'ları zorunlu olarak `Id`, `Code`, `Name` kolonlarını içerir. Bu nedenle yeni bir rehber için ayrıca konfigürasyon yapılmaz — discovery (`SqlGuideRepository.DiscoverAndRegisterGuidesAsync`) ve normalize pass (`NormalizeStandardColumnsAsync`) her başlatmada bu kuralı GuideMas'a uygular.

### İhlaller
- Bir view'da `Code`/`Name` kolonları olmayabilir → fallback olarak ORDINAL_POSITION 1./2. kolon kullanılır. Yeni view tasarlanırken **bu duruma izin verilmemeli** — standart kolon adlarını koruyun.
- Frontend'de admin "Alan Ayarları" → `formatJson.valueColumn` / `displayColumn` ile lokal override yapılabilir; ancak GuideMas seviyesindeki standardı bozmayın. Override sadece özel durumlar içindir.
- "Özel alan rehberi" (admin UI'dan serbest tanımlanan) bu kural dışındadır; kendi value/display ayarlarına sahiptir.
