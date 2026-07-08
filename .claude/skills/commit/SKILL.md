---
name: commit
description: |
  Mevcut çalışma ağacındaki değişiklikleri commit eder, projeyi build eder ve
  sunucuyu yeniden başlatır. Kullanıcı "commit", "commit at", "kaydet ve
  başlat", "build ve re-run" gibi ifadeler kullandığında tetikle.
---

# commit

Aşağıdaki adımları sırayla uygula:

## 1. Değişiklikleri staged yap

```powershell
git -C "D:\JetBrainsRider\Projeler\CalibraHub" add src/
```

Untracked temp dosyaları (run_out.txt, run_err.txt, *.log vb.) staging'e ekleme.

## 2. Commit mesajı yaz

`git diff --cached --stat` ve `git diff --cached` çıktısını oku. Değişikliklerin
kapsamına göre anlamlı, Türkçe/İngilizce karma bir commit mesajı yaz:

- **prefix**: `feat`, `fix`, `style`, `refactor`, `chore` vb.
- **scope**: etkilenen modül/dosya kümesi (örn. `security`, `material-card`, `approval-flow`)
- **body** (gerekirse): önemli değişiklikleri madde madde listele

```powershell
git -C "D:\JetBrainsRider\Projeler\CalibraHub" commit -m @'
<mesaj buraya>

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

## 3. Port 61001'i temizle

```powershell
Get-NetTCPConnection -LocalPort 61001 -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty OwningProcess | Select-Object -Unique |
  ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
```

## 4. Sunucuyu yeniden başlat (background)

```powershell
$log = "C:\Users\bilal\AppData\Local\Temp\claude\calibrahub-run.log"
Start-Process -FilePath "dotnet" `
  -ArgumentList "run","--project","D:\JetBrainsRider\Projeler\CalibraHub\src\CalibraHub.Web\CalibraHub.Web.csproj","--launch-profile","http" `
  -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow
```

`run_in_background: true` ile başlat.

## 5. Smoke test

~12 saniye bekle, ardından:

```powershell
Invoke-WebRequest -Uri "http://localhost:61001/" -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue | Select-Object StatusCode
```

302 veya 200 görünce hazır.

## Notlar

- CLAUDE.md kuralı: port 61001'de çalışan her process sormadan durdurulabilir.
- `--launch-profile http` zorunlu; eksik olursa Production moduna düşer → static 404.
- JSX/CSS değişikliği varsa önce `npm run build` çalıştır
  (`D:\JetBrainsRider\Projeler\CalibraHub\src\CalibraHub.Web\ClientApp\`).
