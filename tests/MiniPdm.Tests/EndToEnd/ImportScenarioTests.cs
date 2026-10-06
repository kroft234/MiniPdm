using FluentAssertions;
using MiniPdm.Cad.FileSystem;
using MiniPdm.Cad.Json;
using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;
using MiniPdm.Core.Validation;
using MiniPdm.Data.Sqlite;

namespace MiniPdm.Tests.EndToEnd;

// Сквозной тест на настоящей папке экспорта.
// Сценарий: первичный импорт → повторный (без изменений) → утверждение → импорт изменённых данных.
public sealed class ImportScenarioTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "minipdm-e2e-" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Полный_сценарий_импорта_и_повторного_изменения()
    {
        string v1 = ExportFolder("v1");
        string v2 = ExportFolder("v2");

        var store = new SqlitePdmStore(_databasePath);
        await store.InitializeAsync();

        var service = new FolderImportService(
            new FileSystemCadExportReader(new JsonCadDocumentReader()), store);

        // Шаг 1. Первичный импорт
        ImportResult first = await service.ImportAsync(v1);

        first.Validation.GetTotalFiles().Should().Be(45);
        first.Validation.GetAccepted().Should().Be(38);
        first.Validation.GetRejected().Should().Be(7);
        first.Applied.Created.Should().Be(38);
        first.Applied.Unchanged.Should().Be(0);

        AssertRejections(first);

        // Шаг 2. Повторный импорт тех же данных
        ImportResult second = await service.ImportAsync(v1);

        second.Validation.GetAccepted().Should().Be(38);
        second.Applied.Created.Should().Be(0);
        second.Applied.UpdatedInPlace.Should().Be(0);
        second.Applied.NewVersions.Should().Be(0);

        // Неизменённые не пишутся в базу — их число берётся из плана
        second.Applied.Unchanged.Should().Be(38);

        // Шаг 3. Утверждение всех версий
        IReadOnlyList<PdmObjectSummary> all = await store.ListAsync(default);

        for (int i = 0; i < all.Count; i++)
        {
            PdmObjectSummary summary = all[i];
            PdmObjectDetails details = (await store.GetDetailsAsync(summary.Id, default))!;
            StoredVersion version = details.CurrentVersion!;

            version.State.Should().Be(ObjectState.InWork);
            StateTransitions.CanTransition(version.State, ObjectState.Approved).Should().BeTrue();

            await store.ChangeVersionStateAsync(version.Id, ObjectState.Approved, default);
            await store.RefreshCurrentVersionAsync(summary.Id, default);
        }

        // Утверждённая версия неизменяема: масса та же
        PdmObjectSummary locked = (await store.SearchAsync("Колесо зубчатое", default)).Single();
        PdmObjectDetails lockedDetails = (await store.GetDetailsAsync(locked.Id, default))!;
        lockedDetails.CurrentVersion!.MassKg.Should().Be(5.86m);

        // Шаг 4. Импорт изменённых данных
        ImportResult third = await service.ImportAsync(v2);

        third.Validation.GetAccepted().Should().Be(38);
        third.Applied.Created.Should().Be(0);
        third.Applied.UpdatedInPlace.Should().Be(0);
        third.Applied.NewVersions.Should().Be(2);
        third.Applied.Unchanged.Should().Be(36);

        // Колесо зубчатое: новая версия
        PdmObjectSummary gear = (await store.SearchAsync("Колесо зубчатое", default)).Single();
        PdmObjectDetails gearDetails = (await store.GetDetailsAsync(gear.Id, default))!;

        gearDetails.CurrentVersion!.VersionNo.Should().Be(2);
        gearDetails.CurrentVersion.State.Should().Be(ObjectState.InWork);
        gearDetails.CurrentVersion.MassKg.Should().Be(5.92m);
        gearDetails.AllVersions.Should().HaveCount(2);

        // Одна версия «Утверждено», вторая «В работе»
        bool hasApproved = false;
        bool hasInWork = false;

        for (int i = 0; i < gearDetails.AllVersions.Count; i++)
        {
            ObjectState state = gearDetails.AllVersions[i].State;

            if (state == ObjectState.Approved)
            {
                hasApproved = true;
            }

            if (state == ObjectState.InWork)
            {
                hasInWork = true;
            }
        }

        hasApproved.Should().BeTrue();
        hasInWork.Should().BeTrue();

        // Крышка подшипника: тоже новая версия
        PdmObjectSummary cover =
            (await store.SearchAsync("Крышка подшипника в сборе", default)).Single();
        PdmObjectDetails coverDetails = (await store.GetDetailsAsync(cover.Id, default))!;
        coverDetails.CurrentVersion!.VersionNo.Should().Be(2);

        // Прокладка: количество 4 × 4 = 16
        long reducerId = (await store.SearchAsync("Редуктор", default))
            .First(r => r.Designation == "РДЦЛ.304112.000").Id;
        IReadOnlyList<BomTreeRow> rows = await store.GetBomTreeRowsAsync(reducerId, 64, default);

        IReadOnlyList<SpecificationRow> spec = BomCalculator.BuildSpecification(rows);

        SpecificationRow? gasket = null;
        for (int i = 0; i < spec.Count; i++)
        {
            if (spec[i].Designation == "РДЦЛ.304112.502")
            {
                gasket = spec[i];
                break;
            }
        }

        gasket.Should().NotBeNull();
        gasket!.TotalQuantity.Should().Be(16);
        gasket.GetTotalMassKg().Should().Be(0.064m);

        // Масса редуктора после изменений
        MassResult mass = BomCalculator.CalculateMass(rows);
        mass.IsComplete.Should().BeTrue();
        mass.TotalKg.Should().Be(98.88m);
    }

    [Fact]
    public async Task Масса_сборки_без_массы_компонента_помечается_неполной()
    {
        var store = new SqlitePdmStore(_databasePath);
        await store.InitializeAsync();

        await ImportV1Async(store);

        // Маслоуказатель: прокладка без массы — полный расчёт невозможен
        PdmObjectSummary gauge =
            (await store.SearchAsync("Маслоуказатель в сборе", default)).Single();
        IReadOnlyList<BomTreeRow> rows =
            await store.GetBomTreeRowsAsync(gauge.Id, 64, default);

        MassResult mass = BomCalculator.CalculateMass(rows);

        mass.IsComplete.Should().BeFalse();

        // В списке нехватающих — прокладка
        bool foundGasket = false;
        for (int i = 0; i < mass.MissingMass.Count; i++)
        {
            if (mass.MissingMass[i].Name.Contains("Прокладка"))
            {
                foundGasket = true;
                break;
            }
        }

        foundGasket.Should().BeTrue();
    }

    [Fact]
    public async Task Циклическая_сборка_показывает_цикл_в_дереве()
    {
        var store = new SqlitePdmStore(_databasePath);
        await store.InitializeAsync();

        await ImportV1Async(store);

        PdmObjectSummary root =
            (await store.SearchAsync("Механизм переключения", default)).Single();
        IReadOnlyList<BomTreeRow> rows = await store.GetBomTreeRowsAsync(root.Id, 64, default);

        IReadOnlyList<CycleInfo> cycles = CycleDetector.FindCyclesInTree(rows);

        // Цикл не рвёт импорт, но виден в расчётах
        cycles.Should().ContainSingle();
        cycles[0].NodeIds.Should().HaveCount(2);
        cycles[0].GetDescription().Should().Contain("Рычаг переключения");
    }

    [Fact]
    public async Task Аннулированная_версия_перестаёт_быть_текущей()
    {
        var store = new SqlitePdmStore(_databasePath);
        await store.InitializeAsync();

        await ImportV1Async(store);

        PdmObjectSummary part = (await store.SearchAsync("Вал промежуточный", default))
            .First(r => r.Designation == "РДЦЛ.304112.301");

        PdmObjectDetails details = (await store.GetDetailsAsync(part.Id, default))!;
        long versionId = details.CurrentVersion!.Id;

        await store.ChangeVersionStateAsync(versionId, ObjectState.Cancelled, default);
        await store.RefreshCurrentVersionAsync(part.Id, default);

        PdmObjectDetails after = (await store.GetDetailsAsync(part.Id, default))!;

        // Единственная версия аннулирована — текущей нет
        after.CurrentVersion.Should().BeNull();

        // Такой объект не попадает в пересчёт состава
        PdmObjectSummary parent =
            (await store.SearchAsync("Вал промежуточный в сборе", default)).Single();
        IReadOnlyList<BomTreeRow> rows =
            await store.GetBomTreeRowsAsync(parent.Id, 64, default);

        // Ищем строку с аннулированной версией
        bool found = false;

        for (int i = 0; i < rows.Count; i++)
        {
            BomTreeRow row = rows[i];

            if (row.Designation == "РДЦЛ.304112.301" && row.VersionId == versionId)
            {
                found = true;
                break;
            }
        }

        found.Should().BeFalse();
    }

    // Общий импорт v1 для тестов ниже
    private static async Task ImportV1Async(SqlitePdmStore store)
    {
        var service = new FolderImportService(
            new FileSystemCadExportReader(new JsonCadDocumentReader()), store);

        await service.ImportAsync(ExportFolder("v1"));
    }

    // Проверка конкретных причин отклонения
    private static void AssertRejections(ImportResult result)
    {
        // Собираем причины всех отклонённых
        var reasons = new List<string>();

        for (int i = 0; i < result.Validation.Verdicts.Count; i++)
        {
            DocumentVerdict verdict = result.Validation.Verdicts[i];

            if (!verdict.GetIsRejected())
            {
                continue;
            }

            for (int j = 0; j < verdict.Issues.Count; j++)
            {
                reasons.Add(verdict.Issues[j].Reason);
            }
        }

        // Каждая причина проверяется отдельно
        ContainsReason(reasons, "РДЦЛ.30411.602", "6 цифр").Should().BeTrue(
            "пять цифр вместо шести в номере");

        ContainsReason(reasons, "латинские").Should().BeTrue(
            "латинская буква вместо кириллической");

        ContainsReason(reasons, "Не удалось разобрать JSON").Should().BeTrue(
            "оборванный JSON-файл");

        ContainsReason(reasons, "Насос шестерённый НШ-10.m3d", "отсутствующий").Should().BeTrue(
            "ссылка на отсутствующий файл");

        ContainsReason(reasons, "Пробка сливная.m3d", "положительным").Should().BeTrue(
            "нулевое количество экземпляров");

        ContainsReason(reasons, "РДЦЛ.304112.710", "Шайба").Should().BeTrue(
            "дубликат обозначения");

        // Предупреждения: цикл и отсутствующая масса
        var warnings = new List<string>();

        for (int i = 0; i < result.Validation.Verdicts.Count; i++)
        {
            DocumentVerdict verdict = result.Validation.Verdicts[i];

            if (!verdict.GetIsAccepted() || !verdict.GetHasWarnings())
            {
                continue;
            }

            for (int j = 0; j < verdict.Issues.Count; j++)
            {
                if (verdict.Issues[j].Severity == IssueSeverity.Warning)
                {
                    warnings.Add(verdict.Issues[j].Reason);
                }
            }
        }

        ContainsReason(warnings, "циклическая ссылка").Should().BeTrue();
        ContainsReason(warnings, "не указана").Should().BeTrue();
    }

    // Есть ли среди строк такая, что содержит все указанные подстроки
    private static bool ContainsReason(List<string> reasons, params string[] parts)
    {
        for (int i = 0; i < reasons.Count; i++)
        {
            string reason = reasons[i];
            bool allParts = true;

            for (int j = 0; j < parts.Length; j++)
            {
                if (!reason.Contains(parts[j]))
                {
                    allParts = false;
                    break;
                }
            }

            if (allParts)
            {
                return true;
            }
        }

        return false;
    }

    // Папка с тестовыми данными рядом с тестовой сборкой
    private static string ExportFolder(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "TestData", name);
    }
}