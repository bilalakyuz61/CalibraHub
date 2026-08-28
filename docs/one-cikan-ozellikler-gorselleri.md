# Öne Çıkan Özellikler — ekran görüntüsü rehberi

Sayfa: **Ayarlar → Öne Çıkan Özellikler** (`/Admin/Features`)

Görseller bu klasörden okunur. Dosya varsa sayfa otomatik gösterir, yoksa kart
görselsiz çıkar (hata yok). Uzantı `.png`, `.jpg`, `.jpeg` veya `.webp` olabilir.
Sayfadaki **Eksik görselleri göster** anahtarı, henüz konmamış görsellerin
beklenen dosya adını kartın üstünde gösterir.

## Öncelik: 12 modül kapağı

Kapak görselleri geniş basılır (PDF ve video için asıl kullanılacak olanlar).
Önerilen çekim: tarayıcı 1600×900, koyu veya açık tema (hepsinde aynı tema),
ekranda gerçekçi veri olsun, kişisel/müşteri adı içeren alanlar tercihen maskeli.

| # | Dosya | Önerilen kaynak ekran |
|---|-------|----------------------|
| 1 | `satis-ve-siparis-yonetimi/_hero.png` | `/Sales/Orders` — satış siparişleri listesi |
| 2 | `satin-alma-ve-ihtiyac-karsilama/_hero.png` | `/Purchase/FulfillmentCenter` — ihtiyaç karşılama merkezi |
| 3 | `stok-ve-depo/_hero.png` | `/Logistics/MaterialCards` veya `/Warehouse/StockIn` |
| 4 | `uretim-planlama-ve-saha/_hero.png` | `/Production/MachineSchedule` — Gantt çizelgesi |
| 5 | `kalite-yonetimi/_hero.png` | `/Quality/DofDashboard` — DÖF panosu |
| 6 | `ar-ge-ur-ge/_hero.png` | `/Arge/Projects` — proje komuta güvertesi |
| 7 | `onay-akislari-ve-elektronik-belge/_hero.png` | `/ApprovalFlow` — onay akışı tasarımcısı |
| 8 | `raporlama-ve-analitik/_hero.png` | `/Dashboard/Boards` — rapor panosu |
| 9 | `kod-yazmadan-uyarlama/_hero.png` | `/Admin/ViewSettings` — alan rehberi / alan yönetimi |
| 10 | `entegrasyon-ve-veri-aktarimi/_hero.png` | `/Integrations` — entegrasyon listesi/sihirbazı |
| 11 | `guvenlik-ve-yonetisim/_hero.png` | `/Admin/Permissions` — yetki matrisi |
| 12 | `kullanim-deneyimi-ve-mobil/_hero.png` | `/` — ana sayfa panosu (sekmeli kabuk görünsün) |

Klasör adları yukarıdaki gibi olmalı; sayfa, modül başlığından türetilen bu
adlarla arar.

## İsteğe bağlı: özellik başına görsel

Kart başına da görsel konabilir — kartta 16:9 kırpılır. Dosya adı
`{modul-klasoru}/{ozellik-adi}.png` biçimindedir; tam liste bu dosyanın sonundaki
kontrol listesindedir. Kapaklar olmadan da çalışır, ikisi
birbirinden bağımsızdır.

## PDF / video kullanımı

- Sayfadaki **Yazdır / PDF** butonu: araç çubuğu ve eksik görsel yer tutucuları
  çıktıya girmez, kartlar sayfa arasında bölünmez, kapak görselleri PDF'e basılır.
- Görsele tıklamak tam ekran büyütür (Esc kapatır) — ekran kaydı alırken
  modül modül gezinmek için kullanışlıdır.

---

## Ek: özellik başına tam kontrol listesi

