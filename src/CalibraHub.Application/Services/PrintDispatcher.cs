using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// PrintDispatcher — Belge Tasarimcisi (DocDesigner) yoluyla PDF uretir.
/// Tum basimlar DocLayoutRule + DocLayout uzerinden DocDesigner motoru
/// tarafindan render edilir.
/// </summary>
public sealed class PrintDispatcher : IPrintDispatcher
{
    private readonly IDesignProvider _designProvider;
    private readonly IDocDesignerService _docDesigner;
    private readonly ILogger<PrintDispatcher> _logger;

    public PrintDispatcher(
        IDesignProvider designProvider,
        IDocDesignerService docDesigner,
        ILogger<PrintDispatcher> logger)
    {
        _designProvider = designProvider;
        _docDesigner    = docDesigner;
        _logger         = logger;
    }

    public async Task<byte[]> DispatchPrintAsync(
        DesignSelectionContext ctx,
        int entityId,
        CancellationToken ct = default)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (entityId <= 0) throw new ArgumentException("entityId > 0 olmalı.", nameof(entityId));

        // DesignProvider — DocLayoutRule + IsDefault zinciri ile uygun LayoutId secer
        int? layoutId;
        try
        {
            layoutId = await _designProvider.TryGetEffectiveLayoutIdAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PrintDispatcher] {DocType} entity={Entity} — DesignProvider hata verdi.",
                ctx.DocType, entityId);
            throw new InvalidOperationException(
                $"Tasarim secimi yapilamadi ({ctx.DocType}): {ex.Message}. " +
                "Belge Tasarimcisi'nda bir tasarimi Varsayilan yapin veya Tasarim Kurallari ekleyin.", ex);
        }

        if (!layoutId.HasValue)
        {
            throw new InvalidOperationException(
                $"'{ctx.DocType}' belge tipi icin tasarim bulunamadi. " +
                "Belge Tasarimcisi'nda bir tasarimi 'Varsayilan' isaretleyin veya Tasarim Kurallari'ndan kural ekleyin.");
        }

        _logger.LogInformation(
            "[PrintDispatcher] {DocType} entity={Entity} → LayoutId={LayoutId}",
            ctx.DocType, entityId, layoutId.Value);

        try
        {
            var pdf = await _docDesigner.RenderPdfAsync(
                new DocLayoutRunRequest(layoutId.Value, entityId, null), ct);
            if (pdf is { Length: > 0 }) return pdf;

            throw new InvalidOperationException(
                $"Tasarim (LayoutId={layoutId.Value}) render edildi ama bos PDF uretti. " +
                "Lutfen tasarimi Belge Tasarimcisi'nda acip veri kaynaklarini ve band baglantilarini kontrol edin.");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PrintDispatcher] Render hatasi (Layout={LayoutId}).", layoutId.Value);
            throw new InvalidOperationException(
                $"Tasarim render edilemedi (LayoutId={layoutId.Value}): {ex.Message}. " +
                "Belge Tasarimcisi'nda tasarimi acip veri baglantilarini ve element binding'leri kontrol edin.", ex);
        }
    }
}
