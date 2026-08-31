# Öğrendiklerim — Sessiz Kırık Hata Kataloğu

Bu dosya, gerçek hata avlarından çıkan **taşınabilir dersleri** toplar. Hepsinin ortak
özelliği şu: **derleme temiz, kod doğru görünüyor, test yok ya da geçiyor — ama davranış
sessizce yanlış.** Derleyici bunları yakalamaz; ancak izini süren biri bulur.

Her madde şu şekilde: **Belirti → Kök neden → Kural.** Örnekler CalibraHub'dan, kurallar
proje bağımsızdır.

---

## 1. İki uçlu sözleşmeler: "kaydediliyor ama geri okunmuyor"

**Belirti:** Kaydetme "başarılı" diyor. Geri okuyunca kayıt sayısı doğru, **içindeki tüm
alanlar boş**.

**Kök neden:** Yazma tarafı alan adlarını `camelCase` üretiyordu, okuma tarafı
büyük/küçük harf **duyarlı** eşlemeyle `PascalCase` bekliyordu. İki taraf da kendi içinde
doğru; kimse hatalı değil, sözleşme ortada kırık.

**Kural:**
- Serileştirme ayarları **tek bir yerde** tanımlanır; yazma ve okuma **aynı** ayarı kullanır.
- Kalıcılaştırdığın her yapı için **round-trip testi** yap: yaz → oku → alanları karşılaştır.
  "200 döndü" kanıt değildir.
- İki ajan/iki geliştirici bir sözleşmenin iki ucunu ayrı yazıyorsa, entegrasyonu
  **çalıştırarak** doğrula; iki taraf da "bende çalışıyor" der.

---

## 2. Aynı adı taşıyan birden fazla kaynak: "dinliyorum ama hiçbir şey gelmiyor"

**Belirti:** Olay dinleyicisi kuruldu, "çalışıyor" görünüyor, ama hiç olay yakalanmıyor.

**Kök neden:** Altyapı, **aynı adla birden fazla** yayıncı örneği oluşturuyordu. Kod
keşfettiği son örneği saklayıp yalnız ona abone oluyordu; trafik diğerlerinden akıyordu.

**Kural:**
- Ada göre keşif yapan aboneliklerde **"tek örnek" varsayma**. Hepsini topla, hepsine
  abone ol, sökerken hepsini sök.
- "Kuruldu mu?" ile "yakalıyor mu?" ayrı sorulardır. Bayrağa değil, **gerçek veriye** bak.

---

## 3. Görünürlük kontrolü iframe sınırını geçmez

**Belirti:** Kullanıcı hiçbir şey yapmadığı halde arka planda saniyede bir istek; sayfa
sayfa on binlerce kayıt çekiliyor.

**Kök neden:** Sonsuz kaydırma "içerik ekranı doldurmadıysa bir sayfa daha" mantığındaydı
ve gizli sekmede durmak için elemanın `offsetParent`'ına bakıyordu. Sekmeler **iframe**
idi ve gizlenen şey iframe elemanının kendisiydi; **iframe'in içindeki belge bunu bilmez**,
kendi elemanlarını görünür sayar. Kontrol sessizce hep "görünür" dedi.

**Kural:**
- Gömülü bir belgede görünürlük ölçerken **kendi `frameElement`ine** bak, yalnız kendi
  DOM'una değil.
- Kendi kendini tetikleyen döngülere (auto-fill, auto-retry, polling) mutlaka bir
  **durma koşulu ve üst sınır** koy.
- Arka planda çalışan iş, kullanıcı görmediği için **sonsuza kadar fark edilmez**.

---

## 4. Yalnız arayüzde uygulanan kural = uygulanmamış kural

**Belirti:** Ayar kapalı ama işlem yine de yapılabiliyor.

**Kök neden:** Kısıt yalnız arayüzde uygulanmıştı: buton gizlendi, menü ögesi kaldırıldı.
Ama aynı işleve **derin bağlantı**, **eski açık sekme** ya da elle istekle ulaşılabiliyordu.
Bir vakada arayüz kısıtlandı, liste ekranındaki kısayol unutuldu ve ayar fiilen delik kaldı.

**Kural:**
- Her kapıyı **sunucuda** kur. Arayüzdeki gizleme yalnız kolaylıktır, koruma değildir.
- Bir yeteneği kısıtlarken **tüm giriş noktalarını** say: buton, menü, satır menüsü,
  derin bağlantı, klavye kısayolu, API ucu.
- Reddederken kullanıcıya anlaşılır sebep, sunucuya gerçek ayrıntı.

---

## 5. Şema kurulum sırası ve yanıltıcı guard

**Belirti:** Sıfırdan kurulum "nesne bulunamadı" ile çöküyor. Mevcut kurulumlarda sorun yok.

