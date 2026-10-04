using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Persistence;

// Запись журнала импорта: что и когда импортировано и что из этого вышло.
public sealed class ImportLogEntry
{
    public ImportLogEntry(
        DateTimeOffset startedAt,
        string sourceFolder,
        int totalFiles,
        int imported,
        int rejected,
        int withWarnings,
        int created,
        int updatedInPlace,
        int newVersions,
        int unchanged)
    {
        StartedAt = startedAt;
        SourceFolder = sourceFolder;
        TotalFiles = totalFiles;
        Imported = imported;
        Rejected = rejected;
        WithWarnings = withWarnings;
        Created = created;
        UpdatedInPlace = updatedInPlace;
        NewVersions = newVersions;
        Unchanged = unchanged;
    }

    public DateTimeOffset StartedAt { get; }

    public string SourceFolder { get; }

    public int TotalFiles { get; }

    public int Imported { get; }

    public int Rejected { get; }

    public int WithWarnings { get; }

    public int Created { get; }

    public int UpdatedInPlace { get; }

    public int NewVersions { get; }

    public int Unchanged { get; }
}

// Итог записи импорта, возвращаемый хранилищем.
public sealed class ImportApplyResult
{
    public ImportApplyResult(int created, int updatedInPlace, int newVersions, int unchanged)
    {
        Created = created;
        UpdatedInPlace = updatedInPlace;
        NewVersions = newVersions;
        Unchanged = unchanged;
    }

    public int Created { get; }

    public int UpdatedInPlace { get; }

    public int NewVersions { get; }

    public int Unchanged { get; }

    // Всего затронуто объектов.
    public int GetTotal()
    {
        return Created + UpdatedInPlace + NewVersions + Unchanged;
    }
}

// Порт хранилища
public interface IPdmStore
{
    Task InitializeAsync(CancellationToken ct);

    // Полный снимок базы для планирования версий.
    Task<IReadOnlyList<StoredObject>> GetAllAsync(CancellationToken ct);

    // Применяет импорт в одной транзакции: либо записано всё, либо ничего.
    Task<ImportApplyResult> ApplyImportAsync(
        IReadOnlyList<ObjectWriteModel> models,
        ImportLogEntry log,
        CancellationToken ct);

    // Плоский список узлов дерева состава одного объекта.
    // Уровень, путь и перемноженные количества считает SQL одним
    // рекурсивным запросом, а не циклом запросов из C#.
    Task<IReadOnlyList<BomTreeRow>> GetBomTreeRowsAsync(long objectId, int maxDepth, CancellationToken ct);

    // Карточка объекта: текущая версия, все версии и состав. null — объекта нет.
    Task<PdmObjectDetails?> GetDetailsAsync(long objectId, CancellationToken ct);

    // Поиск по обозначению или наименованию.
    Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string? query, CancellationToken ct);

    // Полный каталог объектов — то, что показывает интерфейс по умолчанию.
    Task<IReadOnlyList<PdmObjectSummary>> ListAsync(CancellationToken ct);

    // Корневые объекты — те, на которые никто не ссылается. С них начинается
    // просмотр дерева.
    Task<IReadOnlyList<PdmObjectSummary>> GetRootsAsync(CancellationToken ct);

    // Пересчёт текущей версии: текущей становится последняя неаннулированная.
    Task RefreshCurrentVersionAsync(long objectId, CancellationToken ct);

    // Смена состояния версии. Допустимость перехода проверяет StateTransitions
    // до вызова; хранилище только пишет.
    Task ChangeVersionStateAsync(long versionId, ObjectState state, CancellationToken ct);

    // Полная очистка базы: объекты, версии, состав, журнал. Нужна для
    // повторяемых тестов — сценарий «с нуля».
    Task DropAllAsync(CancellationToken ct);
}