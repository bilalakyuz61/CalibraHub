namespace CalibraHub.Application.Contracts;

/// <summary>
/// Döngüsel reçete — hangi malzemelerin döngüyü oluşturduğunu TAŞIR.
///
/// <para><b>Neden ayrı bir tip:</b> alan katmanındaki genel mesaj ("bu bileşenlerden biri
/// eninde sonunda mamulün kendisine bağlı") kullanıcıya ne yapacağını söylemiyordu —
/// hangi bileşen olduğunu bilmeden düzeltilemez. Bu tip zinciri metin olarak
/// (<see cref="Message"/>) ve makine tarafından işlenebilir biçimde
/// (<see cref="ItemIds"/>) birlikte döndürür; ekran ilgili düğümleri işaretleyebilsin.</para>
///
/// <para><see cref="ArgumentException"/>'dan türer: mevcut controller'lar zaten onu
/// yakalayıp 400 dönüyor, dolayısıyla bu tip eklenince hiçbir yol bozulmaz.</para>
/// </summary>
public sealed class BomCycleException : ArgumentException
{
    /// <summary>Döngüyü oluşturan malzeme kimlikleri, zincir sırasında.</summary>
    public IReadOnlyList<int> ItemIds { get; }

    public BomCycleException(string message, IReadOnlyList<int> itemIds)
        : base(message)
        => ItemIds = itemIds;
}
