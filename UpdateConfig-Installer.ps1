param(
    [string]$JsonPath,
    [string]$ConnStr,
    [string]$Port
)
$ErrorActionPreference = "Stop"
try {
    # .NET appsettings.json dosyasını güvenle parse et
    $json = Get-Content -Path $JsonPath -Raw | ConvertFrom-Json
    
    # Yeni girdiği şifreler ile ConnectionString'i değiştir
    $json.CalibraDatabase.ConnectionString = $ConnStr
    
    # Kestrel (Web Sunucusu) Port Numarasını Güncelle
    if ($null -ne $json.Kestrel.Endpoints.Http) {
        $json.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:$Port"
    } else {
        # Eğer Kestrel tanımı eksikse sıfırdan oluşturur
        $json | Add-Member -Name "Kestrel" -MemberType NoteProperty -Value @{ Endpoints = @{ Http = @{ Url = "http://0.0.0.0:$Port" } } } -Force
    }
    
    # Dosyayı yeni ayarlarıyla Türkçe karakter şifrelemesiyle(UTF8) diske kaydet
    $json | ConvertTo-Json -Depth 10 | Set-Content -Path $JsonPath -Encoding UTF8
    
    Write-Host "Veritabanı bilgileri, Web Portu ($Port) ve lisans ayarları başarıyla işlendi."
} catch {
    Write-Error "Ayar dosyasi güncellenemedi: $_"
    exit 1
}
