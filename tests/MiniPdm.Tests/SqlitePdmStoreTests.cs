using FluentAssertions;
using Microsoft.Data.Sqlite;
using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Persistence;
using MiniPdm.Data.Sqlite;

namespace MiniPdm.Tests;

// Тесты SQLite-хранилища на настоящей базе во временном файле.
// Каждый тест получает свою базу.
public sealed class SqlitePdmStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "minipdm-store-" + Guid.NewGuid().ToString("N") + ".db");

    private readonly SqlitePdmStore _store;

    public SqlitePdmStoreTests()
    {
        _store = new SqlitePdmStore(_databasePath);
    }

    public void Dispose()
    {
        // Windows держит файл открытым, пока живы соединения
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Инициализация_идемпотентна_и_база_пуста()
    {
        await _store.InitializeAsync();
        await _store.InitializeAsync();

        (await _store.GetAllAsync()).Should().BeEmpty();
        (await _store.ListAsync()).Should().BeEmpty();
        (await _store.GetRootsAsync()).Should().BeEmpty();
        (await _store.SearchAsync("болт")).Should().BeEmpty();
        (await _store.GetDetailsAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task Первичный_импорт_создаёт_объекты_версии_и_связи()
    {
        await _store.InitializeAsync();

        var assemblyKey = ObjectKey.From(ObjectType.Assembly, "РДЦЛ.100.001", "Редуктор");
        var partKey = ObjectKey.From(ObjectType.Part, "РДЦЛ.100.002", "Вал");

        var assembly = Model(
            assemblyKey, "Редуктор",
            components: new[] { new ComponentWriteModel("Вал.m3d", 2) },
            sourceFile: "Редуктор.m3d");

        var part = Model(
            partKey, "Вал",
            material: "Сталь 45",
            massKg: 1.5m,
            sourceFile: "Вал.m3d");

        ImportApplyResult result = await _store.ApplyImportAsync(
            new[] { assembly, part }, Log());

        result.Created.Should().Be(2);
        result.GetTotal().Should().Be(2);

        IReadOnlyList<StoredObject> objects = await _store.GetAllAsync();
        objects.Should().HaveCount(2);

        StoredObject storedAssembly = SingleByKey(objects, assemblyKey);
        storedAssembly.MaxVersionNo.Should().Be(1);
        storedAssembly.CurrentVersion.Should().NotBeNull();
        storedAssembly.CurrentVersion!.State.Should().Be(ObjectState.InWork);
        storedAssembly.Components.Should().ContainSingle();
        storedAssembly.Components[0].Quantity.Should().Be(2);
        storedAssembly.Components[0].ChildFile.Should().Be("Вал.m3d");
        storedAssembly.Components[0].ChildName.Should().Be("Вал");

        StoredObject storedPart = SingleByKey(objects, partKey);
        storedPart.CurrentVersion!.Material.Should().Be("Сталь 45");
        storedPart.CurrentVersion.MassKg.Should().Be(1.5m);
    }

    [Fact]
    public async Task Повторный_импорт_без_изменений_ничего_не_пишет()
    {
        await _store.InitializeAsync();

        var key = ObjectKey.From(ObjectType.Part, "РДЦЛ.100.003", "Кронштейн");
        await _store.ApplyImportAsync(
            new[] { Model(key, "Кронштейн", massKg: 0.2m, sourceFile: "Кронштейн.m3d") },
            Log());

        long objectId = (await _store.ListAsync())[0].Id;
        long versionId = (await _store.GetDetailsAsync(objectId))!.CurrentVersion!.Id;

        ImportApplyResult second = await _store.ApplyImportAsync(
            new[]
            {
                Model(
                    key, "Кронштейн",
                    action: ImportAction.Unchanged,
                    versionNo: 1,
                    massKg: 0.2m,
                    sourceFile: "Кронштейн.m3d",
                    sourceVersionId: versionId)
            },
            Log());

        second.Unchanged.Should().Be(1);
        second.Created.Should().Be(0);

        // Версия та же: неизменённый объект не породил ни новой версии, ни правок
        PdmObjectDetails details = (await _store.GetDetailsAsync(objectId))!;
        details.AllVersions.Should().ContainSingle();
        details.CurrentVersion!.Id.Should().Be(versionId);
        details.CurrentVersion.MassKg.Should().Be(0.2m);
    }

    [Fact]
    public async Task Изменения_в_версии_в_работе_обновляются_на_месте()
    {
        await _store.InitializeAsync();

        var key = ObjectKey.From(ObjectType.Part, "РДЦЛ.100.004", "Крышка");
        await _store.ApplyImportAsync(
            new[] { Model(key, "Крышка", material: "АК12", massKg: 1.0m, sourceFile: "Крышка.m3d") },
            Log());

        long objectId = (await _store.ListAsync())[0].Id;
        long versionId = (await _store.GetDetailsAsync(objectId))!.CurrentVersion!.Id;

        ImportApplyResult second = await _store.ApplyImportAsync(
            new[]
            {
                Model(
                    key, "Крышка",
                    action: ImportAction.UpdateInPlace,
                    versionNo: 1,
                    material: "АК12М1",
                    massKg: 1.1m,
                    sourceFile: "Крышка.m3d",
                    sourceVersionId: versionId)
            },
            Log());

        second.UpdatedInPlace.Should().Be(1);

        PdmObjectDetails details = (await _store.GetDetailsAsync(objectId))!;
        details.AllVersions.Should().ContainSingle();
        details.CurrentVersion!.Id.Should().Be(versionId);
        details.CurrentVersion.Material.Should().Be("АК12М1");
        details.CurrentVersion.MassKg.Should().Be(1.1m);
    }

    [Fact]
    public async Task После_утверждения_изменение_создаёт_новую_версию()
    {
        await _store.InitializeAsync();

        var key = ObjectKey.From(ObjectType.Part, "РДЦЛ.100.005", "Вал вторичный");
        await _store.ApplyImportAsync(
            new[] { Model(key, "Вал вторичный", massKg: 2.0m, sourceFile: "Вал2.m3d") },
            Log());

        long objectId = (await _store.ListAsync())[0].Id;
        PdmObjectDetails before = (await _store.GetDetailsAsync(objectId))!;
        long firstVersionId = before.CurrentVersion!.Id;

        StateTransitions.CanTransition(before.CurrentVersion.State, ObjectState.Approved)
            .Should().BeTrue();

        await _store.ChangeVersionStateAsync(firstVersionId, ObjectState.Approved, default);
        await _store.RefreshCurrentVersionAsync(objectId, default);

        ImportApplyResult second = await _store.ApplyImportAsync(
            new[]
            {
                Model(
                    key, "Вал вторичный",
                    action: ImportAction.NewVersion,
                    versionNo: 2,
                    massKg: 2.2m,
                    sourceFile: "Вал2.m3d")
            },
            Log());

        second.NewVersions.Should().Be(1);

        PdmObjectDetails after = (await _store.GetDetailsAsync(objectId))!;
        after.AllVersions.Should().HaveCount(2);
        after.CurrentVersion!.VersionNo.Should().Be(2);
        after.CurrentVersion.State.Should().Be(ObjectState.InWork);
        after.CurrentVersion.MassKg.Should().Be(2.2m);
        after.AllVersions[0].State.Should().Be(ObjectState.Approved);
        after.AllVersions[0].MassKg.Should().Be(2.0m);
        after.AllVersions[0].Id.Should().Be(firstVersionId);

        StoredObject stored = (await _store.GetAllAsync())[0];
        stored.MaxVersionNo.Should().Be(2);
        stored.GetCurrentVersionNo().Should().Be(2);
    }

    [Fact]
    public async Task Стандартное_изделие_находится_по_разному_регистру()
    {
        await _store.InitializeAsync();

        // В ключе наименование нормализовано (нижний регистр),
        // в базу пишем как в документе.
        var key = ObjectKey.From(ObjectType.StandardPart, null, "Подшипник 208");
        await _store.ApplyImportAsync(
            new[] { Model(key, "Подшипник 208", massKg: 0.3m, sourceFile: "СП.m3d") },
            Log());

        var otherKey = ObjectKey.From(ObjectType.StandardPart, null, "ПОДШИПНИК  208");
        ImportApplyResult second = await _store.ApplyImportAsync(
            new[]
            {
                Model(
                    otherKey, "ПОДШИПНИК 208",
                    action: ImportAction.UpdateInPlace,
                    versionNo: 1,
                    massKg: 0.35m,
                    sourceFile: "СП.m3d")
            },
            Log());

        second.UpdatedInPlace.Should().Be(1);

        IReadOnlyList<PdmObjectSummary> found = await _store.SearchAsync("Подшипник");
        found.Should().ContainSingle();
        found[0].Type.Should().Be(ObjectType.StandardPart);
        found[0].Name.Should().Be("Подшипник 208");

        PdmObjectDetails details = (await _store.GetDetailsAsync(found[0].Id))!;
        details.CurrentVersion!.MassKg.Should().Be(0.35m);
    }

    [Fact]
    public async Task Ошибочная_запись_откатывается_целиком()
    {
        await _store.InitializeAsync();

        // Первая модель валидна, вторая ссылается на несуществующий файл:
        // после отката в базе не должно остаться следов первой.
        var valid = Model(
            ObjectKey.From(ObjectType.Part, "РДЦЛ.100.006", "Деталь"),
            "Деталь",
            sourceFile: "Деталь.m3d");

        var broken = Model(
            ObjectKey.From(ObjectType.Assembly, "РДЦЛ.100.007", "Сборка"),
            "Сборка",
            components: new[] { new ComponentWriteModel("Нет.m3d", 1) },
            sourceFile: "Сборка.m3d");

        Func<Task> act = async () => await _store.ApplyImportAsync(
            new[] { valid, broken }, Log());

        await act.Should().ThrowAsync<InvalidOperationException>();

        (await _store.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Дерево_состава_строится_рекурсивным_запросом()
    {
        await _store.InitializeAsync();

        // А (корень) содержит Б ×2 и В ×3; Б содержит Г ×4
        var a = ObjectKey.From(ObjectType.Assembly, "А.001", "Ассамблея");
        var b = ObjectKey.From(ObjectType.Assembly, "Б.001", "Узел б");
        var c = ObjectKey.From(ObjectType.Part, "В.001", "Деталь в");
        var d = ObjectKey.From(ObjectType.Part, "Г.001", "Деталь г");

        await _store.ApplyImportAsync(
            new[]
            {
                Model(a, "Ассамблея",
                    components: new[]
                    {
                        new ComponentWriteModel("Б.m3d", 2),
                        new ComponentWriteModel("В.m3d", 3)
                    },
                    sourceFile: "А.m3d"),
                Model(b, "Узел б",
                    components: new[] { new ComponentWriteModel("Г.m3d", 4) },
                    sourceFile: "Б.m3d"),
                Model(c, "Деталь в", massKg: 1m, sourceFile: "В.m3d"),
                Model(d, "Деталь г", massKg: 0.5m, sourceFile: "Г.m3d")
            },
            Log());

        long rootId = (await _store.GetRootsAsync())[0].Id;
        IReadOnlyList<BomTreeRow> rows =
            await _store.GetBomTreeRowsAsync(rootId, 64, default);

        rows.Should().HaveCount(4);

        // Ищем нужные строки вручную, без LINQ-лямбд
        BomTreeRow? root = FindRow(rows, r => r.Level == 0);
        root.Should().NotBeNull();
        root!.ObjectId.Should().Be(rootId);
        root.Quantity.Should().Be(1);
        root.ParentObjectId.Should().BeNull();

        BomTreeRow? bRow = FindRowByDesignation(rows, "Б.001");
        bRow.Should().NotBeNull();
        bRow!.Level.Should().Be(1);
        bRow.Quantity.Should().Be(2);
        bRow.EdgeQuantity.Should().Be(2);

        BomTreeRow? dRow = FindRowByDesignation(rows, "Г.001");
        dRow.Should().NotBeNull();
        dRow!.Level.Should().Be(2);
        dRow.Quantity.Should().Be(8);   // 2 × 4
        dRow.EdgeQuantity.Should().Be(4);
        dRow.PathIds.Should().HaveCount(3);
        dRow.Path.Should().Contain(" / ");

        HasAnyCycle(rows).Should().BeFalse();

        // Масса: 3 × 1 + (4 × 0.5) × 2 = 7
        var mass = BomCalculator.CalculateMass(rows);
        mass.IsComplete.Should().BeTrue();
        mass.TotalKg.Should().Be(7m);
    }

    [Fact]
    public async Task Циклическая_связь_помечается_и_не_раскрывается()
    {
        await _store.InitializeAsync();

        // А состоит из Б, Б ссылается обратно на А — замыкающее ребро
        await _store.ApplyImportAsync(
            new[]
            {
                Model(
                    ObjectKey.From(ObjectType.Assembly, "Ц.001", "А"),
                    "А",
                    components: new[] { new ComponentWriteModel("Б.m3d", 1) },
                    sourceFile: "А.m3d"),
                Model(
                    ObjectKey.From(ObjectType.Assembly, "Ц.002", "Б"),
                    "Б",
                    components: new[] { new ComponentWriteModel("А.m3d", 1) },
                    sourceFile: "Б.m3d")
            },
            Log());

        // Корней в смысле GetRootsAsync тут нет: на оба объекта кто-то ссылается
        long rootId = FindSummaryByDesignation(await _store.ListAsync(), "Ц.001").Id;

        IReadOnlyList<BomTreeRow> rows =
            await _store.GetBomTreeRowsAsync(rootId, 64, default);

        rows.Should().HaveCount(3);
        CountCycles(rows).Should().Be(1);

        BomTreeRow cycleRow = FindCycleRow(rows)!;
        cycleRow.Designation.Should().Be("Ц.001");

        var cycles = CycleDetector.FindCyclesInTree(rows);
        cycles.Should().ContainSingle();
    }

    [Fact]
    public async Task Поиск_экранирует_символы_like()
    {
        await _store.InitializeAsync();

        await _store.ApplyImportAsync(
            new[]
            {
                Model(
                    ObjectKey.From(ObjectType.Part, "Т.1%", "Процент"),
                    "Процент",
                    sourceFile: "1.m3d"),
                Model(
                    ObjectKey.From(ObjectType.Part, "Т.2", "Обычный"),
                    "Обычный",
                    sourceFile: "2.m3d")
            },
            Log());

        // «%» в запросе — литеральный символ, а не маска
        IReadOnlyList<PdmObjectSummary> percent = await _store.SearchAsync("%");
        percent.Should().ContainSingle();
        percent[0].Designation.Should().Be("Т.1%");

        (await _store.SearchAsync("_")).Should().BeEmpty();
        (await _store.SearchAsync(null)).Should().BeEmpty();
        (await _store.SearchAsync("   ")).Should().BeEmpty();
        (await _store.SearchAsync("Обычный")).Should().ContainSingle();
    }

    [Fact]
    public async Task Список_корни_и_карточка_показывают_объекты()
    {
        await _store.InitializeAsync();

        var rootKey = ObjectKey.From(ObjectType.Assembly, "К.001", "Корень");
        await _store.ApplyImportAsync(
            new[]
            {
                Model(rootKey, "Корень",
                    components: new[] { new ComponentWriteModel("Деталь.m3d", 5) },
                    sourceFile: "Корень.m3d"),
                Model(
                    ObjectKey.From(ObjectType.Part, "К.002", "Деталь"),
                    "Деталь",
                    massKg: 0.1m,
                    sourceFile: "Деталь.m3d")
            },
            Log());

        IReadOnlyList<PdmObjectSummary> all = await _store.ListAsync();
        all.Should().HaveCount(2);

        // Корень один: деталь ссылается из корневой сборки
        IReadOnlyList<PdmObjectSummary> roots = await _store.GetRootsAsync();
        roots.Should().ContainSingle();
        roots[0].Designation.Should().Be("К.001");
        roots[0].Name.Should().Be("Корень");
        roots[0].VersionNo.Should().Be(1);
        roots[0].State.Should().Be(ObjectState.InWork);

        PdmObjectDetails details = (await _store.GetDetailsAsync(roots[0].Id))!;
        details.Name.Should().Be("Корень");
        details.AllVersions.Should().ContainSingle();
        details.Components.Should().ContainSingle();
        details.Components[0].ChildKey.Value.Should().Be("К.002");
        details.Components[0].ChildName.Should().Be("Деталь");
        details.Components[0].Quantity.Should().Be(5);

        (await _store.GetDetailsAsync(999999)).Should().BeNull();
    }

    [Fact]
    public async Task Аннулирование_уводит_текущую_версию_и_очистка_работает()
    {
        await _store.InitializeAsync();

        var key = ObjectKey.From(ObjectType.Part, "А.001", "Аннулируемая");
        await _store.ApplyImportAsync(
            new[] { Model(key, "Аннулируемая", massKg: 1m, sourceFile: "А.m3d") },
            Log());

        long objectId = (await _store.ListAsync())[0].Id;
        PdmObjectDetails before = (await _store.GetDetailsAsync(objectId))!;
        long versionId = before.CurrentVersion!.Id;

        await _store.ChangeVersionStateAsync(versionId, ObjectState.Cancelled, default);

        // Триггер базы сам перенёс указатель: версия единственная,
        // текущей у объекта больше нет
        PdmObjectDetails after = (await _store.GetDetailsAsync(objectId))!;
        after.CurrentVersion.Should().BeNull();
        after.AllVersions.Should().ContainSingle();
        after.AllVersions[0].State.Should().Be(ObjectState.Cancelled);

        // Явный пересчёт ничего не меняет — состояние уже согласовано
        await _store.RefreshCurrentVersionAsync(objectId, default);
        (await _store.GetDetailsAsync(objectId))!.CurrentVersion.Should().BeNull();

        await _store.DropAllAsync(default);

        (await _store.GetAllAsync()).Should().BeEmpty();
        (await _store.ListAsync()).Should().BeEmpty();
    }

    // Модель записи со значениями по умолчанию
    private static ObjectWriteModel Model(
        ObjectKey key,
        string name,
        ImportAction action = ImportAction.Create,
        int versionNo = 1,
        ObjectState state = ObjectState.InWork,
        string? material = null,
        decimal? massKg = null,
        IReadOnlyList<ComponentWriteModel>? components = null,
        string? sourceFile = null,
        long? sourceVersionId = null)
    {
        return new ObjectWriteModel(
            key,
            name,
            action,
            versionNo,
            state,
            material,
            massKg,
            components ?? new List<ComponentWriteModel>(),
            sourceFile ?? (name + ".m3d"),
            sourceVersionId);
    }

    private static ImportLogEntry Log()
    {
        return new ImportLogEntry(
            DateTimeOffset.Now, "тестовая папка", 2, 2, 0, 0, 2, 0, 0, 0);
    }

    // Один объект по ключу; если не один — тест упадёт
    private static StoredObject SingleByKey(IReadOnlyList<StoredObject> objects, ObjectKey key)
    {
        StoredObject? found = null;

        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].Key.Equals(key))
            {
                if (found != null)
                {
                    throw new InvalidOperationException(
                        "Объектов с ключом " + key + " больше одного");
                }

                found = objects[i];
            }
        }

        if (found == null)
        {
            throw new InvalidOperationException("Объект с ключом " + key + " не найден");
        }

        return found;
    }

    // Строка дерева по предикату (для одного теста)
    private static BomTreeRow? FindRow(
        IReadOnlyList<BomTreeRow> rows, Func<BomTreeRow, bool> predicate)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (predicate(rows[i]))
            {
                return rows[i];
            }
        }

        return null;
    }

    private static BomTreeRow? FindRowByDesignation(IReadOnlyList<BomTreeRow> rows, string designation)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Designation == designation)
            {
                return rows[i];
            }
        }

        return null;
    }

    private static PdmObjectSummary FindSummaryByDesignation(
        IReadOnlyList<PdmObjectSummary> items, string designation)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Designation == designation)
            {
                return items[i];
            }
        }

        throw new InvalidOperationException("Объект с обозначением " + designation + " не найден");
    }

    private static bool HasAnyCycle(IReadOnlyList<BomTreeRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsCycle)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountCycles(IReadOnlyList<BomTreeRow> rows)
    {
        int count = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsCycle)
            {
                count++;
            }
        }

        return count;
    }

    private static BomTreeRow? FindCycleRow(IReadOnlyList<BomTreeRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsCycle)
            {
                return rows[i];
            }
        }

        return null;
    }
}