## Satış ve Sipariş Yönetimi
- [ ] `satis-ve-siparis-yonetimi/_hero.png` — modül kapak görseli
- [ ] `satis-ve-siparis-yonetimi/uctan-uca-satis-akisi.png` — Uçtan Uca Satış Akışı
- [ ] `satis-ve-siparis-yonetimi/kismi-teslimat.png` — Kısmi Teslimat
- [ ] `satis-ve-siparis-yonetimi/fiyat-listesi-ve-cariye-ozel-fiyat.png` — Fiyat Listesi ve Cariye Özel Fiyat
- [ ] `satis-ve-siparis-yonetimi/kit-paket-urun.png` — Kit / Paket Ürün
- [ ] `satis-ve-siparis-yonetimi/siparis-bazinda-seri-takibi.png` — Sipariş Bazında Seri Takibi
- [ ] `satis-ve-siparis-yonetimi/siparis-serisi-rezervasyonu.png` — Sipariş Serisi Rezervasyonu
- [ ] `satis-ve-siparis-yonetimi/stok-rezervasyonu.png` — Stok Rezervasyonu
- [ ] `satis-ve-siparis-yonetimi/yukleme-planlama-merkezi.png` — Yükleme Planlama Merkezi
- [ ] `satis-ve-siparis-yonetimi/ozellik-ve-kombinasyon.png` — Özellik ve Kombinasyon

## Satın Alma ve İhtiyaç Karşılama
- [ ] `satin-alma-ve-ihtiyac-karsilama/_hero.png` — modül kapak görseli
- [ ] `satin-alma-ve-ihtiyac-karsilama/tam-tedarik-zinciri.png` — Tam Tedarik Zinciri
- [ ] `satin-alma-ve-ihtiyac-karsilama/ihtiyac-karsilama-merkezi.png` — İhtiyaç Karşılama Merkezi
- [ ] `satin-alma-ve-ihtiyac-karsilama/karsilama-defteri.png` — Karşılama Defteri
- [ ] `satin-alma-ve-ihtiyac-karsilama/malzeme-belge-kilitleri.png` — Malzeme Belge Kilitleri

## Stok ve Depo
- [ ] `stok-ve-depo/_hero.png` — modül kapak görseli
- [ ] `stok-ve-depo/ambar-giris-cikis-transfer-sayim.png` — Ambar Giriş / Çıkış / Transfer / Sayım
- [ ] `stok-ve-depo/lot-ve-seri-takibi.png` — Lot ve Seri Takibi
- [ ] `stok-ve-depo/baz-birim-normalizasyonu.png` — Baz Birim Normalizasyonu
- [ ] `stok-ve-depo/eksi-bakiye-kontrolu.png` — Eksi Bakiye Kontrolü
- [ ] `stok-ve-depo/lokasyon-yonetimi.png` — Lokasyon Yönetimi
- [ ] `stok-ve-depo/stok-hareketleri-dokumu.png` — Stok Hareketleri Dökümü

## Üretim Planlama ve Saha
- [ ] `uretim-planlama-ve-saha/_hero.png` — modül kapak görseli
- [ ] `uretim-planlama-ve-saha/urun-agaci-recete.png` — Ürün Ağacı (Reçete)
- [ ] `uretim-planlama-ve-saha/is-emirleri.png` — İş Emirleri
- [ ] `uretim-planlama-ve-saha/uretim-terminali.png` — Üretim Terminali
- [ ] `uretim-planlama-ve-saha/makine-planlama-gantt.png` — Makine Planlama (Gantt)
- [ ] `uretim-planlama-ve-saha/vardiya-senaryolari-ve-takvim.png` — Vardiya Senaryoları ve Takvim
- [ ] `uretim-planlama-ve-saha/kapasite-yuk-raporu.png` — Kapasite / Yük Raporu

## Kalite Yönetimi
- [ ] `kalite-yonetimi/_hero.png` — modül kapak görseli
- [ ] `kalite-yonetimi/muayene-planlari.png` — Muayene Planları
- [ ] `kalite-yonetimi/muayene-kayitlari.png` — Muayene Kayıtları
- [ ] `kalite-yonetimi/hata-kodlari.png` — Hata Kodları
- [ ] `kalite-yonetimi/dof-duzeltici-onleyici-faaliyet.png` — DÖF (Düzeltici / Önleyici Faaliyet)
- [ ] `kalite-yonetimi/dof-panosu.png` — DÖF Panosu

