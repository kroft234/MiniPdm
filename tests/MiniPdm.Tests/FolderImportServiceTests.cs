using FluentAssertions;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;
using NSubstitute;

namespace MiniPdm.Tests;

// Тесты сервиса импорта.
// Сервис общается с миром только через два порта — ICadExportReader и IPdmStore.
// Оба заменены заглушками: ни диска, ни базы не трогаем,
// но проверяем весь конвейер — чтение, проверку, план версий и запись.
public sealed class FolderImportServiceTests
{
    private const string FolderPath = "C:\\export";

    [Fact]
    public async Task Первый_импорт_планирует_создание_всех_документов()
    {
        var document = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m);

        var harness = new ImportHarness(new[] { Ok(document) }, new StoredObject[0]);

        ImportResult result = await harness.Service.ImportAsync(FolderPath);

        result.GetImported().Should().Be(1);
        result.GetRejected().Should().Be(0);
        result.Applied.Created.Should().Be(1);
        result.Applied.GetTotal().Should().Be(1);

        // Хранилище получило ровно одну запись — создать новую версию v1
        harness.Writes.Should().HaveCount(1);
        harness.Writes[0].Should().HaveCount(1);
        harness.Writes[0][0].Action.Should().Be(ImportAction.Create);
        harness.Writes[0][0].VersionNo.Should().Be(1);
        harness.Writes[0][0].SourceFile.Should().Be("Вал промежуточный.m3d");

