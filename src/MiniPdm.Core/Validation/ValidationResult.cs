namespace MiniPdm.Core.Validation;

// Уровень проблемы в отчёте импорта
public enum IssueSeverity
{
    Error = 0,    // документ не импортируется
    Warning = 1   // импортируется, но с замечанием
}

// Одна строка отчёта импорта: файл, уровень проблемы, причина
public sealed class ImportIssue
{
    public ImportIssue(string fileName, IssueSeverity severity, string reason, string? relatedFile = null)
    {
        FileName = fileName;
        Severity = severity;
        Reason = reason;
        RelatedFile = relatedFile;
    }

    public string FileName { get; }

    public IssueSeverity Severity { get; }

    public string Reason { get; }

    // Другой файл, к которому относится проблема (если есть)
    public string? RelatedFile { get; }

    public override string ToString()
    {
        string severityText;
        if (Severity == IssueSeverity.Error)
        {
            severityText = "Ошибка";
        }
        else
        {
            severityText = "Предупреждение";
        }

        string relatedText = string.Empty;
        if (RelatedFile != null)
        {
            relatedText = " [связано с: " + RelatedFile + "]";
        }

        return FileName + " " + severityText + " " + Reason + relatedText;
    }
}