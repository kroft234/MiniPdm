using MiniPdm.Core.Cad;
using MiniPdm.Core.Persistence;
using MiniPdm.Core.Validation;

namespace MiniPdm.Core.Import;

// Импорт папки с CAD-документами: чтение → проверка → план версий → запись в базу.
// Зависит только от интерфейсов, поэтому тестируется без диска и базы.
public sealed class FolderImportService
{
    private readonly ICadExportReader _exportReader;
    private readonly IPdmStore _store;

    public FolderImportService(ICadExportReader exportReader, IPdmStore store)
    {
        _exportReader = exportReader;
        _store = store;
    }

    // Событие прогресса для полоски загрузки в интерфейсе
    public event Action<ImportProgress>? ProgressChanged;

    public async Task<ImportResult> ImportAsync(string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Путь к папке не указан", nameof(folderPath));
        }

        DateTimeOffset startedAt = DateTimeOffset.Now;
        Report(new ImportProgress("Чтение документов", 0, null));

        // 1. Читаем все файлы. Битый файл — в отчёт, остальные читаются дальше.
        IReadOnlyList<CadReadResult> readResults = await _exportReader.ReadFolderAsync(folderPath, ct);

        // 2. Проверяем набор: дубликаты, битые ссылки, циклы в составах.
        Report(new ImportProgress("Проверка документов", 0, readResults.Count));
        ValidationResult validation = DocumentSetValidator.Validate(readResults);

        // 3. Составляем план: какие объекты создать, какие обновить, какие пропустить.
        Report(new ImportProgress("Планирование версий", 0, validation.GetAccepted()));
        IReadOnlyList<StoredObject> stored = await _store.GetAllAsync(ct);

        // Собираем документы, которые прошли проверку
        var documents = new List<CadDocument>();
        IReadOnlyList<DocumentVerdict> accepted = validation.GetAcceptedDocuments();
        for (int i = 0; i < accepted.Count; i++)
        {
            if (accepted[i].Document != null)
            {
                documents.Add(accepted[i].Document);
            }
        }

        IReadOnlyList<ObjectWriteModel> models = VersionPlanner.PlanAll(documents, stored);

        // Неизменённые объекты в базу не пишем — экономим запросы
        int plannedUnchanged = CountByAction(models, ImportAction.Unchanged);
        var writable = new List<ObjectWriteModel>();
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i].Action != ImportAction.Unchanged)
            {
                writable.Add(models[i]);
            }
        }

        // Записываем лог импорта
        var log = new ImportLogEntry(
            startedAt,
            folderPath,
            validation.GetTotalFiles(),
            validation.GetAccepted(),
            validation.GetRejected(),
            validation.GetWithWarnings(),
            CountByAction(models, ImportAction.Create),
            CountByAction(models, ImportAction.UpdateInPlace),
            CountByAction(models, ImportAction.NewVersion),
            plannedUnchanged);

        // 4. Записываем всё одной транзакцией (либо всё, либо ничего).
        Report(new ImportProgress("Запись в базу", 0, writable.Count));
        ImportApplyResult applied = await _store.ApplyImportAsync(writable, log, ct);

        DateTimeOffset finishedAt = DateTimeOffset.Now;
        Report(new ImportProgress("Готово", writable.Count, applied.GetTotal()));

        // Возвращаем итог, добавляя неизменённые к результату записи
        ImportApplyResult result = new ImportApplyResult(
            applied.Created,
            applied.UpdatedInPlace,
            applied.NewVersions,
            plannedUnchanged);

        return new ImportResult(validation, result, startedAt, finishedAt);
    }

    private void Report(ImportProgress progress)
    {
        ProgressChanged?.Invoke(progress);
    }

    private static int CountByAction(IReadOnlyList<ObjectWriteModel> models, ImportAction action)
    {
        int count = 0;
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i].Action == action)
            {
                count++;
            }
        }

        return count;
    }
}