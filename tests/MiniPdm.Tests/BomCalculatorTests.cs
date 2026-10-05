using FluentAssertions;
using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;

namespace MiniPdm.Tests;

// Тесты расчётов по дереву состава.
// Строки строятся вручную — проверяется чистая арифметика.
public sealed class BomCalculatorTests
{
    [Fact]
    public void Пустое_дерево_даёт_нулевую_полную_массу()
    {
        var result = BomCalculator.CalculateMass(new BomTreeRow[0]);

        result.IsComplete.Should().BeTrue();
        result.TotalKg.Should().Be(0m);
    }

    [Fact]
    public void Масса_сборки_суммируется_из_компонентов()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Part(2, 3m, 1, 1, 1, new long[] { 1, 2 }, parent: 1),
            Part(3, 0.5m, 1, 4, 4, new long[] { 1, 3 }, parent: 1)
        };

        var result = BomCalculator.CalculateMass(rows);

        result.IsComplete.Should().BeTrue();
        result.TotalKg.Should().Be(3m + 0.5m * 4);
    }

    [Fact]
    public void Масса_вложенной_сборки_перемножается_на_количество()
    {
        // Корень → сборка 2 в количестве 2 → деталь 1 кг внутри
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 2, 2, new long[] { 1, 2 }, parent: 1),
            Part(3, 1m, 2, 2, 1, new long[] { 1, 2, 3 }, parent: 2)
        };

        BomCalculator.CalculateMass(rows).TotalKg.Should().Be(2m);
    }

    [Fact]
    public void Компонент_без_массы_делает_расчёт_неполным()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Part(2, null, 1, 1, 1, new long[] { 1, 2 }, parent: 1)
        };

        var result = BomCalculator.CalculateMass(rows);

        result.IsComplete.Should().BeFalse();
        result.MissingMass.Should().ContainSingle();
        result.MissingMass[0].Name.Should().Be("Компонент 2");
    }

    [Fact]
    public void При_неполном_расчёте_сообщаются_все_компоненты_без_массы()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Part(2, null, 1, 1, 1, new long[] { 1, 2 }, parent: 1),
            Part(3, null, 1, 1, 1, new long[] { 1, 3 }, parent: 1)
        };

        BomCalculator.CalculateMass(rows).MissingMass.Should().HaveCount(2);
    }

    [Fact]
    public void Один_объект_в_двух_ветвях_учитывается_в_обеих()
    {
        // Регрессия: узел ищется по пути, а не по ObjectId.
        // Подшипник входит и в узел 2, и в узел 3.
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 1, 1, new long[] { 1, 2 }, parent: 1),
            Assembly(3, 1, 1, 1, new long[] { 1, 3 }, parent: 1),
            Part(4, 2m, 2, 1, 1, new long[] { 1, 2, 4 }, parent: 2),
            Part(4, 2m, 2, 3, 3, new long[] { 1, 3, 4 }, parent: 3)
        };

        // 1×2 + 3×2 = 8
        BomCalculator.CalculateMass(rows).TotalKg.Should().Be(8m);
    }

    [Fact]
    public void Количества_по_пути_перемножаются_в_спецификации()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 2, 2, new long[] { 1, 2 }, parent: 1),
            Part(3, 1m, 2, 6, 3, new long[] { 1, 2, 3 }, parent: 2)
        };

        var spec = BomCalculator.BuildSpecification(rows);

        spec.Should().ContainSingle();
        spec[0].TotalQuantity.Should().Be(6);
        spec[0].GetTotalMassKg().Should().Be(6m);
    }

    [Fact]
    public void Одинаковое_изделие_из_двух_ветвей_суммируется_в_одну_позицию()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Part(3, 1m, 1, 2, 2, new long[] { 1, 3 }, parent: 1),
            Part(3, 1m, 1, 3, 3, new long[] { 1, 3 }, parent: 1)
        };

        var spec = BomCalculator.BuildSpecification(rows);

        spec.Should().ContainSingle();
        spec[0].TotalQuantity.Should().Be(5);
    }

    [Fact]
    public void Сборки_не_попадают_в_сводную_спецификацию()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 1, 1, new long[] { 1, 2 }, parent: 1),
            Part(3, 1m, 2, 1, 1, new long[] { 1, 2, 3 }, parent: 2)
        };

        BomCalculator.BuildSpecification(rows).Should().ContainSingle();
    }

    [Fact]
    public void В_спецификации_детали_идут_перед_стандартными_изделиями()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Part(2, 1m, 1, 1, 1, new long[] { 1, 2 }, parent: 1,
                type: ObjectType.StandardPart, designation: null, name: "Болт М12"),
            Part(3, 2m, 1, 1, 1, new long[] { 1, 3 }, parent: 1,
                type: ObjectType.Part, designation: "РДЦЛ.304112.301", name: "Вал")
        };

        var spec = BomCalculator.BuildSpecification(rows);

        spec.Should().HaveCount(2);
        spec[0].Type.Should().Be(ObjectType.Part);
        spec[1].Type.Should().Be(ObjectType.StandardPart);
    }

    [Fact]
    public void Позиция_без_массы_имеет_пустую_суммарную_массу()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Part(2, null, 1, 4, 4, new long[] { 1, 2 }, parent: 1)
        };

        SpecificationRow row = BomCalculator.BuildSpecification(rows).Single();

        row.GetHasMass().Should().BeFalse();
        row.GetTotalMassKg().Should().BeNull();
        row.TotalQuantity.Should().Be(4);
    }

    [Fact]
    public void Сумма_масс_спецификации_равна_массе_сборки()
    {
        // Связь двух расчётов: если один ошибочен — сумма не совпадёт
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 4, 4, new long[] { 1, 2 }, parent: 1),
            Part(3, 1.15m, 2, 12, 3, new long[] { 1, 2, 3 }, parent: 2),
            Part(4, 0.05m, 1, 16, 16, new long[] { 1, 4 }, parent: 1)
        };

        decimal mass = BomCalculator.CalculateMass(rows).TotalKg;

        // Считаем сумму вручную через for
        IReadOnlyList<SpecificationRow> spec = BomCalculator.BuildSpecification(rows);
        decimal specTotal = 0m;

        for (int i = 0; i < spec.Count; i++)
        {
            decimal? rowMass = spec[i].GetTotalMassKg();
            if (rowMass != null)
            {
                specTotal += rowMass.Value;
            }
        }

        specTotal.Should().Be(mass);
        mass.Should().Be(4 * (3 * 1.15m) + 16 * 0.05m);
    }

    [Fact]
    public void Разворачивание_дерева_сохраняет_все_узлы()
    {
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 1, 1, new long[] { 1, 2 }, parent: 1),
            Part(3, 1m, 2, 1, 1, new long[] { 1, 2, 3 }, parent: 2)
        };

        BomTreeNode? tree = BomCalculator.BuildTree(rows);

        tree.Should().NotBeNull();
        BomCalculator.Flatten(tree).Should().HaveCount(3);
    }

    [Fact]
    public void Цикл_в_дереве_виден_в_результате_массы()
    {
        // Цикл не рвёт расчёт, но должен быть заметен
        var rows = new[]
        {
            Assembly(1, 0, 1, 1, new long[] { 1 }),
            Assembly(2, 1, 1, 1, new long[] { 1, 2 }, parent: 1),
            Assembly(1, 2, 1, 1, new long[] { 1, 2, 1 }, parent: 2)
        };

        var result = BomCalculator.CalculateMass(rows);

        result.Cycles.Should().ContainSingle();
        result.Cycles[0].NodeIds.Should().HaveCount(2);
        result.Cycles[0].GetDescription().Should().Contain("Сборка 1");
    }

    // Строка-сборка с настройками по умолчанию
    private static BomTreeRow Assembly(
        long id, int level, long quantity, long edge, IReadOnlyList<long> path, long? parent = null)
    {
        return new BomTreeRow(
            id,
            ObjectType.Assembly,
            "Сборка " + id,
            level,
            quantity,
            edge,
            path,
            "Сборка " + id,
            designation: "РДЦЛ.304112." + id.ToString("000"),
            parentObjectId: parent);
    }

    // Строка-деталь или стандартное изделие
    private static BomTreeRow Part(
        long id,
        decimal? mass,
        int level,
        long quantity,
        long edge,
        IReadOnlyList<long> path,
        long? parent = null,
        ObjectType type = ObjectType.Part,
        string? designation = null,
        string? name = null)
    {
        // Если обозначение не задано и это деталь — генерируем из id
        string? finalDesignation = designation;

        if (finalDesignation == null && type == ObjectType.Part)
        {
            finalDesignation = "РДЦЛ.304112." + id.ToString("000");
        }

        return new BomTreeRow(
            id,
            type,
            name ?? "Компонент " + id,
            level,
            quantity,
            edge,
            path,
            "Компонент " + id,
            designation: finalDesignation,
            massKg: mass,
            parentObjectId: parent);
    }
}