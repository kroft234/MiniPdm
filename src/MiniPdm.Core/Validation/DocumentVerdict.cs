using MiniPdm.Core.Cad;

namespace MiniPdm.Core.Validation;

// Вердикт по одному документу: принят, отклонён или принят с предупреждением.
public sealed class DocumentVerdict
{
    public DocumentVerdict(
        string fileName,
        CadDocument? document,
        IReadOnlyList<ImportIssue> issues,
        string? rejectedBecauseOf)
    {
        FileName = fileName;
        Document = document;
        Issues = issues;
        RejectedBecauseOf = rejectedBecauseOf;
    }

    public string FileName { get; }

    // Документ; null, если файл не прочитался или отклонён.
    public CadDocument? Document { get; }

    public IReadOnlyList<ImportIssue> Issues { get; }

    // Имя файла-причины, если отклонён каскадом вверх по дереву.
    public string? RejectedBecauseOf { get; }

    // Есть ли ошибка — из-за неё документ не импортируется.
    public bool GetIsRejected()
    {
        for (int i = 0; i < Issues.Count; i++)
        {
            if (Issues[i].Severity == IssueSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    // Есть ли предупреждения.
    public bool GetHasWarnings()
    {
        for (int i = 0; i < Issues.Count; i++)
        {
            if (Issues[i].Severity == IssueSeverity.Warning)
            {
                return true;
            }
        }

        return false;
    }

    // Принят = нет ошибок.
    public bool GetIsAccepted()
    {
        return !GetIsRejected();
    }

    // Это сборка? (И документ не null.)
    public bool GetIsAssembly()
    {
        if (Document == null)
        {
            return false;
        }

        return Document.GetIsAssembly();
    }

    // Создать принятый вердикт.
    public static DocumentVerdict Accept(CadDocument document, IReadOnlyList<ImportIssue> issues)
    {
        return new DocumentVerdict(document.FileName, document, issues, null);
    }

    // Создать отклонённый вердикт.
    public static DocumentVerdict Reject(
        string fileName,
        IReadOnlyList<ImportIssue> issues,
        string? becauseOf = null)
    {
        return new DocumentVerdict(fileName, null, issues, becauseOf);
    }
}

// Итог разбора папки: вердикты по всем файлам и счётчики.
public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<DocumentVerdict> verdicts)
    {
        Verdicts = verdicts;
    }

    public IReadOnlyList<DocumentVerdict> Verdicts { get; }

    public int GetTotalFiles()
    {
        return Verdicts.Count;
    }

    // Принято (без ошибок).
    public int GetAccepted()
    {
        int count = 0;
        for (int i = 0; i < Verdicts.Count; i++)
        {
            if (Verdicts[i].GetIsAccepted())
            {
                count++;
            }
        }

        return count;
    }

    // Отклонено.
    public int GetRejected()
    {
        int count = 0;
        for (int i = 0; i < Verdicts.Count; i++)
        {
            if (Verdicts[i].GetIsRejected())
            {
                count++;
            }
        }

        return count;
    }

    // Принято, но с предупреждениями.
    public int GetWithWarnings()
    {
        int count = 0;
        for (int i = 0; i < Verdicts.Count; i++)
        {
            if (Verdicts[i].GetIsAccepted() && Verdicts[i].GetHasWarnings())
            {
                count++;
            }
        }

        return count;
    }

    // Принято совсем без замечаний.
    public int GetAcceptedWithoutWarnings()
    {
        return GetAccepted() - GetWithWarnings();
    }

    // Вердикты принятых документов (в порядке чтения).
    public IReadOnlyList<DocumentVerdict> GetAcceptedDocuments()
    {
        var accepted = new List<DocumentVerdict>();

        for (int i = 0; i < Verdicts.Count; i++)
        {
            if (Verdicts[i].GetIsAccepted() && Verdicts[i].Document != null)
            {
                accepted.Add(Verdicts[i]);
            }
        }

        return accepted;
    }

    // Все проблемы всех файлов одним списком.
    public IReadOnlyList<ImportIssue> GetAllIssues()
    {
        var issues = new List<ImportIssue>();

        for (int i = 0; i < Verdicts.Count; i++)
        {
            for (int j = 0; j < Verdicts[i].Issues.Count; j++)
            {
                issues.Add(Verdicts[i].Issues[j]);
            }
        }

        return issues;
    }

    // Вердикт по имени файла; null — файла в наборе не было.
    public DocumentVerdict? ForFile(string fileName)
    {
        for (int i = 0; i < Verdicts.Count; i++)
        {
            if (string.Equals(Verdicts[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return Verdicts[i];
            }
        }

        return null;
    }
}