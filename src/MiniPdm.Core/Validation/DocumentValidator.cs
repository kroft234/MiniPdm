using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Validation;

// Валидатор одного CAD-документа. Ничего не меняет — только собирает проблемы
public static class DocumentValidator
{
    // Структурная и семантическая проверка одного документа
    public static IReadOnlyList<ImportIssue> Validate(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<ImportIssue>();

        CheckFileName(document, issues);
        CheckType(document, issues);
        CheckProperties(document, issues);
        CheckComponents(document, issues);

        return issues;
    }

    private static void CheckFileName(CadDocument document, List<ImportIssue> issues)
    {
        // Имя файла на диске — ключ документа (fileName должно совпасть)
        if (document.DeclaredFileName != null
            && !string.Equals(document.DeclaredFileName, document.FileName, StringComparison.Ordinal))
        {
            issues.Add(Error(document,
                "Значение fileName «" + document.DeclaredFileName + "» расходится с именем файла "
                + "«" + document.FileName + "»; именно имя файла служит ключом документа"));
        }

        if (document.FormatVersion != 1)
        {
            issues.Add(Error(document,
                "Версия формата " + document.FormatVersion + " не поддерживается, допустима только 1"));
        }
    }

    private static void CheckType(CadDocument document, List<ImportIssue> issues)
    {
        var type = document.Type;

        // Тип не распознан(проверка не имеет смысл)
        if (!document.GetIsTypeRecognized())
        {
            issues.Add(Error(document,
                document.DeclaredType == null
                    ? "Поле type отсутствует, ожидается Assembly, Part или StandardPart"
                    : "Тип объекта «" + document.DeclaredType + "» неизвестен, ожидается Assembly, Part или StandardPart"));
        }

        if (type.RequiresDesignation())
        {
            var check = DesignationValidator.Validate(document.Designation);
            if (!check.IsValid)
            {
                issues.Add(Error(document, check.Error!));
            }
        }
        else
        {
            // У стандартного изделия обозначения нет
            if (!string.IsNullOrWhiteSpace(document.Designation))
            {
                issues.Add(Error(document,
                    "Стандартное изделие опознаётся по наименованию и не должно иметь обозначения, "
                    + "а указано «" + document.Designation + "»"));
            }
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            issues.Add(Error(document, "Наименование не заполнено"));
        }
    }

    private static void CheckProperties(CadDocument document, List<ImportIssue> issues)
    {
        if (document.Properties == null)
        {
            issues.Add(Error(document, "Блок properties отсутствует"));
            return;
        }

        var type = document.Type;

        // Материал обязателен только у деталей
        if (type.RequiresMaterial() && string.IsNullOrWhiteSpace(document.Properties.Material))
        {
            issues.Add(Error(document, "Для детали материал обязателен"));
        }

        if (type == ObjectType.Assembly)
        {
            // У сборки масса не задаётся: она вычисляется по составу
            if (document.Properties.MassKg != null)
            {
                issues.Add(Warning(document,
                    "Масса сборки (" + document.Properties.MassKg
                    + " кг) задана вручную, но вычисляется по составу и будет проигнорирована"));
            }

            return;
        }

        // У детали и стандартного изделия масса обязательна,
        // но её отсутствие — предупреждение, а не ошибка
        if (document.Properties.MassKg == null)
        {
            issues.Add(Warning(document, "Масса одной штуки не указана"));
        }
        else if (document.Properties.MassKg <= 0)
        {
            issues.Add(Error(document, "Масса должна быть положительной, получено " + document.Properties.MassKg));
        }
    }

    private static void CheckComponents(CadDocument document, List<ImportIssue> issues)
    {
        var components = document.Components;

        if (!document.GetIsAssembly())
        {
            // У детали и стандартного изделия состава быть не может
            if (components.Count > 0)
            {
                issues.Add(Error(document,
                    "Документ типа " + document.Type.ToCadString() + " не может иметь состав, "
                    + "но содержит " + components.Count + " компонент(ов)"));
            }

            return;
        }

        if (components.Count == 0)
        {
            issues.Add(Warning(document, "Состав сборки пуст"));
        }

        // Дубликат компонента — ошибка: связь «родитель + потомок» уникальна
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < components.Count; i++)
        {
            var component = components[i];

            // Нет ссылки на файл
            if (string.IsNullOrWhiteSpace(component.File))
            {
                issues.Add(Error(document, "У компонента не указан файл"));
                continue;
            }

            // Количество должно быть положительным
            if (component.Count <= 0)
            {
                issues.Add(Error(document,
                    "Число экземпляров «" + component.File + "» должно быть положительным, получено "
                    + component.Count));
            }

            // Компонент уже встречался(дубликат)
            if (seenFiles.Contains(component.File))
            {
                issues.Add(Error(document,
                    "Компонент «" + component.File + "» встречается в сборке более одного раза"));
            }
            else
            {
                seenFiles.Add(component.File);
            }
        }

        // Имя сборки совпало с именем файла (неправильный экспорт)
        if (string.Equals(document.FileName, document.Name, StringComparison.Ordinal))
        {
            issues.Add(Warning(document, "Наименование сборки совпадает с именем файла"));
        }
    }

    private static ImportIssue Error(CadDocument document, string reason)
    {
        return new ImportIssue(document.FileName, IssueSeverity.Error, reason);
    }

    private static ImportIssue Warning(CadDocument document, string reason)
    {
        return new ImportIssue(document.FileName, IssueSeverity.Warning, reason);
    }
}