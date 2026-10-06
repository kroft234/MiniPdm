using FluentAssertions;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;

namespace MiniPdm.Tests;

// Тесты правил повторного импорта.
// Здесь решается, создаётся ли новый объект, обновляется ли рабочая
// версия на месте или появляется новая версия.
public sealed class VersionPlannerTests
{
    [Fact]
    public void Новый_объект_создаётся_с_первой_версией_в_работе()
    {
        var plan = VersionPlanner.Plan(Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m), null);

        plan.Action.Should().Be(ImportAction.Create);
        plan.VersionNo.Should().Be(1);
        plan.State.Should().Be(ObjectState.InWork);
        plan.Material.Should().Be("Сталь 45");
        plan.MassKg.Should().Be(3.42m);
        plan.SourceFile.Should().Be("Вал промежуточный.m3d");
        plan.Components.Should().BeEmpty();
    }

    [Fact]
    public void Повторный_импорт_без_изменений_ничего_не_пишет()
    {
        var document = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m);
        var existing = Stored(document, ObjectState.InWork);

        var plan = VersionPlanner.Plan(document, existing);

        plan.Action.Should().Be(ImportAction.Unchanged);
    }

    [Fact]
    public void Изменение_массы_рабочей_версии_обновляет_её_на_месте()
    {
        var existing = Stored(Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m), ObjectState.InWork);
        var changed = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.50m);

        var plan = VersionPlanner.Plan(changed, existing);

        plan.Action.Should().Be(ImportAction.UpdateInPlace);
        plan.VersionNo.Should().Be(1);
        plan.MassKg.Should().Be(3.50m);
        plan.SourceVersionId.Should().NotBeNull();
    }

    [Fact]
    public void Изменение_массы_утверждённой_версии_создаёт_новую_версию()
    {
        var existing = Stored(Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m), ObjectState.Approved);
        var changed = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.50m);

        var plan = VersionPlanner.Plan(changed, existing);

        // Утверждённая версия неизменяема — правится не она, а создаётся v2
        plan.Action.Should().Be(ImportAction.NewVersion);
        plan.VersionNo.Should().Be(2);
        plan.State.Should().Be(ObjectState.InWork);
    }

    [Fact]
    public void Номер_новой_версии_считается_от_максимального_а_не_от_текущей()
    {
        // v1 утверждена, v2 аннулирована, v3 в работе. Следующая — v4
        var existing = Stored(
            Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m),
            ObjectState.Approved, maxVersionNo: 3);

        var plan = VersionPlanner.Plan(
            Part("РДЦЛ.304112.301", "Вал промежуточный", 3.50m), existing);

        plan.Action.Should().Be(ImportAction.NewVersion);
        plan.VersionNo.Should().Be(4);
    }

    [Fact]
    public void Изменение_состава_сборки_рабочей_версии_обновляет_её_на_месте()
    {
        var existing = Stored(
            Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
                new CadComponent("Крышка подшипника.m3d", 1),
                new CadComponent("Прокладка регулировочная.m3d", 3)),
            ObjectState.InWork);

        var changed = Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
            new CadComponent("Крышка подшипника.m3d", 1),
            new CadComponent("Прокладка регулировочная.m3d", 4));

        var plan = VersionPlanner.Plan(changed, existing);

        plan.Action.Should().Be(ImportAction.UpdateInPlace);
        plan.Components.Should().HaveCount(2);
    }

    [Fact]
    public void Изменение_состава_утверждённой_сборки_создаёт_новую_версию()
    {
        var existing = Stored(
            Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
                new CadComponent("Крышка подшипника.m3d", 1),
                new CadComponent("Прокладка регулировочная.m3d", 3)),
            ObjectState.Approved);

        var changed = Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
            new CadComponent("Крышка подшипника.m3d", 1),
            new CadComponent("Прокладка регулировочная.m3d", 4));

        var plan = VersionPlanner.Plan(changed, existing);

        plan.Action.Should().Be(ImportAction.NewVersion);
        plan.VersionNo.Should().Be(2);
    }

    [Fact]
    public void Добавление_компонента_считается_изменением()
    {
        var existing = Stored(
            Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
                new CadComponent("Крышка подшипника.m3d", 1)),
            ObjectState.InWork);

        var changed = Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
            new CadComponent("Крышка подшипника.m3d", 1),
            new CadComponent("Прокладка регулировочная.m3d", 3));

        VersionPlanner.Plan(changed, existing).Action.Should().Be(ImportAction.UpdateInPlace);
    }

    [Fact]
    public void Перестановка_компонентов_не_считается_изменением()
    {
        var existing = Stored(
            Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
                new CadComponent("Крышка подшипника.m3d", 1),
                new CadComponent("Прокладка регулировочная.m3d", 3)),
            ObjectState.InWork);

        var reordered = Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
            new CadComponent("Прокладка регулировочная.m3d", 3),
            new CadComponent("Крышка подшипника.m3d", 1));

        VersionPlanner.Plan(reordered, existing).Action.Should().Be(ImportAction.Unchanged);
    }

    [Fact]
    public void Состав_сравнивается_по_имени_файла_а_не_по_отображаемому_ключу()
    {
        // Регрессия: раньше сравнение шло по ObjectKey.Display,
        // которого в документе нет, и любой повторный импорт считался изменением.
        var existing = Stored(
            Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
                new CadComponent("Крышка подшипника.m3d", 1)),
            ObjectState.InWork);

        var same = Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
            new CadComponent("Крышка подшипника.m3d", 1));

        VersionPlanner.Plan(same, existing).Action.Should().Be(ImportAction.Unchanged);
    }

    [Fact]
    public void Одинаковые_компоненты_с_разными_количествами_не_считаются_равными()
    {
        // Состав — мультимножество: сохранено a×1 и a×2, пришло a×1 и a×1
        var existing = new StoredObject(
            1,
            ObjectKey.From(ObjectType.Assembly, "РДЦЛ.304112.500", "Сборка"),
            1,
            new StoredVersion(10, 1, ObjectState.InWork, null, null),
            new[]
            {
                new StoredComponent(StandardKey("Болт"), 1, "Болт.m3d"),
                new StoredComponent(StandardKey("Болт"), 2, "Болт.m3d")
            });

        var incoming = new[]
        {
            new ComponentWriteModel("Болт.m3d", 1),
            new ComponentWriteModel("Болт.m3d", 1)
        };

        existing.HasSameComponentsAs(incoming).Should().BeFalse();
    }

    [Fact]
    public void У_сборки_отсутствие_массы_не_считается_изменением()
    {
        var existing = Stored(Assembly("РДЦЛ.304112.300", "Вал в сборе",
            new CadComponent("Вал.m3d", 1)), ObjectState.InWork);

        VersionPlanner.Plan(
            Assembly("РДЦЛ.304112.300", "Вал в сборе", new CadComponent("Вал.m3d", 1)),
            existing).Action.Should().Be(ImportAction.Unchanged);
    }

    [Fact]
    public void У_детали_отсутствие_массы_значимо()
    {
        var existing = Stored(Part("РДЦЛ.304112.904", "Прокладка", null), ObjectState.InWork);

        VersionPlanner.Plan(Part("РДЦЛ.304112.904", "Прокладка", 0.01m), existing)
            .Action.Should().Be(ImportAction.UpdateInPlace);
    }

    [Fact]
    public void Изменение_материала_обновляет_рабочую_версию()
    {
        var existing = Stored(Part("РДЦЛ.304112.301", "Вал", 3.42m), ObjectState.InWork);
        var changed = Part("РДЦЛ.304112.301", "Вал", 3.42m, material: "Сталь 40Х");

        VersionPlanner.Plan(changed, existing).Action.Should().Be(ImportAction.UpdateInPlace);
    }

    [Fact]
    public void Изменение_массы_меньше_грамма_не_считается_изменением()
    {
        // Допуск сравнения — один грамм: разница в 0,1 мг это шум формата
        var existing = Stored(Part("РДЦЛ.304112.301", "Вал", 3.42m), ObjectState.InWork);
        var changed = Part("РДЦЛ.304112.301", "Вал", 3.4200001m);

        VersionPlanner.Plan(changed, existing).Action.Should().Be(ImportAction.Unchanged);
    }

    [Fact]
    public void Объект_без_текущей_версии_получает_новую_версию()
    {
        // Все версии аннулированы — объект есть, но редактировать нечего
        var existing = new StoredObject(
            1,
            ObjectKey.From(ObjectType.Part, "РДЦЛ.304112.301", "Вал"),
            2,
            null,
            Array.Empty<StoredComponent>());

        var plan = VersionPlanner.Plan(Part("РДЦЛ.304112.301", "Вал", 3.42m), existing);

        plan.Action.Should().Be(ImportAction.NewVersion);
        plan.VersionNo.Should().Be(3);
    }

    [Fact]
    public void Планирование_для_всего_набора_сопоставляет_объекты_по_ключу()
    {
        var documents = new[]
        {
            Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m),
            Part("РДЦЛ.304112.302", "Колесо зубчатое", 5.86m)
        };

        var stored = new[]
        {
            Stored(documents[0], ObjectState.Approved)
        };

        var plan = VersionPlanner.PlanAll(documents, stored);

        plan.Should().HaveCount(2);
        plan[0].Action.Should().Be(ImportAction.Unchanged);
        plan[1].Action.Should().Be(ImportAction.Create);
    }

    // Снимок сохранённого объекта — как его отдаёт хранилище
    private static StoredObject Stored(CadDocument document, ObjectState state, int maxVersionNo = 1)
    {
        StoredVersion version = new StoredVersion(
            10,
            maxVersionNo,
            state,
            document.GetMaterial(),
            document.GetMassKg());

        var components = new List<StoredComponent>();

        for (int i = 0; i < document.Components.Count; i++)
        {
            CadComponent component = document.Components[i];
            components.Add(new StoredComponent(
                StandardKey(component.File), component.Count, component.File));
        }

        return new StoredObject(1, document.ToKey(), maxVersionNo, version, components);
    }

    // Ключ стандартного изделия — по имени файла без расширения
    private static ObjectKey StandardKey(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        return ObjectKey.From(ObjectType.StandardPart, null, name);
    }

    private static CadDocument Part(
        string designation, string name, decimal? mass, string? material = "Сталь 45")
    {
        return new CadDocument(
            name + ".m3d",
            1,
            ObjectType.Part,
            name,
            name + ".m3d",
            "Part",
            designation,
            new CadProperties(material, mass));
    }

    private static CadDocument Assembly(
        string designation, string name, params CadComponent[] components)
    {
        return new CadDocument(
            name + ".a3d",
            1,
            ObjectType.Assembly,
            name,
            name + ".a3d",
            "Assembly",
            designation,
            null,
            components);
    }
}