**Kök neden:** Kolon ekleme bloğu, tablonun oluşturulmasından **önce** duruyordu. Guard
olarak kullanılan "kolon var mı" kontrolü, **tablo hiç yokken de** "yok" cevabı veriyordu;
guard geçiyor, işlem patlıyordu. Üstelik o kolonlar oluşturma listesinde de yoktu — kurulum
çökmese bile şema eksik kalacaktı.

**Kural:**
- "Kolon var mı" kontrolü **tablo varlığını kanıtlamaz**. Nesne varlığını ayrıca kontrol et
  ya da bloğu oluşturmadan **sonraya** taşı.
- Yeni kolon iki yere birden eklenir: **oluşturma listesine** (yeni kurulumlar) ve
  **idempotent değiştirme bloğuna** (mevcut kurulumlar).
- Kurulum kodunu yalnız okuyarak doğrulama: **boş bir veritabanında gerçekten çalıştır**,
  sonra **ikinci kez** çalıştır (idempotentlik).
- Sıra hatası tek vaka değildir, **sınıftır**: bulduğunda dosyanın tamamını aynı desen için
  tara.

---

## 6. Bırakılmış kilit: bir daha asla çalışmayan iş

**Belirti:** Zamanlanmış görev sessizce hiç çalışmıyor. Hata yok, log yok.

**Kök neden:** Süreç iş ortasında kapandığında "çalışıyor" bayrağı açık kaldı. Kilit
alınamadığı için görev bir daha hiç başlamadı — ve kimse fark etmedi.

**Kural:**
- Kilit/bayrak alan her mekanizmaya **süpürücü** yaz: eşik üstü asılı kalanları serbest bırak,
  ne yaptığını **logla**.
- Süreç ölümüne dayanıklılık, `finally` ile bitmez; yeniden başlangıçta **temizlik** gerekir.
- "Hiç çalışmıyor" en zor fark edilen arızadır: gürültü üretmez.

---

## 7. Yazılmayan kayıt: hep boş görünen ekran

**Belirti:** Geçmiş/çalışma kaydı ekranı hep boş. Kullanıcı "ekran açılmıyor" diye bildiriyor.

**Kök neden:** İşi yapan bileşenler yalnız "son çalışma" özet alanlarını güncelliyor,
geçmiş tablosuna **hiç satır yazmıyordu**. Ekran doğru çalışıyordu; besleyen taraf yoktu.

**Kural:**
- Bir ekran hep boşsa önce **veri yazılıyor mu** diye bak; okuma tarafını suçlamadan önce
  yazma tarafını kanıtla.
- Boş durum mesajı **ayırt edici** olsun: "hiç kayıt yok" ile "kayıt tutulmuyor" farklı
  şeylerdir; kullanıcı ikisini ayırt edemezse hatayı yanlış yere bildirir.

---

## 8. Aynı özellik iki görünümde: biri unutuluyor

**Belirti:** Liste kart görünümünde silme onay soruyor, tablo görünümünde **sormadan siliyor**.
Aynı ekranda "geçmiş" butonu bir görünümde çalışıyor, diğerinde hiçbir şey yapmıyor.

**Kök neden:** Aksiyon sözleşmesi (onay metni, aksiyon tipi) yapılandırmada tanımlıydı ama
yalnız **bir** görünüm bileşeni onu işliyordu. Diğer bileşen bilmediği alanı **sessizce yok
sayıyordu**.

**Kural:**
- Aynı yapılandırmayı tüketen **her** bileşen, sözleşmenin **tamamını** desteklemeli;
  desteklemiyorsa sessizce yutmak yerine **görünür şekilde** hata vermeli.
- Yeni bir alan eklerken "bunu kim okuyor?" diye ara; tek tüketici varsayma.
- Tanımadığı aksiyona sessizce navigasyona düşen kod, en sinsi hata kaynağıdır.

---

## 9. Numara eşlemesi: ekran ile kod ayrışması

**Belirti:** Yok — henüz. Uyuyan tuzak.

**Kök neden:** Ekran `1 = Hammadde, 3 = Mamul` yazıyordu; koddaki tanım `1 = Mamul,
3 = Hammadde` idi. Üretim planlaması "üretilebilir mi" kararını koddan veriyordu. Alan
yeni olduğu için canlıda az kayıt vardı; kullanılmaya başlanınca **sessizce ters** davranacaktı.

**Kural:**
- Kullanıcının seçtiği sabit değerler ile koddaki karşılıkları **tek kaynaktan** gelmeli;
  ikisi elle yazılıyorsa er ya da geç ayrışır.
- Ayrışma tespit edilince **veriyi ve kullanıcının gördüğünü** esas al, kodu ona uydur.
- "Şu an zararsız" ≠ "sorun değil". Az veri, hatayı gizler.

---

## 10. Önizleme yan etkisi

**Belirti:** "Canlı önizleme" düğmesi her basıldığında gerçek sayaç ilerliyor; numaralarda
açıklanamayan boşluklar oluşuyor.

**Kök neden:** Önizleme, üretim kodunun aynısını çağırıyordu ve o kod sayacı artırıyordu.
Kod içindeki yorum "sayaç artmaz" diyordu; **yorum yanlıştı**, kod doğruydu.

