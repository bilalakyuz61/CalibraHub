# CalibraHub

Bu cozum, entegratorden gelen e-Fatura, e-Irsaliye ve e-Arsiv belgelerini alarak veri tabanina kaydetmek ve web arayuzu uzerinden onay surecine dahil etmek icin katmanli bir kurumsal iskelet sunar.

## Katmanlar

- `src/CalibraHub.Domain`: Is kurallari ve cekirdek varliklar.
- `src/CalibraHub.Application`: Use-case servisleri ve uygulama sozlesmeleri.
- `src/CalibraHub.Persistence`: Kalici veri adaptoru (baslangicta in-memory, sonrasi Calibra DB).
- `src/CalibraHub.Infrastructure`: Entegrator istemcisi/adaptorleri.
- `src/CalibraHub.Web`: Admin paneli ve onay arayuzu.
- `src/CalibraHub.Worker`: Arka plan dokuman cekme ve sisteme alma servisi.

## Baslatma

```powershell
dotnet build CalibraHub.sln
dotnet run --project src/CalibraHub.Web
dotnet run --project src/CalibraHub.Worker
```

## Dokumanlar

- Mimari ozet: `docs/architecture-overview.md`
- Entegrator uyarlama klasoru: `docs/integrator-adaptation/`