## AR-GE / ÜR-GE
- [ ] `ar-ge-ur-ge/_hero.png` — modül kapak görseli
- [ ] `ar-ge-ur-ge/proje-komuta-guvertesi.png` — Proje Komuta Güvertesi
- [ ] `ar-ge-ur-ge/gorev-sablonlari-ve-wbs.png` — Görev Şablonları ve WBS
- [ ] `ar-ge-ur-ge/proje-maliyeti.png` — Proje Maliyeti

## Onay Akışları ve Elektronik Belge
- [ ] `onay-akislari-ve-elektronik-belge/_hero.png` — modül kapak görseli
- [ ] `onay-akislari-ve-elektronik-belge/gorsel-onay-akisi-tasarimcisi.png` — Görsel Onay Akışı Tasarımcısı
- [ ] `onay-akislari-ve-elektronik-belge/onayda-bekleyenler.png` — Onayda Bekleyenler
- [ ] `onay-akislari-ve-elektronik-belge/durum-kilidi.png` — Durum Kilidi
- [ ] `onay-akislari-ve-elektronik-belge/akisi-ai-ile-olustur.png` — Akışı AI ile Oluştur
- [ ] `onay-akislari-ve-elektronik-belge/tek-tik-onay-baglantilari.png` — Tek Tık Onay Bağlantıları
- [ ] `onay-akislari-ve-elektronik-belge/e-fatura-e-arsiv-e-irsaliye.png` — e-Fatura / e-Arşiv / e-İrsaliye
- [ ] `onay-akislari-ve-elektronik-belge/belge-tasarimcisi.png` — Belge Tasarımcısı

## Raporlama ve Analitik
- [ ] `raporlama-ve-analitik/_hero.png` — modül kapak görseli
- [ ] `raporlama-ve-analitik/rapor-tasarimcisi.png` — Rapor Tasarımcısı
- [ ] `raporlama-ve-analitik/rapor-panolari.png` — Rapor Panoları
- [ ] `raporlama-ve-analitik/anlik-goruntu-canli-veri.png` — Anlık Görüntü / Canlı Veri
- [ ] `raporlama-ve-analitik/ana-sayfa-panosu.png` — Ana Sayfa Panosu
- [ ] `raporlama-ve-analitik/her-listede-excel.png` — Her Listede Excel

## Kod Yazmadan Uyarlama
- [ ] `kod-yazmadan-uyarlama/_hero.png` — modül kapak görseli
- [ ] `kod-yazmadan-uyarlama/dinamik-alan-tanimlama.png` — Dinamik Alan Tanımlama
- [ ] `kod-yazmadan-uyarlama/form-davranis-katmani.png` — Form Davranış Katmanı
- [ ] `kod-yazmadan-uyarlama/kart-duzeni-editoru.png` — Kart Düzeni Editörü
- [ ] `kod-yazmadan-uyarlama/liste-kisisellestirme.png` — Liste Kişiselleştirme
- [ ] `kod-yazmadan-uyarlama/kod-ve-numaralandirma-kurallari.png` — Kod ve Numaralandırma Kuralları
- [ ] `kod-yazmadan-uyarlama/ondalik-hassasiyeti.png` — Ondalık Hassasiyeti
- [ ] `kod-yazmadan-uyarlama/sql-gorunum-ve-hesaplanan-kolon-yonetimi.png` — SQL Görünüm ve Hesaplanan Kolon Yönetimi

