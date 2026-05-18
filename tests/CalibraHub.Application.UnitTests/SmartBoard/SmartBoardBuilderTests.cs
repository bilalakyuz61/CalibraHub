using SB = CalibraHub.Application.SmartBoard;

namespace CalibraHub.Application.UnitTests.SmartBoardTests;

/// <summary>
/// SmartBoardBuilder fluent API testleri — uretilen Dictionary shape'inin SmartBoard.jsx
/// sozlesmesine uydugunu dogrular. Rapor §2.5 cozumu icin sozlesme garantisi.
/// </summary>
public sealed class SmartBoardBuilderTests
{
    private sealed record TestItem(int Id, string Name, bool IsActive);

    // ── Header config ──────────────────────────────────────────────────

    [Fact]
    public void Build_HeaderFields_AreSerialized()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .WithBoardKey("test-board")
            .WithTitle("Test Board", subtitle: "1 item")
            .WithIcon("Cog", "indigo")
            .WithRefreshUrl("/test/refresh")
            .WithSearchPlaceholder("ara...")
            .WithEmptyText("bos")
            .Build();

        board["boardKey"].Should().Be("test-board");
        board["title"].Should().Be("Test Board");
        board["subtitle"].Should().Be("1 item");
        board["icon"].Should().Be("Cog");
        board["iconColor"].Should().Be("indigo");
        board["refreshUrl"].Should().Be("/test/refresh");
        board["searchPlaceholder"].Should().Be("ara...");
        board["emptyText"].Should().Be("bos");
    }

    [Fact]
    public void Build_DefaultIconColor_IsIndigo()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(Array.Empty<TestItem>())
            .WithIcon("Cog")
            .Build();

        board["iconColor"].Should().Be("indigo");
    }

    // ── Actions ────────────────────────────────────────────────────────

    [Fact]
    public void AddHeaderAction_AppendsToActionsArray()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(Array.Empty<TestItem>())
            .AddHeaderAction("new", "Yeni", "Plus", "/edit")
            .AddHeaderAction("imp", "Import", "Upload", "/import", variant: "secondary")
            .Build();

        var actions = (object[])board["actions"]!;
        actions.Should().HaveCount(2);
    }

    [Fact]
    public void NoHeaderActions_ReturnsEmptyArray()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(Array.Empty<TestItem>())
            .Build();

        var actions = (object[])board["actions"]!;
        actions.Should().BeEmpty();
    }

    // ── Entity mapping ─────────────────────────────────────────────────

    [Fact]
    public void MapEntities_AppliesMapperToEachItem()
    {
        var items = new[] { new TestItem(1, "A", true), new TestItem(2, "B", false) };

        var board = (Dictionary<string, object?>)SB.SmartBoard.For(items)
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name))
            .Build();

        var entities = (object[])board["entities"]!;
        entities.Should().HaveCount(2);
    }

    [Fact]
    public void MapEntities_PreservesOrder()
    {
        var items = new[] { new TestItem(3, "Z", true), new TestItem(1, "A", true), new TestItem(2, "M", true) };

        var board = (Dictionary<string, object?>)SB.SmartBoard.For(items)
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name))
            .Build();

        var entities = (Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray();
        entities[0]["id"].Should().Be(3);
        entities[1]["id"].Should().Be(1);
        entities[2]["id"].Should().Be(2);
    }

    [Fact]
    public void NoMapEntities_ReturnsEmptyEntities()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .Build();

        var entities = (object[])board["entities"]!;
        entities.Should().BeEmpty();
    }

    // ── Entity builder: widgets ────────────────────────────────────────

    [Fact]
    public void AddStatusWidget_ActiveTrue_GreenColor()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name)
                .AddStatusWidget("w_status", "Durum", i.IsActive))
            .Build();

        var entity = ((Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray())[0];
        var widgets = (object[])entity["widgets"]!;
        widgets.Should().HaveCount(1);
        // Widget anonim type — reflection ile bak
        var w = widgets[0];
        var props = w.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(w));
        props["value"].Should().Be("Aktif");
        props["color"].Should().Be("emerald");
    }

    [Fact]
    public void AddStatusWidget_ActiveFalse_SlateColor()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", false) })
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name)
                .AddStatusWidget("w_status", "Durum", i.IsActive))
            .Build();

        var entity = ((Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray())[0];
        var widgets = (object[])entity["widgets"]!;
        var w = widgets[0];
        var props = w.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(w));
        props["value"].Should().Be("Pasif");
        props["color"].Should().Be("slate");
    }

    [Fact]
    public void AddTextWidget_PopulatesFields()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name)
                .AddTextWidget("w_code", "Kod", "ABC-001", detail: "tedarikçi", color: "amber"))
            .Build();

        var entity = ((Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray())[0];
        var w = ((object[])entity["widgets"]!)[0];
        var props = w.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(w));
        props["value"].Should().Be("ABC-001");
        props["detail"].Should().Be("tedarikçi");
        props["color"].Should().Be("amber");
    }

    // ── Entity builder: action shortcuts ───────────────────────────────

    [Fact]
    public void WithEditAndDelete_GeneratesBothActions()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name)
                .WithEditAndDelete(
                    editUrl: "/edit/1",
                    deleteApiUrl: "/del/1",
                    deleteConfirm: "Silmek istiyor musun?"))
            .Build();

        var entity = ((Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray())[0];
        entity["primaryAction"].Should().NotBeNull();
        entity["secondaryAction"].Should().NotBeNull();

        var primary = entity["primaryAction"]!;
        var pProps = primary.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(primary));
        pProps["url"].Should().Be("/edit/1");
        pProps["icon"].Should().Be("Edit");

        var secondary = entity["secondaryAction"]!;
        var sProps = secondary.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(secondary));
        sProps["apiUrl"].Should().Be("/del/1");
        sProps["confirm"].Should().Be("Silmek istiyor musun?");
        sProps["icon"].Should().Be("Trash2");
    }

    [Fact]
    public void WithNavigateAction_HideButtonFalse()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name)
                .WithNavigateAction("Görüntüle", "Eye", "/view/1"))
            .Build();

        var entity = ((Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray())[0];
        var primary = entity["primaryAction"]!;
        var props = primary.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(primary));
        props["hideButton"].Should().Be(false);
    }

    // ── Entity builder: status badge ───────────────────────────────────

    [Fact]
    public void WithStatusBadge_PopulatesObject()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(new[] { new TestItem(1, "A", true) })
            .MapEntities(i => SB.SmartBoardEntity.For(i.Id, i.Name)
                .WithStatusBadge("Onaylı", "emerald"))
            .Build();

        var entity = ((Dictionary<string, object?>[])((object[])board["entities"]!).Cast<Dictionary<string, object?>>().ToArray())[0];
        entity["statusBadge"].Should().NotBeNull();
        var b = entity["statusBadge"]!;
        var props = b.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(b));
        props["label"].Should().Be("Onaylı");
        props["color"].Should().Be("emerald");
    }

    // ── Extras (tree / master-detail) ───────────────────────────────────

    [Fact]
    public void WithExtra_AddsArbitraryProperty()
    {
        var board = (Dictionary<string, object?>)SB.SmartBoard.For(Array.Empty<TestItem>())
            .WithExtra("routingFormCode", "ROUTINGS")
            .WithExtra("opMasterWidgets", new List<object>())
            .Build();

        board["routingFormCode"].Should().Be("ROUTINGS");
        board["opMasterWidgets"].Should().NotBeNull();
    }
}
