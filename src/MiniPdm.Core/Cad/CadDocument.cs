using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Cad;

// Строка состава сборки: имя файла дочернего документа + количество
public sealed class CadComponent
{
    public CadComponent(string file, int count)
    {
        File = file;
        Count = count;
    }

    public string File { get; }

    public int Count { get; }

    public override string ToString()
    {
        return File + " x " + Count;
    }
}

// Материал и масса одной штуки (могут отсутствовать — у сборки их нет)
public sealed class CadProperties
{
    public CadProperties(string? material, decimal? massKg)
    {
        Material = material;
        MassKg = massKg;
    }

    public string? Material { get; }

    public decimal? MassKg { get; }
}

// Документ CAD-системы, готовый к импорту.
// Тут нет ни JSON, ни файлов — их разбирает другой класс (читатель).
public sealed class CadDocument
{
    public CadDocument(
        string fileName,
        int formatVersion,
        ObjectType type,
        string name,
        string? declaredFileName = null,
        string? declaredType = null,
        string? designation = null,
        CadProperties? properties = null,
        CadComponent[]? components = null)
    {
        FileName = fileName;
        FormatVersion = formatVersion;
        Type = type;
        Name = name;
        DeclaredFileName = declaredFileName;
        DeclaredType = declaredType;
        Designation = designation;
        Properties = properties;
        Components = components ?? Array.Empty<CadComponent>();
    }

    // Имя файла в папке экспорта — по нему разрешаются ссылки сборок
    public string FileName { get; }

    // Имя файла изнутри документа (для сверки с реальным именем файла)
    public string? DeclaredFileName { get; }

    public int FormatVersion { get; }

    public ObjectType Type { get; }

    // Поле type как было в файле (для сообщений об ошибках)
    public string? DeclaredType { get; }

    public string? Designation { get; }

    public string Name { get; }

    public CadProperties? Properties { get; }

    // Состав сборки (у деталей — пустой список)
    // IReadOnlyList: состав можно читать, но нельзя менять извне.
    public IReadOnlyList<CadComponent> Components { get; }

    // Короткие методы, чтобы не писать везде Properties?.MassKg
    public string? GetMaterial()
    {
        return Properties?.Material;
    }

    public decimal? GetMassKg()
    {
        return Properties?.MassKg;
    }

    // Совпадает ли type из файла с распознанным типом
    public bool GetIsTypeRecognized()
    {
        return DeclaredType != null
            && string.Equals(DeclaredType, Type.ToCadString(), StringComparison.Ordinal);
    }

    public bool GetIsAssembly()
    {
        return Type == ObjectType.Assembly;
    }

    // Ключ объекта в системе: обозначение или наименование стандартного изделия
    public ObjectKey ToKey()
    {
        return ObjectKey.From(Type, Designation, Name);
    }

    public override string ToString()
    {
        return FileName + " (" + Type.ToCadString() + ")";
    }
}

// Результат чтения одного файла.
// Битый файл — не авария, а обычный исход: попадёт в отчёт, импорт продолжится.
public sealed class CadReadResult
{
    public CadReadResult(string fileName, CadDocument? document, string? error)
    {
        FileName = fileName;
        Document = document;
        Error = error;
    }

    public string FileName { get; }

    public CadDocument? Document { get; }

    public string? Error { get; }

    public bool GetSuccess()
    {
        return Document != null;
    }

    public static CadReadResult Ok(CadDocument document)
    {
        return new CadReadResult(document.FileName, document, null);
    }

    public static CadReadResult Failed(string fileName, string error)
    {
        return new CadReadResult(fileName, null, error);
    }
}