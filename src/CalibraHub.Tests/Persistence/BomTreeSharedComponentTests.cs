using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CalibraHub.Tests.Persistence;

/// <summary>
/// REÇETE AĞACI — PAYLAŞIMLI ALT REÇETE senaryosu, uçtan uca gerçek veritabanına karşı.
///
/// <para><b>Doğrulanan davranış (kullanıcı kararı 2026-08-29):</b> ağaçta bir alt reçete
/// düzenlendiğinde, o reçete birden fazla mamul tarafından kullanılıyorsa YERİNDE EZİLMEZ.
/// Otomatik yeni versiyon türetilir ve YALNIZ düzenlenen ağacın ata satırı o versiyona
/// sabitlenir. Diğer mamuller eski (baz) reçeteyi izlemeye devam eder.</para>
///
/// <para><b>Neden gerçek veritabanı:</b> bu davranış tek bir metotta değil, üç katmanın
/// birlikte çalışmasında yaşıyor — referans sayımı SQL'de (<c>GetBomReferenceCountsAsync</c>,
/// hem sabitlenmiş hem baz-izleyen satırları sayar), versiyon türetme serviste, satır
/// sabitleme ise post-order kaydetme sırasında. Sahte bir depoyla üçü de doğrulanmış
/// SAYILIR ama gerçekte kırık kalabilir.</para>
///
/// <para>Test kendi fikstürünü kurar ve <c>finally</c> içinde ÜRETTİĞİ HER SATIRI siler.
/// Veritabanı çözülemezse sessizce geçmez, Skip ile ATLANIR.</para>
/// </summary>
public sealed class BomTreeSharedComponentTests
{
    private static readonly Lazy<string?> LazyConn = new(ResolveConnectionString);
    private static string? Conn => LazyConn.Value;

