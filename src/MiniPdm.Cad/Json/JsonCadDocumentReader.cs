using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;

namespace MiniPdm.Cad.Json;

// Читает CAD-документы из JSON. Единственный класс, знающий формат файла.
public sealed class JsonCadDocumentReader : ICadDocumentReader
{
    // "Мягкие" настройки: прощаем мелкие огрехи формата.
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        // Регистр имён не важен: type, Type, TYPE.
        PropertyNameCaseInsensitive = true,

        // Комментарии в JSON разрешены.
        ReadCommentHandling = JsonCommentHandling.Skip,

        // Запятая перед "}" не ошибка.
        AllowTrailingCommas = true,

        // Число может быть как 5, так и "5".
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // Прочитать документ из файла по пути.
    public async Task<CadDocument> ReadAsync(string path, CancellationToken ct)
    {
        // Пустой путь — ошибка.
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // using закроет файл в конце метода.
        using var stream = File.OpenRead(path);

        // Имя файла берём из пути.
        string fileName = Path.GetFileName(path);

        return await ReadAsync(stream, fileName, ct);
    }

    // Прочитать документ из открытого потока.
    // Имя файла задаёт вызывающий код, а не поле внутри файла.
    public async Task<CadDocument> ReadAsync(Stream stream, string fileName, CancellationToken ct)
    {
        // JSON → промежуточный объект CadDocumentDto.
        CadDocumentDto? dto = await JsonSerializer
            .DeserializeAsync<CadDocumentDto>(stream, Options, ct);

        // Пустой файл или не объект — ошибка.
        if (dto == null)
        {
            throw new JsonException("Документ «" + fileName + "» пуст или не является JSON");
        }

        // Промежуточный объект → доменный CadDocument.
        return dto.ToDomain(fileName);
    }
}

internal sealed class CadDocumentDto
{
    // Версия формата, по умолчанию 1.
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    // Поле fileName внутри файла.
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    // Тип: "Assembly", "Part", "StandardPart".
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    // Обозначение.
    [JsonPropertyName("designation")]
    public string? Designation { get; set; }

    // Наименование.
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // Блок свойств.
    [JsonPropertyName("properties")]
    public CadPropertiesDto? Properties { get; set; }

    // Список компонентов.
    [JsonPropertyName("components")]
    public List<CadComponentDto>? Components { get; set; }

    // Превращение JSON-модели в доменный CadDocument.
    public CadDocument ToDomain(string actualFileName)
    {

        ObjectType type;
        if (CadTypeParser.TryParse(Type, out ObjectType parsed))
        {
            type = parsed;
        }
        else
        {
            type = ObjectType.StandardPart;
        }

        // Свойства: не было — null, было — создаём.
        CadProperties? properties;
        if (Properties == null)
        {
            properties = null;
        }
        else
        {
            properties = new CadProperties(Properties.Material, Properties.Mass);
        }

        // Компоненты: не было — null, было — превращаем каждый.
        CadComponent[]? components;
        if (Components == null)
        {
            components = null;
        }
        else
        {
            var list = new List<CadComponent>();
            foreach (CadComponentDto item in Components)
            {
                // Нет file — подставляем пустую строку, валидатор заметит.
                string file = item.File ?? string.Empty;
                list.Add(new CadComponent(file, item.Count));
            }
            components = list.ToArray();
        }

        // Собираем доменный документ.
        return new CadDocument(
            actualFileName,          // имя файла на диске
            FormatVersion,           // версия формата
            type,                    // распознанный тип
            Name ?? string.Empty,    // наименование
            FileName,                // поле fileName из файла
            Type,                    // исходный текст type
            Designation,             // обозначение
            properties,              // свойства
            components);             // состав
    }
}

// Свойства в JSON.
internal sealed class CadPropertiesDto
{
    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("mass")]
    public decimal? Mass { get; set; }
}

// Один компонент в JSON.
internal sealed class CadComponentDto
{
    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

// Разбор строки типа в ObjectType без исключений.
public static class CadTypeParser
{
    public static bool TryParse(string? value, out ObjectType type)
    {
        // Не получилось превратить строку — false.
        if (!Enum.TryParse(value, ignoreCase: false, out type))
        {
            return false;
        }

        // Enum.TryParse пропускает числа вне перечисления (например, "99").
        // Поэтому проверяем, что значение реально существует.
        return Enum.IsDefined(type);
    }
}