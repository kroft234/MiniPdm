using MiniPdm.Core.Bom;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Validation;

// Проверка всего набора документов.
public static class DocumentSetValidator
{
    // Порядок: дубликаты имён → документ → обозначения → ссылки → каскад → циклы.
    public static ValidationResult Validate(IReadOnlyList<CadReadResult> readResults)
    {
        ArgumentNullException.ThrowIfNull(readResults);

        // Имя файла → результат чтения.
        var byFileName = new Dictionary<string, CadReadResult>(StringComparer.OrdinalIgnoreCase);
        var duplicateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < readResults.Count; i++)
        {
            CadReadResult read = readResults[i];

            if (byFileName.ContainsKey(read.FileName))
            {
                duplicateNames.Add(read.FileName);
            }
            else
            {
                byFileName.Add(read.FileName, read);
            }
        }

        // Дубликаты — заранее.
        Dictionary<string, List<string>> duplicateDesignations =
            FindDuplicateDesignations(byFileName);
        Dictionary<string, List<string>> duplicateStandardNames =
            FindDuplicateStandardPartNames(byFileName);

        // Вердикт по каждому файлу.
        var verdicts = new List<DocumentVerdict>();

        for (int i = 0; i < readResults.Count; i++)
        {
            CadReadResult read = readResults[i];

            // Имя-дубликат.
            if (duplicateNames.Contains(read.FileName))
            {
                var issues = new List<ImportIssue>();
                issues.Add(new ImportIssue(read.FileName, IssueSeverity.Error,
                    "В папке найдено несколько файлов с одинаковым именем"));

                verdicts.Add(DocumentVerdict.Reject(read.FileName, issues));
                continue;
            }

            // Файл не прочитался.
            if (read.Document == null)
            {
                var issues = new List<ImportIssue>();
                issues.Add(new ImportIssue(read.FileName, IssueSeverity.Error,
                    read.Error ?? "Не удалось прочитать документ"));

                verdicts.Add(DocumentVerdict.Reject(read.FileName, issues));
                continue;
            }

            CadDocument document = read.Document;

            verdicts.Add(BuildVerdict(
                document, byFileName, duplicateDesignations, duplicateStandardNames));
        }

        // Каскад: сборка, ссылающаяся на отклонённый файл, отклоняется тоже.
        ApplyCascade(verdicts);

        // Циклы — предупреждение.
        AddCycleWarnings(verdicts);

