# Mimari Ozet

## Hedef

Kurumsal olcekte:

- Entegratorden dokuman cekme (worker)
- Calibra veri tabanina yazma (persistence)
- Web panelde admin yonetimi (entegrator, departman, kullanici, ust onaylayici)
- Web panelde onay kuyrugu yonetimi

## Katmanlar ve Bagimlilik Yonleri

`Domain <- Application <- (Persistence, Infrastructure) <- (Web, Worker)`

- Domain: Is modeli.
- Application: Is akislari ve interface sozlesmeleri.
- Persistence: Repository implementasyonlari.
- Infrastructure: Entegrator istemcisi implementasyonlari.
- Web/Worker: Uygulama giris noktasi ve orchestration.

## Mevcut Durum

- Persistence, baslangicta in-memory adaptordur.
- Infrastructure, baslangicta mock entegrator istemcisi kullanir.
- Web:
  - `Admin` ekraninda entegrator, departman, kullanici hiyerarsisi listelenir.
  - `Approval` ekraninda bekleyen belgeler listelenir.
- Worker, aktif entegratorlerden periyodik cekim yapar ve kuyruga ekler.

## Sonraki Kurumsal Adimlar

1. Persistence katmaninda in-memory yerine EF Core / SQL implementasyonu ile Calibra DB gecisi.
2. Integrator adaptoru icin gercek API istemcisi ve kimlik dogrulama.
3. Onay aksiyonlari (onay/red) ve denetim izi (audit trail).
4. Rol bazli yetkilendirme (RBAC) ve merkezi kimlik dogrulama.
5. Kuyruk/messaging tabanli hata toleransli import tasarimi.
