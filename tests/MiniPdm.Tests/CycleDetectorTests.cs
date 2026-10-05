using FluentAssertions;
using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;

namespace MiniPdm.Tests;

// Тесты обнаружения циклических ссылок.
public sealed class CycleDetectorTests
{
    [Fact]
    public void Граф_без_циклов_не_даёт_результатов()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "C" },
            ["C"] = new string[0]
        };

        CycleDetector.FindCycles(graph).Should().BeEmpty();
    }

    [Fact]
    public void Простой_цикл_обнаруживается()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "A" }
        };

        var cycles = CycleDetector.FindCycles(graph);

        cycles.Should().ContainSingle();
        cycles[0].NodeIds.Should().BeEquivalentTo(new string[] { "A", "B" });
    }

    [Fact]
    public void Цикл_находится_независимо_от_точки_входа()
    {
        // «A→B→C→A» и «B→C→A→B» — один и тот же цикл, вернуться должен один раз
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "C" },
            ["C"] = new string[] { "A" }
        };

        CycleDetector.FindCycles(graph).Should().ContainSingle();
    }

    [Fact]
    public void Цикл_не_замыкается_дважды_в_описании()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "A" }
        };

        CycleDetector.FindCycles(graph)[0].GetDescription().Should().Be("A → B → A");
    }

    [Fact]
    public void Цикл_длиннее_двух_узлов_обнаруживается()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "C" },
            ["C"] = new string[] { "A" }
        };

        CycleDetector.FindCycles(graph)[0].NodeIds.Should().HaveCount(3);
    }

    [Fact]
    public void Несколько_независимых_циклов_находятся_все()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "A" },
            ["C"] = new string[] { "D" },
            ["D"] = new string[] { "C" }
        };

        CycleDetector.FindCycles(graph).Should().HaveCount(2);
    }

    [Fact]
    public void Рекурсивный_обход_не_зависает_на_цикле()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new string[] { "B" },
            ["B"] = new string[] { "A" }
        };

        CycleDetector.FindCycles(graph).Should().NotBeNull();
    }

    // ── Циклы в дереве из рекурсивного CTE ───────────────────────────────────

    [Fact]
    public void Цикл_в_дереве_находится_по_повтору_в_пути()
    {
        // Идентификатор 1 уже встречался выше по пути — это замыкающее ребро
        var rows = new[]
        {
            Row(1, 0, new long[] { 1 }),
            Row(2, 1, new long[] { 1, 2 }, parent: 1),
            Row(1, 2, new long[] { 1, 2, 1 }, parent: 2)
        };

        var cycles = CycleDetector.FindCyclesInTree(rows);

        cycles.Should().ContainSingle();
        cycles[0].GetDescription().Should().Be("Узел 1 → Узел 2 → Узел 1");
    }

    [Fact]
    public void Дерево_без_цикла_циклов_не_даёт()
    {
        var rows = new[]
        {
            Row(1, 0, new long[] { 1 }),
            Row(2, 1, new long[] { 1, 2 }, parent: 1),
            Row(3, 2, new long[] { 1, 2, 3 }, parent: 2)
        };

        CycleDetector.FindCyclesInTree(rows).Should().BeEmpty();
    }

    [Fact]
    public void Цикл_длиннее_двух_узлов_находится_в_дереве()
    {
        var rows = new[]
        {
            Row(1, 0, new long[] { 1 }),
            Row(2, 1, new long[] { 1, 2 }, parent: 1),
            Row(3, 2, new long[] { 1, 2, 3 }, parent: 2),
            Row(1, 3, new long[] { 1, 2, 3, 1 }, parent: 3)
        };

        var cycles = CycleDetector.FindCyclesInTree(rows);

        cycles.Should().ContainSingle();
        cycles[0].NodeIds.Should().HaveCount(3);
        cycles[0].GetDescription().Should().Be("Узел 1 → Узел 2 → Узел 3 → Узел 1");
    }

    // Строка дерева для тестов: путь задаётся явно, как его строит SQL
    private static BomTreeRow Row(long id, int level, IReadOnlyList<long> path, long? parent = null)
    {
        return new BomTreeRow(
            id,
            ObjectType.Assembly,
            "Узел " + id,
            level,
            1,
            1,
            path,
            "Узел " + id,
            parentObjectId: parent);
    }
}