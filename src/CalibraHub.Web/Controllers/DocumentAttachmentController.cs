using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// DocumentAttachmentController — Belge ekleri (PDF/foto/Excel vb.) yukleme/listeleme/
/// indirme/silme endpoint'leri (rapor §2.3 SalesController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Sales/GetDocumentAttachments?documentId=  → liste
///   - POST /Sales/UploadDocumentAttachment            → multipart upload (50 MB limit)
///   - GET  /Sales/DownloadDocumentAttachment?id=      → byte download
///   - POST /Sales/DeleteDocumentAttachment            → soft-delete
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.DocTemplates)]
public sealed class DocumentAttachmentController : Controller
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public DocumentAttachmentController(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions dbOptions)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    [HttpGet("/Sales/GetDocumentAttachments")]
    public async Task<IActionResult> GetDocumentAttachments(int documentId, CancellationToken ct)
    {
        var list = new List<object>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[FileName],[MimeType],[FileSize],[UploadedAt]
            FROM [{_schema}].[DocumentAttachment]
            WHERE [DocumentId] = @DocumentId AND [IsActive] = 1
            ORDER BY [UploadedAt] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                id = r.GetInt32(0),
                fileName = r.GetString(1),
                mimeType = r.IsDBNull(2) ? null : r.GetString(2),
                fileSize = r.GetInt64(3),
                uploadedAt = r.GetDateTime(4),
            });
        }
        return Json(list);
    }

    [HttpPost("/Sales/UploadDocumentAttachment")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB per dosya
    public async Task<IActionResult> UploadDocumentAttachment(
        [FromForm] int documentId,
        [FromForm] List<IFormFile> files,
        CancellationToken ct)
    {
        if (documentId <= 0)
            return BadRequest(new { success = false, message = "Belge id gecersiz." });
        if (files == null || files.Count == 0)
            return BadRequest(new { success = false, message = "Dosya yok." });

        var user = User.Identity?.Name ?? "unknown";
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        foreach (var file in files)
        {
            if (file.Length <= 0) continue;
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO [{_schema}].[DocumentAttachment]
                    ([DocumentId],[FileName],[MimeType],[FileSize],[Content],[UploadedBy],[UploadedAt],[IsActive])
                VALUES (@DocumentId,@FileName,@Mime,@Size,@Content,@User,SYSUTCDATETIME(),1);
                """;
            cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
            cmd.Parameters.Add(new SqlParameter("@FileName", file.FileName ?? "file"));
            cmd.Parameters.Add(new SqlParameter("@Mime", (object?)file.ContentType ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Size", (long)bytes.Length));
            cmd.Parameters.Add(new SqlParameter("@Content", bytes));
            cmd.Parameters.Add(new SqlParameter("@User", user));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return Json(new { success = true });
    }

    [HttpGet("/Sales/DownloadDocumentAttachment")]
    public async Task<IActionResult> DownloadDocumentAttachment(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [FileName],[MimeType],[Content]
            FROM [{_schema}].[DocumentAttachment]
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return NotFound();
        var fileName = r.GetString(0);
        var mime = r.IsDBNull(1) ? "application/octet-stream" : r.GetString(1);
        var content = (byte[])r[2];
        return File(content, mime, fileName);
    }

    [HttpPost("/Sales/DeleteDocumentAttachment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocumentAttachment([FromBody] DeleteAttachmentBody body, CancellationToken ct)
    {
        if (body == null || body.Id <= 0)
            return BadRequest(new { success = false, message = "Id gecersiz." });
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE [{_schema}].[DocumentAttachment] SET [IsActive] = 0 WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", body.Id));
        await cmd.ExecuteNonQueryAsync(ct);
        return Json(new { success = true });
    }
}
