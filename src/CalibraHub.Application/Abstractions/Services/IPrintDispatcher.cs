using CalibraHub.Application.Abstractions.DesignProvider;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Belge yazdirma dispatcher'i — Dinamik Tasarim Secim Motoru (DocLayoutRule + DocLayout)
/// uzerinden Belge Tasarimcisi (DocDesigner) ile PDF uretir.
///
/// Akis:
///   1) IDesignProvider.TryGetEffectiveLayoutIdAsync(ctx) ile uygun LayoutId bulunur.
///   2) IDocDesignerService.RenderPdfAsync(layoutId, entityId) cagrilir.
///   3) LayoutId yoksa veya render basarisiz olursa anlamli bir hata firlatilir.
///
/// Eski FastReport branch'i kaldirildi — tum basimlar Belge Tasarimcisi uzerinden.
/// </summary>
public interface IPrintDispatcher
{
    Task<byte[]> DispatchPrintAsync(
        DesignSelectionContext ctx,
        int entityId,
        CancellationToken ct = default);
}

/// <summary>
/// Dispatcher karar sonucu — log/audit ihtiyaclari icin ileride genisletilebilir.
/// </summary>
public enum PrintDispatchBranch
{
    NewEngine,    // DocLayoutRule + DocLayout
    None,
}