**Kural:**
- Önizleme **yan etkisiz** olmalı. Değilse adı önizleme olmamalı ve kullanıcıya ne
  tükettiği açıkça yazılmalı.
- Yoruma güvenme, **koda bak**. Yanlış yorum, yanlış koddan daha tehlikelidir: doğrulamayı
  durdurur.

---

## 11. Çıkarım ≠ ilişki

**Belirti:** Ad benzerliğinden çıkarılan ilişkilerin gerçek kısıta çevrilmesi teklif edildi;
veri kontrolünde "öksüz kayıt yok" çıktı.

**Kök neden:** Aday alanlardan biri aslında **başka bir listeye** işaret ediyordu; öksüz
çıkmaması **tesadüftü** (tek kayıt her iki tabloda da aynı numaraya denk geliyordu). Kısıt
eklenseydi yanlış anlam kalıcılaşacaktı.

**Kural:**
- "Veri uyuyor" **anlam doğru** demek değildir. Az veride tesadüf sık görülür.
- Kısıt eklemeden önce alanın **kod içindeki kullanımını** oku; ad benzerliği kanıt değildir.
- Kısıtları geriye dönük eklerken: öksüz varsa **kurma, sessizce atlama, gerekçesini logla**.
  Açılışta çöken bir kurulum, eksik kısıttan kötüdür.

---

## 12. Hata mesajını yutan tip uyuşmazlığı

**Belirti:** Kullanıcı hata yerine `[object Object]` görüyor.

**Kök neden:** Sunucu hata alanını **nesne** olarak döndürüyordu, arayüz **metin** bekliyordu.
Özenle yazılmış anlaşılır mesaj, ekrana hiç ulaşmıyordu.

**Kural:**
- Hata gösteren kod, hem metin hem nesne biçimini tolere etsin.
- Kullanıcıya gösterilen mesajı **gerçekten göstererek** test et; "mesaj döndürdüm" yetmez.

---

## 13. Test ortamı gerçekten izole mi?

**Belirti:** Yok — yakalanmasaydı canlı veriye yazacaktı.

**Kök neden:** Fonksiyon testleri yalnız "test şirketi" adında çalışsın diye korunuyordu.
Ama test şirketi **yeni veritabanı seçilmeden** kurulduğunda canlı veritabanının bağlantısını
devralıyordu. Ad kontrolü geçiyor, testler canlı veriye yazacaktı.

**Kural:**
- İzolasyonu **ada göre** değil, **gerçek hedefe** göre doğrula (hangi veritabanı/şema?).
- Emin olamadığın durumda **çalıştırma** (fail-closed). Test verisi üreten bir mekanizmada
  varsayılan "izin ver" olmamalı.

---

## 14. Ulaşılamayan özellik

**Belirti:** Özellik tamamlandı, ama kullanıcı ona hiçbir yoldan ulaşamıyor.

**Kök neden:** Test ekranı yalnız test oturumunda çalışıyordu; o oturuma geçmenin bir yolu
yoktu (üretilen parola hiçbir yerde saklanmıyordu).

**Kural:**
- "Bitti" demeden önce **kullanıcının o özelliğe nasıl ulaşacağını** baştan sona yürü.
- Menü girişi, yetki, oturum bağlamı: biri eksikse özellik yok sayılır.

---

## 15. Doğrulama disiplini

Bu avlarda işe yarayan alışkanlıklar:

- **HTTP seviyesinde uçtan uca dene.** "Derleme temiz" ile "çalışıyor" arasında uçurum var.
- **Round-trip iste:** yaz → oku → karşılaştır. Tek yönlü doğrulama, 1. maddedeki hatayı kaçırır.
- **Negatif senaryoyu da dene:** kapalı ayarla reddediliyor mu, öksüz veriyle atlıyor mu,
  yetkisiz erişimde ne oluyor.
- **Ölçüm hatana açık ol.** Bir kontrol "hiç yok" diyorsa önce ölçüm biçimini sorgula;
  yanlış parametreyle çekilen sayfa, olmayan hata icat ettirir.
- **Yorumları kanıt sayma.** Kod ne yapıyorsa o doğrudur.
- **Bir hata bulduğunda sınıfını ara.** Tek vaka nadiren tektir.

---

## 16. Karar geçmişini koru

**Belirti:** Kullanıcı, üç gün önce kendi istediği davranışın tersini istedi.

**Kök neden:** Önceki kararın **gerekçesi** koda yazılmıştı ("tek tıkla yanlışlıkla ekran
değiştirmek iş kaybına yol açıyordu"). Gerekçe olmasaydı sessizce geri alınacak ve eski
sorun geri dönecekti.

**Kural:**
- Alışılmadık her karara **neden** yaz. "Ne" kodda zaten var; kaybolan "neden"dir.
- Bir kararı geri almadan önce gerekçesini oku; hâlâ geçerliyse **sor**, sessizce çevirme.
