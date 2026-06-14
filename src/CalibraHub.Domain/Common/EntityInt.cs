namespace CalibraHub.Domain.Common;

/// <summary>
/// INT IDENTITY PK tabanli entity base.
/// Yeni tablolar bu base'i kullanir (PK = int Id).
/// Mevcut Entity (Guid Id) base'i legacy entity'ler icin korunur — refactor edildikce taşinir.
/// </summary>
public abstract class EntityInt
{
    public int Id { get; set; }
}
