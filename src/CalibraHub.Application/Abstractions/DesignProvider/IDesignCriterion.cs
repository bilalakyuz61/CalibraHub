using System.Data;

namespace CalibraHub.Application.Abstractions.DesignProvider;

/// <summary>
/// Tasarım seçim kriteri soyutlaması. Her kriter DocLayoutRule tablosunda
/// kendine ait NULLable kolona karşılık gelir. Yeni kriter eklemek için:
///   1) DocLayoutRule tablosuna NULLable kolon ekle (migration)
///   2) DesignSelectionContext'e nullable property ekle
///   3) Bu interface'i implement eden yeni sınıf yaz
///   4) DI'a IDesignCriterion olarak kaydet
/// SqlDocLayoutRuleRepository sorguyu DI'dan gelen kriter listesinden dinamik
/// üretir; bu yüzden Open/Closed prensibi korunur (sorgu/repo değişmez).
/// </summary>
public interface IDesignCriterion
{
    /// <summary>DocLayoutRule tablosundaki kolon adı (örn. "CustomerId").</summary>
    string ColumnName { get; }

    /// <summary>SQL parametre adı, '@' önekiyle (örn. "@CustomerId").</summary>
    string ParameterName { get; }

    /// <summary>
    /// Hiyerarşi ağırlığı; 2'nin kuvveti olmalıdır (1, 2, 4, 8, 16, ...).
    /// Daha yüksek = daha özel = öncelikli. Repository ORDER BY'da bu ağırlıkları
    /// toplayarak en iyi eşleşmeyi seçer; 2^n kuralı eşitlik üretmemesi içindir.
    /// </summary>
    int Weight { get; }

    /// <summary>SQL parametresinin tipi.</summary>
    SqlDbType SqlType { get; }

    /// <summary>
    /// Çalışma anında bağlamdan değeri okur. <c>null</c> dönerse o kriter
    /// match'lemede ignore edilir (rule'un o kolonunun NULL olmasına denk gelir).
    /// </summary>
    object? ExtractValue(DesignSelectionContext context);
}