    private static string? ResolveConnectionString()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "CalibraHub.Web", "appsettings.json");
            if (File.Exists(candidate))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                    var raw = doc.RootElement.GetProperty("CalibraDatabase")
                                             .GetProperty("ConnectionString").GetString();
                    var plain = DpapiSecretDecryptor.DecryptIfNeeded(raw);
                    return string.IsNullOrWhiteSpace(plain) ? null : plain;
                }
                catch { return null; }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static bool DbReachable()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return false;
        try
        {
            var b = new SqlConnectionStringBuilder(Conn) { ConnectTimeout = 5 };
            using var c = new SqlConnection(b.ConnectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN OBJECT_ID('dbo.BOM','U') IS NULL THEN 0 ELSE 1 END;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
        }
        catch { return false; }
    }

    // Kimliksiz erisim: ResolveEffectiveCompanyId veritabaninin SAHIBI sirketi doner
    // (CLAUDE.md coklu-kiracilik kurali). HttpContext ACIKCA null — statik AsyncLocal
    // uzerinden baska bir testin sirket baglami sizabiliyor.
    private static SqlServerConnectionFactory BuildFactory()
        => new(new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" },
               new CompanyConnectionRegistry(),
               new HttpContextAccessor { HttpContext = null });

    [SkippableFact]
    public async Task Paylasimli_alt_recete_degisince_versiyon_turetilir_diger_mamul_etkilenmez()
    {
        Skip.IfNot(DbReachable(), "CalibraHub veritabanina erisilemedi.");

        var factory = BuildFactory();
        var repo = new SqlLogisticsConfigurationRepository(
            factory,
            new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" },
            new NoDataVisibilityFilter());
        var service = new LogisticsConfigurationService(repo);
        var ct = CancellationToken.None;

        var tag = "ZZTREE" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var itemIds = new List<int>();
        var bomIds = new List<int>();

        try
        {
            var unitId = await FirstActiveUnitIdAsync();
            Skip.If(unitId is null, "Aktif olcu birimi yok — fikstur kurulamiyor.");

            // ── Fikstur ──
            //   P1 ─┐                     P2 ─┐
            //       └─ S ─ C1                 └─ S   (AYNI baz recete: paylasimli)
            var p1 = await InsertItemAsync(tag + "-P1", "Test Mamul 1", 3, unitId!.Value); itemIds.Add(p1);
            var p2 = await InsertItemAsync(tag + "-P2", "Test Mamul 2", 3, unitId.Value);  itemIds.Add(p2);
            var s  = await InsertItemAsync(tag + "-S",  "Test Yari Mamul", 2, unitId.Value); itemIds.Add(s);
            var c1 = await InsertItemAsync(tag + "-C1", "Test Hammadde 1", 1, unitId.Value); itemIds.Add(c1);
            var x  = await InsertItemAsync(tag + "-X",  "Test Hammadde 2", 1, unitId.Value); itemIds.Add(x);

            var bomS  = await AddBomAsync(repo, s,  (c1, 2m), ct); bomIds.Add(bomS);
            var bomP1 = await AddBomAsync(repo, p1, (s, 1m), ct);  bomIds.Add(bomP1);
            var bomP2 = await AddBomAsync(repo, p2, (s, 1m), ct);  bomIds.Add(bomP2);

            // ── 1) OKUMA: S dugumu PAYLASIMLI gorunmeli ──
            // Iki ata satiri da S'nin BAZ recetesini izliyor (ComponentBomId NULL),
            // dolayisiyla referans sayisi 2 olmali. 1 gorunurse SQL'deki "baz-izleyen"
            // dali calismiyor demektir ve paylasim hic fark edilmez.
            var tree = await service.GetBomTreeAsync(p1, null, bomP1, ct);
            Assert.NotNull(tree);
            var sNode = Assert.Single(tree!.Root.Children);
            Assert.Equal(s, sNode.ItemId);
            Assert.Equal(bomS, sNode.BomId);
            Assert.Equal(2, sNode.ReferenceCount);
            Assert.Single(sNode.Children);          // S -> C1

            // ── 2) YAZMA: S'nin altina yeni bir bilesen ekle ──
            var payload = ToSave(tree.Root);
            payload.Children[0].Children.Add(new MutableNode
            {
                ItemId = x, Quantity = 3m, ScrapRatio = 0m, BomId = null
            });

            var result = await service.SaveBomTreeAsync(new SaveBomTreeRequest(Freeze(payload)), null, ct);

            // Turetme RAPORLANMALI — kullanici versiyon aciladigini bilmeli.
            var derived = Assert.Single(result.Notes, n => n.Action == "derived");
            Assert.Equal(s, derived.ItemId);
            Assert.False(string.IsNullOrWhiteSpace(derived.VersionCode));
            Assert.Equal(2, derived.ReferenceCount);

            var derivedBomId = derived.BomId;
            bomIds.Add(derivedBomId);

            // ── 3) BAZ RECETE DOKUNULMAMIS OLMALI ──
            var baseAfter = await repo.GetBOMByIdWithNamesAsync(bomS, ct);
            Assert.NotNull(baseAfter);
            Assert.Null(baseAfter!.VersionCode);
            Assert.Single(baseAfter.Lines);                       // hala yalniz C1
            Assert.Equal(c1, baseAfter.Lines.First().ItemId);

            // ── 4) TURETILEN VERSIYON yeni bileseni tasimali ──
            var derivedBom = await repo.GetBOMByIdWithNamesAsync(derivedBomId, ct);
            Assert.NotNull(derivedBom);
            Assert.Equal(s, derivedBom!.ItemId);
            Assert.False(string.IsNullOrWhiteSpace(derivedBom.VersionCode));
            Assert.Equal(2, derivedBom.Lines.Count);
            Assert.Contains(derivedBom.Lines, l => l.ItemId == x && l.Quantity == 3m);

            // ── 5) DUZENLENEN mamulun satiri versiyona SABITLENMIS olmali ──
            var p1After = await repo.GetBOMByIdWithNamesAsync(bomP1, ct);
            var p1Line = Assert.Single(p1After!.Lines);
            Assert.Equal(s, p1Line.ItemId);
            Assert.Equal(derivedBomId, p1Line.ComponentBomId);

            // ── 6) EN KRITIK: DIGER mamul ETKILENMEMELI ──
            // Ozelligin butun varlik sebebi bu. P2'nin satiri sabitlenmemis kalmali ve
            // bazi izlemeye devam etmeli — yani P2 icin hicbir sey degismemis olmali.
            var p2After = await repo.GetBOMByIdWithNamesAsync(bomP2, ct);
            var p2Line = Assert.Single(p2After!.Lines);
            Assert.Equal(s, p2Line.ItemId);
            Assert.Null(p2Line.ComponentBomId);

            var p2Tree = await service.GetBomTreeAsync(p2, null, bomP2, ct);
            var p2SNode = Assert.Single(p2Tree!.Root.Children);
            Assert.Equal(bomS, p2SNode.BomId);                    // hala BAZ recete
            Assert.Single(p2SNode.Children);                      // hala yalniz C1 — X GELMEMELI

            // ── 7) DEGISIKLIK YOKSA IKINCI VERSIYON URETILMEMELI ──
            // Bu koruma olmasaydi agaci acip kaydetmek her seferinde yeni versiyon
            // dogururdu; ozellik bir cop ureticisine donerdi.
            var reread = await service.GetBomTreeAsync(p1, null, bomP1, ct);
            var again = await service.SaveBomTreeAsync(
                new SaveBomTreeRequest(Freeze(ToSave(reread!.Root))), null, ct);
            Assert.DoesNotContain(again.Notes, n => n.Action == "derived");

            var versions = await repo.GetBomVersionsAsync(s, null, ct);
            Assert.Equal(2, versions.Count);                      // 1 baz + 1 turetilmis
        }
        finally
        {
            await CleanupAsync(itemIds, bomIds);
        }
    }

    /// <summary>
    /// Veri gorunurlugu (satir bazli kisitlama) testte DEVRE DISI. Sebep: bu test
    /// paylasim/versiyon mantigini olcuyor; kullanici bazli gorunurluk kurallari
    /// devreye girseydi fikstur satirlari suzulup test yanlis yere kirmizi verirdi.
    /// </summary>
    private sealed class NoDataVisibilityFilter : IDataVisibilityFilter
    {
        public Task<DataVisibilityPredicate> BuildAsync(
            string formCode, string tableAlias, string idColumn, CancellationToken ct)
            => Task.FromResult(DataVisibilityPredicate.Empty);
        public void InvalidateCache(string formCode) { }
    }

    /// <summary>
    /// DÖNGÜ, ağaçta YAPRAK görünen bir bileşenin KAYITLI reçetesi üzerinden kapanıyorsa
    /// da yakalanmalı — ve zincir ADIYLA bildirilmeli.
    ///
    /// <para>Kullanıcının canlıda karşılaştığı durum tam buydu: bileşen ağaca yeni
    /// eklenmiş, ekranda çocuksuz görünüyor, ama veritabanındaki kendi reçetesi dolaylı
    /// olarak mamulün kendisine bağlı. Yalnız gönderilen ağaca bakan bir denetim bunu
    /// GÖREMEZ.</para>
    /// </summary>
    [SkippableFact]
    public async Task Kayitli_recete_uzerinden_kapanan_dongu_zinciriyle_birlikte_bildirilir()
    {
        Skip.IfNot(DbReachable(), "CalibraHub veritabanina erisilemedi.");

        var factory = BuildFactory();
        var repo = new SqlLogisticsConfigurationRepository(
            factory,
            new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" },
            new NoDataVisibilityFilter());
        var service = new LogisticsConfigurationService(repo);
        var ct = CancellationToken.None;

        var tag = "ZZTREE" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var itemIds = new List<int>();

        try
        {
            var unitId = await FirstActiveUnitIdAsync();
            Skip.If(unitId is null, "Aktif olcu birimi yok — fikstur kurulamiyor.");

            // A ─ C          (A'nin recetesi)
            // B ─ A          (B'nin recetesi A'yi KULLANIYOR)
            // Simdi A'nin agacina B eklenirse:  A -> B -> A  = dongu
            var a = await InsertItemAsync(tag + "-A", "Dongu Mamul A", 3, unitId!.Value); itemIds.Add(a);
            var b = await InsertItemAsync(tag + "-B", "Dongu Yari B", 2, unitId.Value);   itemIds.Add(b);
            var c = await InsertItemAsync(tag + "-C", "Dongu Ham C", 1, unitId.Value);    itemIds.Add(c);

            var bomA = await AddBomAsync(repo, a, (c, 1m), ct);
            await AddBomAsync(repo, b, (a, 1m), ct);

            var tree = await service.GetBomTreeAsync(a, null, bomA, ct);
            Assert.NotNull(tree);

            // B'yi A'nin altina ekle — agacta YAPRAK olarak (cocuksuz), tipki ekranda
            // yeni bilesen eklendiginde oldugu gibi.
            var payload = ToSave(tree!.Root);
            payload.Children.Add(new MutableNode { ItemId = b, Quantity = 1m, ScrapRatio = 0m, BomId = null });

            var ex = await Assert.ThrowsAsync<BomCycleException>(() =>
                service.SaveBomTreeAsync(new SaveBomTreeRequest(Freeze(payload)), null, ct));

            // Zincirdeki HER IKI malzeme de bildirilmeli — ekran bunlari isaretleyecek.
            Assert.Contains(a, ex.ItemIds);
            Assert.Contains(b, ex.ItemIds);
            // Mesaj kodlari icermeli; "bu bilesenlerden biri" gibi genel bir metin
            // kullaniciya hangi bagi kaldiracagini SOYLEMIYORDU.
            Assert.Contains(tag + "-A", ex.Message);
            Assert.Contains(tag + "-B", ex.Message);

            // Ve HICBIR SEY YAZILMAMIS olmali: denetim kaydetmeden ONCE calisir,
            // yarim yazilmis bir agac birakmaz.
            var aAfter = await repo.GetBOMByIdWithNamesAsync(bomA, ct);
            Assert.Single(aAfter!.Lines);
            Assert.Equal(c, aAfter.Lines.First().ItemId);
        }
        finally
        {
            await CleanupAsync(itemIds, new List<int>());
        }
    }

    /// <summary>
    /// AYNI İÇERİK İKİNCİ KEZ İSTENİRSE yeni sürüm AÇILMAZ — mevcut sürüme bağlanır.
    ///
    /// <para><b>Neden önemli:</b> yalnızca kayıt tasarrufu değil. MRP kümüle iş emirlerini
    /// reçeteye göre gruplayacağı için, aynı içeriğin iki ayrı sürümde durması aynı işi
    /// İKİ AYRI iş emrine bölerdi. Tekilleştirme bunu kaynağında engeller.</para>
    ///
    /// <para>Senaryo: P1 ve P2 aynı yarı mamulü (S) kullanıyor. Önce P1'in ağacında S
    /// özelleştiriliyor → sürüm türüyor. Sonra P2'nin ağacında S AYNI hâle getiriliyor →
    /// ikinci sürüm DOĞMAMALI, P2'nin satırı da ilk sürüme sabitlenmeli.</para>
    /// </summary>
    [SkippableFact]
    public async Task Ayni_icerikli_surum_varsa_yenisi_acilmaz_mevcut_surume_baglanir()
    {
        Skip.IfNot(DbReachable(), "CalibraHub veritabanina erisilemedi.");

        var factory = BuildFactory();
        var repo = new SqlLogisticsConfigurationRepository(
            factory,
            new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" },
            new NoDataVisibilityFilter());
        var service = new LogisticsConfigurationService(repo);
        var ct = CancellationToken.None;

        var tag = "ZZTREE" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var itemIds = new List<int>();

        try
        {
            var unitId = await FirstActiveUnitIdAsync();
            Skip.If(unitId is null, "Aktif olcu birimi yok — fikstur kurulamiyor.");

            var p1 = await InsertItemAsync(tag + "-P1", "Test Mamul 1", 3, unitId!.Value); itemIds.Add(p1);
            var p2 = await InsertItemAsync(tag + "-P2", "Test Mamul 2", 3, unitId.Value);  itemIds.Add(p2);
            var s  = await InsertItemAsync(tag + "-S",  "Test Yari Mamul", 2, unitId.Value); itemIds.Add(s);
            var c1 = await InsertItemAsync(tag + "-C1", "Test Hammadde 1", 1, unitId.Value); itemIds.Add(c1);
            var x  = await InsertItemAsync(tag + "-X",  "Test Hammadde 2", 1, unitId.Value); itemIds.Add(x);

            var bomS  = await AddBomAsync(repo, s,  (c1, 2m), ct);
            var bomP1 = await AddBomAsync(repo, p1, (s, 1m), ct);
            var bomP2 = await AddBomAsync(repo, p2, (s, 1m), ct);

            // ── 1) P1'in agacinda S'ye X ekle → surum turemeli ──
            var t1 = await service.GetBomTreeAsync(p1, null, bomP1, ct);
            var pay1 = ToSave(t1!.Root);
            pay1.Children[0].Children.Add(new MutableNode { ItemId = x, Quantity = 3m, ScrapRatio = 0m });
            var r1 = await service.SaveBomTreeAsync(new SaveBomTreeRequest(Freeze(pay1)), null, ct);
            var derived = Assert.Single(r1.Notes, n => n.Action == "derived");
            var versionBomId = derived.BomId;

            // ── 2) P2'nin agacinda S'yi AYNI hale getir → YENI SURUM ACILMAMALI ──
            var t2 = await service.GetBomTreeAsync(p2, null, bomP2, ct);
            var pay2 = ToSave(t2!.Root);
            pay2.Children[0].Children.Add(new MutableNode { ItemId = x, Quantity = 3m, ScrapRatio = 0m });
            var r2 = await service.SaveBomTreeAsync(new SaveBomTreeRequest(Freeze(pay2)), null, ct);

            Assert.DoesNotContain(r2.Notes, n => n.Action == "derived");
            var reused = Assert.Single(r2.Notes, n => n.Action == "reused");
            Assert.Equal(versionBomId, reused.BomId);

            // ── 3) S'nin toplam recete sayisi 2 olmali (baz + TEK surum) ──
            var versions = await repo.GetBomVersionsAsync(s, null, ct);
            Assert.Equal(2, versions.Count);

            // ── 4) HER IKI mamulun satiri da AYNI surume sabitlenmis olmali ──
            // MRP kumule gruplamasi recete kimligine bakacagi icin bu esitlik,
            // iki talebin TEK is emrinde birlesmesinin on kosulu.
            var p1Line = Assert.Single((await repo.GetBOMByIdWithNamesAsync(bomP1, ct))!.Lines);
            var p2Line = Assert.Single((await repo.GetBOMByIdWithNamesAsync(bomP2, ct))!.Lines);
            Assert.Equal(versionBomId, p1Line.ComponentBomId);
            Assert.Equal(versionBomId, p2Line.ComponentBomId);

            // ── 5) BAZ recete hala dokunulmamis ──
            var baseAfter = await repo.GetBOMByIdWithNamesAsync(bomS, ct);
            Assert.Single(baseAfter!.Lines);
        }
        finally
        {
            await CleanupAsync(itemIds, new List<int>());
        }
    }

    // ── Fikstur yardimcilari ────────────────────────────────────────────

    private static async Task<int?> FirstActiveUnitIdAsync()
    {
        await using var c = new SqlConnection(Conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 [Id] FROM [dbo].[Unit] WHERE [IsActive] = 1 ORDER BY [Id];";
        var v = await cmd.ExecuteScalarAsync();
        return v is null || v == DBNull.Value ? null : Convert.ToInt32(v);
    }

    private static async Task<int> InsertItemAsync(string code, string name, int typeId, int unitId)
    {
        await using var c = new SqlConnection(Conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        // CompanyId veritabaninin sahibi sirketten alinir — sabit 1 yazmak, sahibi
        // baska bir sirket olan bir veritabaninda gorunmez satir uretirdi.
        cmd.CommandText = """
            DECLARE @Cid INT = (SELECT TOP 1 [Id] FROM [dbo].[Company] ORDER BY [Id]);
            INSERT INTO [dbo].[Items]
                ([CompanyId],[Code],[Name],[TypeId],[UnitId],[IsActive],[Created],
                 [Combinations],[TaxRate],[TrackingType],[MinStock],[AutoSerial])
            VALUES (@Cid,@Code,@Name,@TypeId,@UnitId,1,SYSUTCDATETIME(),0,20,'None',0,0);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@TypeId", typeId);
        cmd.Parameters.AddWithValue("@UnitId", unitId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> AddBomAsync(
        SqlLogisticsConfigurationRepository repo, int itemId, (int ItemId, decimal Qty) line,
        CancellationToken ct)
    {
        var bom = new BOM { ItemId = itemId, Description = "otomatik test fiksturu" };
        bom.AddLine(BOMLine.Create(line.ItemId, null, line.Qty, 0m));
        return await repo.AddBOMAsync(bom, ct);
    }

    private static async Task CleanupAsync(List<int> itemIds, List<int> bomIds)
    {
        if (itemIds.Count == 0 && bomIds.Count == 0) return;
        try
        {
            await using var c = new SqlConnection(Conn);
            await c.OpenAsync();

            // Turetilen versiyonlar bomIds'te olmayabilir (kaydetme sirasinda dogdular);
            // bu yuzden fikstur MALZEMELERINE ait TUM receteler silinir.
            await using (var cmd = c.CreateCommand())
            {
                var ids = string.Join(",", itemIds);
                if (ids.Length > 0)
                {
                    cmd.CommandText = $"""
                        DELETE l FROM [dbo].[BOMLine] l
                          INNER JOIN [dbo].[BOM] b ON b.[Id] = l.[BOMId]
                          WHERE b.[ItemId] IN ({ids}) OR l.[ItemId] IN ({ids});
                        DELETE FROM [dbo].[BOM] WHERE [ItemId] IN ({ids});
                        DELETE FROM [dbo].[Items] WHERE [Id] IN ({ids});
                        """;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            // Temizlik hatasi testi maskelemesin, ama SESSIZ de kalmasin.
            Console.Error.WriteLine("[BomTreeSharedComponentTests] Fikstur temizligi basarisiz: " + ex.Message);
        }
    }

    // ── Agac -> kaydetme yuku donusumu ──────────────────────────────────
    // SaveBomTreeNode degismez (record) oldugundan, once mutable bir agaca cevirip
    // duzenliyor, sonra donduruyoruz.

    private sealed class MutableNode
    {
        public int ItemId;
        public int? ConfigId;
        public decimal Quantity;
        public decimal ScrapRatio;
        public string? Note;
        public int? BomId;
        public List<MutableNode> Children = new();
    }

    private static MutableNode ToSave(BomTreeNodeDto n) => new()
    {
        ItemId = n.ItemId, ConfigId = n.ConfigId,
        Quantity = n.Quantity, ScrapRatio = n.ScrapRatio, Note = n.Note,
        BomId = n.BomId,
        Children = n.Children.Select(ToSave).ToList(),
    };

    private static SaveBomTreeNode Freeze(MutableNode n) => new(
        n.ItemId, n.ConfigId, n.Quantity, n.ScrapRatio, n.Note, n.BomId,
        n.Children.Select(Freeze).ToList());
}
