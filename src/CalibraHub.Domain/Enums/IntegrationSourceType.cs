namespace CalibraHub.Domain.Enums;

/// <summary>
/// MappingRule'un hedef alan icin kaynak deger sagladigi 4 yontem.
/// DB'de NVARCHAR(20).
/// </summary>
public enum IntegrationSourceType
{
    /// <summary>Kaynak formdan bir alan ismi. SourceValue = field code.</summary>
    FormField = 0,

    /// <summary>Sabit literal deger. SourceValue = direkt yazilan deger.</summary>
    Constant = 1,

    /// <summary>NCalc expression. SourceValue = formul string'i (orn. "Adet * BirimFiyat").</summary>
    Formula = 2,

    /// <summary>Standart rehber lookup. SourceValue = guide code (orn. "COUNTRIES"),
    /// LookupSourceField = lookup'a verilecek form alani.</summary>
    Lookup = 3,

    /// <summary>
    /// Standart lookup fonksiyonu — pre-defined entity registry uzerinden:
    /// Stok/Cari/Depo/Olcu Birimi gibi sabit tablo eslemeleri.
    ///   SourceValue           = function id (orn. "ITEMS", "CONTACTS", "LOCATIONS")
    ///   LookupSourceField     = anahtar deger okunacak form alani (orn. "ItemId")
    ///   LookupReturnColumn    = donulecek kolon (orn. "Code", "Name")
    /// Backend registry hangi view'i ve hangi anahtar kolonu kullanacagini bilir.
    /// Rehber tipinden farkli olarak: kullanici view adi ezberlemez, kolon listesi
    /// onceden bilinen sabit metadata'dan gelir (UI net dropdown).
    /// </summary>
    Function = 4,
}