        return new ValidationResult(verdicts);
    }

    // Вердикт для одного документа.
    private static DocumentVerdict BuildVerdict(
        CadDocument document,
        Dictionary<string, CadReadResult> byFileName,
        Dictionary<string, List<string>> duplicateDesignations,
        Dictionary<string, List<string>> duplicateStandardNames)
    {
        var issues = new List<ImportIssue>(DocumentValidator.Validate(document));

        // Дубликат обозначения.
        if (duplicateDesignations.TryGetValue(document.FileName, out List<string>? desFiles))
        {
            // Собираем текст по частям — null + строка = строка, ничего не сломается.
            string designation = document.Designation ?? string.Empty;
            string filesText = string.Join(", ", desFiles);

            string message = "Обозначение «" + designation
                + "» совпадает с файлами: " + filesText;

            issues.Add(new ImportIssue(document.FileName, IssueSeverity.Error, message));
        }

        // Дубликат наименования стандартного изделия.
        if (duplicateStandardNames.TryGetValue(document.FileName, out List<string>? nameFiles))
        {
            string filesText = string.Join(", ", nameFiles);

            string message = "Наименование стандартного изделия «" + document.Name
                + "» совпадает с файлами: " + filesText;

            issues.Add(new ImportIssue(document.FileName, IssueSeverity.Error, message));
        }

        // Ссылки сборки на отсутствующие файлы.
        string? becauseOf = null;
        if (document.GetIsAssembly())
        {
            becauseOf = CheckMissingReferences(document, byFileName, issues);
        }

        if (HasError(issues))
        {
            return DocumentVerdict.Reject(document.FileName, issues, becauseOf);
        }

        return DocumentVerdict.Accept(document, WarningsOnly(issues));
    }

    // Проверяет ссылки. Возвращает первый отсутствующий файл или null.
    private static string? CheckMissingReferences(
        CadDocument document,
        Dictionary<string, CadReadResult> byFileName,
        List<ImportIssue> issues)
    {
        string? firstMissing = null;

        for (int i = 0; i < document.Components.Count; i++)
        {
            string file = document.Components[i].File;

            if (string.IsNullOrWhiteSpace(file))
            {
                continue;
            }

            if (!byFileName.ContainsKey(file))
            {
                issues.Add(new ImportIssue(document.FileName, IssueSeverity.Error,
                    "Ссылка на отсутствующий файл «" + file + "»", file));

                if (firstMissing == null)
                {
                    firstMissing = file;
                }
            }
        }

        return firstMissing;
    }

    // Каскадное отклонение вверх по дереву.
    private static void ApplyCascade(List<DocumentVerdict> verdicts)
    {
        var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < verdicts.Count; i++)
        {
            if (verdicts[i].GetIsRejected())
            {
                rejected.Add(verdicts[i].FileName);
            }
        }

        bool changed = true;

        while (changed)
        {
            changed = false;

            for (int i = 0; i < verdicts.Count; i++)
            {
                DocumentVerdict verdict = verdicts[i];

                if (!verdict.GetIsAccepted() || !verdict.GetIsAssembly())
                {
                    continue;
                }

                string? badComponent = FindRejectedComponent(verdict, rejected);
                if (badComponent == null)
                {
                    continue;
                }

                // Сборка падает вместе с компонентом.
                var issues = new List<ImportIssue>();
                issues.Add(new ImportIssue(verdict.FileName, IssueSeverity.Error,
                    "Компонент «" + badComponent + "» отклонён", badComponent));

                verdicts[i] = DocumentVerdict.Reject(verdict.FileName, issues, badComponent);
                rejected.Add(verdict.FileName);
                changed = true;
            }
        }
    }

    // Первый компонент сборки, который уже отклонён.
    private static string? FindRejectedComponent(
        DocumentVerdict verdict,
        HashSet<string> rejected)
    {
        CadDocument? document = verdict.Document;
        if (document == null || !document.GetIsAssembly())
        {
            return null;
        }

        for (int i = 0; i < document.Components.Count; i++)
        {
            string file = document.Components[i].File;
            if (rejected.Contains(file))
            {
                return file;
            }
        }

        return null;
    }

    // Предупреждения о циклах.
    private static void AddCycleWarnings(List<DocumentVerdict> verdicts)
    {
        // Граф из принятых сборок.
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < verdicts.Count; i++)
        {
            DocumentVerdict verdict = verdicts[i];
            if (!verdict.GetIsAccepted())
            {
                continue;
            }

            CadDocument? document = verdict.Document;
            if (document == null || !document.GetIsAssembly())
            {
                continue;
            }

            var files = new List<string>();
            for (int j = 0; j < document.Components.Count; j++)
            {
                files.Add(document.Components[j].File);
            }

            graph.Add(document.FileName, files);
        }

        IReadOnlyList<CycleInfo> cycles = CycleDetector.FindCycles(graph);
        if (cycles.Count == 0)
        {
            return;
        }

        for (int i = 0; i < verdicts.Count; i++)
        {
            DocumentVerdict verdict = verdicts[i];
            if (!verdict.GetIsAccepted())
            {
                continue;
            }

            CadDocument? document = verdict.Document;
            if (document == null)
            {
                continue;
            }

            CycleInfo? cycle = FindCycleFor(document, cycles);
            if (cycle == null)
            {
                continue;
            }

            var warnings = WarningsOnly(verdict.Issues);
            warnings.Add(new ImportIssue(document.FileName, IssueSeverity.Warning,
                "Обнаружена циклическая ссылка: " + cycle.GetDescription()));

            verdicts[i] = DocumentVerdict.Accept(document, warnings);
        }
    }

    // Первый цикл, в котором участвует документ.
    private static CycleInfo? FindCycleFor(CadDocument document, IReadOnlyList<CycleInfo> cycles)
    {
        for (int i = 0; i < cycles.Count; i++)
        {
            if (Participates(document, cycles[i]))
            {
                return cycles[i];
            }
        }

        return null;
    }

    // Документ — узел кольца или ссылается на узел кольца.
    private static bool Participates(CadDocument document, CycleInfo cycle)
    {
        var cycleNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < cycle.NodeIds.Count; i++)
        {
            cycleNodes.Add(cycle.NodeIds[i]);
        }

        if (cycleNodes.Contains(document.FileName))
        {
            return true;
        }

        for (int i = 0; i < document.Components.Count; i++)
        {
            if (cycleNodes.Contains(document.Components[i].File))
            {
                return true;
            }
        }

        return false;
    }

    // Детали с одинаковым обозначением.
    private static Dictionary<string, List<string>> FindDuplicateDesignations(
        Dictionary<string, CadReadResult> byFileName)
    {
        // Обозначение → файлы.
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // Берём список ключей, чтобы обойти словарь циклом for.
        List<string> keys = new List<string>(byFileName.Keys);

        for (int i = 0; i < keys.Count; i++)
        {
            CadReadResult read = byFileName[keys[i]];
            CadDocument? document = read.Document;

            if (document == null || string.IsNullOrWhiteSpace(document.Designation))
            {
                continue;
            }

            string designation = document.Designation.Trim();

            if (!groups.ContainsKey(designation))
            {
                groups.Add(designation, new List<string>());
            }

            groups[designation].Add(read.FileName);
        }

        return ToFileMap(groups);
    }

    // Стандартные изделия с одинаковым наименованием.
    private static Dictionary<string, List<string>> FindDuplicateStandardPartNames(
        Dictionary<string, CadReadResult> byFileName)
    {
        // Нормализованное имя → файлы.
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        List<string> keys = new List<string>(byFileName.Keys);

        for (int i = 0; i < keys.Count; i++)
        {
            CadReadResult read = byFileName[keys[i]];
            CadDocument? document = read.Document;

            if (document == null
                || document.Type != ObjectType.StandardPart
                || string.IsNullOrWhiteSpace(document.Name))
            {
                continue;
            }

            string normalized = ObjectKey.NormalizeName(document.Name);

            if (!groups.ContainsKey(normalized))
            {
                groups.Add(normalized, new List<string>());
            }

            groups[normalized].Add(read.FileName);
        }

        return ToFileMap(groups);
    }

    // Превращает «значение → файлы» в «файл → все файлы группы».
    private static Dictionary<string, List<string>> ToFileMap(
        Dictionary<string, List<string>> groups)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        List<string> groupKeys = new List<string>(groups.Keys);

        for (int i = 0; i < groupKeys.Count; i++)
        {
            List<string> files = groups[groupKeys[i]];

            if (files.Count <= 1)
            {
                continue;
            }

            List<string> sorted = new List<string>(files);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);

            for (int j = 0; j < sorted.Count; j++)
            {
                result[sorted[j]] = sorted;
            }
        }

        return result;
    }

    // Есть ли хоть одна ошибка.
    private static bool HasError(IReadOnlyList<ImportIssue> issues)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == IssueSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    // Только предупреждения.
    private static List<ImportIssue> WarningsOnly(IReadOnlyList<ImportIssue> issues)
    {
        var warnings = new List<ImportIssue>();

        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == IssueSeverity.Warning)
            {
                warnings.Add(issues[i]);
            }
        }

        return warnings;
    }
}