        // Журнал импорта заполнен счётчиками
        ImportLogEntry log = harness.Logs.Single();
        log.SourceFolder.Should().Be(FolderPath);
        log.TotalFiles.Should().Be(1);
        log.Imported.Should().Be(1);
        log.Created.Should().Be(1);
        log.Rejected.Should().Be(0);
    }

    [Fact]
    public async Task Повторный_импорт_без_изменений_ничего_не_пишет()
    {
        var document = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m);

        // В базе объект уже есть с текущей версией «В работе»
        var harness = new ImportHarness(
            new[] { Ok(document) },
            new[] { Stored(document, ObjectState.InWork) });

        ImportResult result = await harness.Service.ImportAsync(FolderPath);

        result.GetImported().Should().Be(1);
        result.Applied.Created.Should().Be(0);
        result.Applied.UpdatedInPlace.Should().Be(0);
        result.Applied.NewVersions.Should().Be(0);
        result.Applied.Unchanged.Should().Be(1);

        // Запись вызвана, но с пустым списком — писать нечего
        harness.Writes.Should().ContainSingle();
        harness.Writes[0].Should().BeEmpty();
        harness.Logs.Single().Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task Изменение_утверждённой_версии_планирует_новую_версию()
    {
        CadDocument oldDoc = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m);
        var changed = Part("РДЦЛ.304112.301", "Вал промежуточный", 3.50m);

        var harness = new ImportHarness(
            new[] { Ok(changed) },
            new[] { Stored(oldDoc, ObjectState.Approved) });

        ImportResult result = await harness.Service.ImportAsync(FolderPath);

        result.Applied.NewVersions.Should().Be(1);

        ObjectWriteModel model = harness.Writes.Single().Single();
        model.Action.Should().Be(ImportAction.NewVersion);
        model.VersionNo.Should().Be(2);
    }

    [Fact]
    public async Task Битый_файл_отклоняется_и_в_план_не_попадает()
    {
        var part = Ok(Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m));
        var broken = CadReadResult.Failed("Отдушина.m3d", "Не удалось разобрать JSON");

        var harness = new ImportHarness(new[] { broken, part }, new StoredObject[0]);

        ImportResult result = await harness.Service.ImportAsync(FolderPath);

        result.GetImported().Should().Be(1);
        result.GetRejected().Should().Be(1);

        harness.Writes.Single().Should().HaveCount(1);
        harness.Logs.Single().TotalFiles.Should().Be(2);
        harness.Logs.Single().Rejected.Should().Be(1);
    }

    [Fact]
    public async Task Каскад_отклонения_убирает_сборку_и_из_плана()
    {
        var broken = CadReadResult.Failed("Насос шестерённый НШ-10.m3d",
            "Не удалось разобрать JSON");
        var assembly = Ok(Assembly("РДЦЛ.304112.100", "Насос в сборе",
            new CadComponent("Насос шестерённый НШ-10.m3d", 1)));

        var harness = new ImportHarness(new[] { broken, assembly }, new StoredObject[0]);

        ImportResult result = await harness.Service.ImportAsync(FolderPath);

        // Принятых нет — план пуст, запись вызвана с пустым списком
        result.GetImported().Should().Be(0);
        result.GetRejected().Should().Be(2);

        harness.Writes.Should().ContainSingle();
        harness.Writes[0].Should().BeEmpty();
    }

    [Fact]
    public async Task Пустой_путь_останавливает_импорт_до_чтения()
    {
        var harness = new ImportHarness(
            new[] { Ok(Part("РДЦЛ.304112.301", "Вал", 3.42m)) },
            new StoredObject[0]);

        Func<Task> act = () => harness.Service.ImportAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Reader.DidNotReceiveWithAnyArgs().ReadFolderAsync(default, default);
        harness.Store.DidNotReceiveWithAnyArgs().GetAllAsync(default);
    }

    [Fact]
    public async Task Прогресс_выдаёт_этапы_в_порядке_выполнения()
    {
        var harness = new ImportHarness(
            new[] { Ok(Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m)) },
            new StoredObject[0]);

        var stages = new List<string>();
        harness.Service.ProgressChanged += progress => stages.Add(progress.Stage);

        await harness.Service.ImportAsync(FolderPath);

        stages.Should().ContainInOrder(
            "Чтение документов",
            "Проверка документов",
            "Планирование версий",
            "Запись в базу",
            "Готово");
    }

    [Fact]
    public async Task Сборка_планируется_вместе_со_своим_составом()
    {
        var cover = Part("РДЦЛ.304112.701", "Крышка подшипника", 1.2m);
        var gasket = Part("РДЦЛ.304112.702", "Прокладка регулировочная", 0.03m);
        var assembly = Assembly("РДЦЛ.304112.500", "Крышка подшипника в сборе",
            new CadComponent("Крышка подшипника.m3d", 1),
            new CadComponent("Прокладка регулировочная.m3d", 4));

        var harness = new ImportHarness(
            new[] { Ok(cover), Ok(gasket), Ok(assembly) },
            new StoredObject[0]);

        await harness.Service.ImportAsync(FolderPath);

        // Три новых объекта, у сборки в записи — оба её компонента
        IReadOnlyList<ObjectWriteModel> firstWrite = harness.Writes.Single();

        ObjectWriteModel write = null;
        for (int i = 0; i < firstWrite.Count; i++)
        {
            if (firstWrite[i].SourceFile == "Крышка подшипника в сборе.a3d")
            {
                write = firstWrite[i];
                break;
            }
        }

        write.Should().NotBeNull();
        write!.Action.Should().Be(ImportAction.Create);
        write.Components.Should().HaveCount(2);
        write.Components[0].ChildFile.Should().Be("Крышка подшипника.m3d");
        write.Components[0].Quantity.Should().Be(1);
        write.Components[1].ChildFile.Should().Be("Прокладка регулировочная.m3d");
        write.Components[1].Quantity.Should().Be(4);
    }

    // Связка «сервис + заглушки»: читатель подменён в памяти,
    // хранилище — записью полученных моделей.
    private sealed class ImportHarness
    {
        public ImportHarness(
            IReadOnlyList<CadReadResult> reads,
            IReadOnlyList<StoredObject> stored)
        {
            Reader = Substitute.For<ICadExportReader>();
            Reader.ReadFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(reads.ToList());

            Store = Substitute.For<IPdmStore>();
            Store.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns(stored.ToList());

            // Заглушка «пишет», как настоящее хранилище:
            // считает модели по действиям, но ничего не сохраняет.
            Store.ApplyImportAsync(
                    Arg.Do<IReadOnlyList<ObjectWriteModel>>(models => Writes.Add(models.ToList())),
                    Arg.Do<ImportLogEntry>(log => Logs.Add(log)),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo => BuildApplyResult(callInfo.Arg<IReadOnlyList<ObjectWriteModel>>()));

            Service = new FolderImportService(Reader, Store);
        }

        public FolderImportService Service { get; }

        public ICadExportReader Reader { get; }

        public IPdmStore Store { get; }

        // Всё, что хранилище получало на запись
        public List<IReadOnlyList<ObjectWriteModel>> Writes { get; } = new List<IReadOnlyList<ObjectWriteModel>>();

        public List<ImportLogEntry> Logs { get; } = new List<ImportLogEntry>();

        // Считает модели по действиям — как ответило бы настоящее хранилище
        private static ImportApplyResult BuildApplyResult(IReadOnlyList<ObjectWriteModel> models)
        {
            int created = 0;
            int updated = 0;
            int newVersions = 0;

            for (int i = 0; i < models.Count; i++)
            {
                ObjectWriteModel model = models[i];

                if (model.Action == ImportAction.Create)
                {
                    created++;
                }

                if (model.Action == ImportAction.UpdateInPlace)
                {
                    updated++;
                }

                if (model.Action == ImportAction.NewVersion)
                {
                    newVersions++;
                }
            }

            return new ImportApplyResult(created, updated, newVersions, 0);
        }
    }

    private static CadReadResult Ok(CadDocument document)
    {
        return CadReadResult.Ok(document);
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
            new CadProperties(null, null),
            components);
    }

    // Снимок сохранённого объекта для документа — как его отдаёт база
    private static StoredObject Stored(
        CadDocument document, ObjectState state, int maxVersionNo = 1)
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
                ObjectKey.From(ObjectType.Part, null, component.File),
                component.Count,
                component.File));
        }

        return new StoredObject(1, document.ToKey(), maxVersionNo, version, components);
    }
}