## Yapay Zeka Asistanı
- [ ] `yapay-zeka-asistani/_hero.png` — modül kapak görseli
- [ ] `yapay-zeka-asistani/kendi-saglayiciniz.png` — Kendi Sağlayıcınız
- [ ] `yapay-zeka-asistani/her-ekranda-calibo.png` — Her Ekranda Calibo
- [ ] `yapay-zeka-asistani/verinizde-arama.png` — Verinizde Arama
- [ ] `yapay-zeka-asistani/onayli-kayit-olusturma.png` — Onaylı Kayıt Oluşturma
- [ ] `yapay-zeka-asistani/dosya-okuma.png` — Dosya Okuma
- [ ] `yapay-zeka-asistani/onay-akisi-uretimi.png` — Onay Akışı Üretimi

## Entegrasyon ve Veri Aktarımı
- [ ] `entegrasyon-ve-veri-aktarimi/_hero.png` — modül kapak görseli
- [ ] `entegrasyon-ve-veri-aktarimi/dosyadan-ice-aktarim.png` — Dosyadan İçe Aktarım
- [ ] `entegrasyon-ve-veri-aktarimi/veritabanindan-ice-aktarim.png` — Veritabanından İçe Aktarım
- [ ] `entegrasyon-ve-veri-aktarimi/rest-entegrasyon-sihirbazi.png` — REST Entegrasyon Sihirbazı
- [ ] `entegrasyon-ve-veri-aktarimi/whatsapp-gelen-kutusu.png` — WhatsApp Gelen Kutusu
- [ ] `entegrasyon-ve-veri-aktarimi/toplu-mail.png` — Toplu Mail

## Güvenlik ve Yönetişim
- [ ] `guvenlik-ve-yonetisim/_hero.png` — modül kapak görseli
- [ ] `guvenlik-ve-yonetisim/form-ve-islem-bazli-yetki.png` — Form ve İşlem Bazlı Yetki
- [ ] `guvenlik-ve-yonetisim/veri-perdeleme-kurallari.png` — Veri Perdeleme Kuralları
- [ ] `guvenlik-ve-yonetisim/islem-loglari.png` — İşlem Logları
- [ ] `guvenlik-ve-yonetisim/hata-loglari-ve-saglik-kontrolu.png` — Hata Logları ve Sağlık Kontrolü
- [ ] `guvenlik-ve-yonetisim/eszamanli-duzenleme-korumasi.png` — Eşzamanlı Düzenleme Koruması
- [ ] `guvenlik-ve-yonetisim/kayit-butunlugu-kilitleri.png` — Kayıt Bütünlüğü Kilitleri
- [ ] `guvenlik-ve-yonetisim/oturum-zaman-asimi.png` — Oturum Zaman Aşımı
- [ ] `guvenlik-ve-yonetisim/cok-sirketli-yapi.png` — Çok Şirketli Yapı

## Kullanım Deneyimi ve Mobil
- [ ] `kullanim-deneyimi-ve-mobil/_hero.png` — modül kapak görseli
- [ ] `kullanim-deneyimi-ve-mobil/sekmeli-calisma-alani.png` — Sekmeli Çalışma Alanı
- [ ] `kullanim-deneyimi-ve-mobil/kisayol-cubugu.png` — Kısayol Çubuğu
- [ ] `kullanim-deneyimi-ve-mobil/acik-koyu-tema.png` — Açık / Koyu Tema
- [ ] `kullanim-deneyimi-ve-mobil/f1-ile-sayfa-yardimi.png` — F1 ile Sayfa Yardımı
- [ ] `kullanim-deneyimi-ve-mobil/sayfa-ici-geri-bildirim.png` — Sayfa İçi Geri Bildirim
- [ ] `kullanim-deneyimi-ve-mobil/turkce-ingilizce-arayuz.png` — Türkçe / İngilizce Arayüz
- [ ] `kullanim-deneyimi-ve-mobil/takvim-ve-notlar.png` — Takvim ve Notlar
- [ ] `kullanim-deneyimi-ve-mobil/bildirim-merkezi.png` — Bildirim Merkezi
- [ ] `kullanim-deneyimi-ve-mobil/mobil-depo-ve-uretim.png` — Mobil Depo ve Üretim

Toplam: 80 özellik görseli + 13 modül kapağı.
