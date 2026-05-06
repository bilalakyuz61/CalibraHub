namespace CalibraHub.Domain.Entities;

/// <summary>
/// Sistem Ayarlari (Gate) sifresi — tek-row tablodaki tek admin sifresi.
/// Hash PBKDF2 formatinda; <see cref="Application.Security.PasswordHasher"/> kullanir.
/// </summary>
public sealed class GateCredentials
{
    public int       Id                { get; set; } = 1;
    public string    PasswordHash      { get; set; } = string.Empty;
    public DateTime  LastChangedAt     { get; set; }
    public string?   LastChangedFromIp { get; set; }
    public DateTime  CreatedAt         { get; set; }
}
