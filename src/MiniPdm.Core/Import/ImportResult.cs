using MiniPdm.Core.Persistence;
using MiniPdm.Core.Validation;

namespace MiniPdm.Core.Import;

// Итог импорта папки
public sealed class ImportResult
{
    public ImportResult(
        ValidationResult validation,
        ImportApplyResult applied,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt)
    {
        Validation = validation;
        Applied = applied;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
    }

    public ValidationResult Validation { get; }

    public ImportApplyResult Applied { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset FinishedAt { get; }

    // Сколько занял импорт
    public TimeSpan GetDuration()
    {
        return FinishedAt - StartedAt;
    }

    // Принято объектов (включая принятых с предупреждениями)
    public int GetImported()
    {
        return Validation.GetAccepted();
    }

    public int GetRejected()
    {
        return Validation.GetRejected();
    }

    public int GetWithWarnings()
    {
        return Validation.GetWithWarnings();
    }

    // Все проблемы отчёта одним списком
    public IReadOnlyList<ImportIssue> GetAllIssues()
    {
        return Validation.GetAllIssues();
    }
}

// Один шаг прогресса импорта для индикатора в интерфейсе
public sealed class ImportProgress
{
    public ImportProgress(string stage, int processed, int? total)
    {
        Stage = stage;
        Processed = processed;
        Total = total;
    }

    public string Stage { get; }

    public int Processed { get; }

    // Размер этапа известен не всегда: у чтения папки он выясняется уже после
    public int? Total { get; }

    // Процент выполнения
    public int? GetPercent()
    {
        if (Total == null || Total.Value <= 0)
        {
            return null;
        }

        // 100L — чтобы умножение шло в long и не переполнилось
        long percent = Processed * 100L / Total.Value;
        return (int)percent;
    }
}