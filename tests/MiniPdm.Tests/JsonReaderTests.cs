using System.Text;
using System.Text.Json;
using FluentAssertions;
using MiniPdm.Cad.Json;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;

namespace MiniPdm.Tests;

public class JsonReaderTests
{
    private static readonly JsonCadDocumentReader Reader = new JsonCadDocumentReader();

    // Разбор JSON из строки через поток в памяти
    private static async Task<CadDocument> ReadJson(string json, string fileName)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await Reader.ReadAsync(stream, fileName, CancellationToken.None);
    }

    [Fact]
    public async Task Валидная_деталь_разбирается_полностью()
    {
        var json = """
            {
              "formatVersion": 1,
              "fileName": "part.json",
              "type": "Part",
              "designation": "АБВГ.301245.001",
              "name": "Вал",
              "properties": { "material": "Сталь 45", "mass": 1.5 }
            }
            """;

        var doc = await ReadJson(json, "part.json");

        doc.FileName.Should().Be("part.json");
        doc.FormatVersion.Should().Be(1);
        doc.Type.Should().Be(ObjectType.Part);
        doc.GetIsTypeRecognized().Should().BeTrue();
        doc.DeclaredFileName.Should().Be("part.json");
        doc.Designation.Should().Be("АБВГ.301245.001");
        doc.Name.Should().Be("Вал");
        doc.GetMaterial().Should().Be("Сталь 45");
        doc.GetMassKg().Should().Be(1.5m);
        doc.Components.Should().BeEmpty();
    }

    [Fact]
    public async Task Сборка_с_компонентами_читает_состав()
    {
        var json = """
            {
              "type": "Assembly",
              "designation": "АБВГ.301245.001",
              "name": "Узел",
              "components": [
                { "file": "part.json", "count": 2 },
                { "file": "std.json", "count": 4 }
              ]
            }
            """;

        var doc = await ReadJson(json, "asm1.json");

        doc.GetIsAssembly().Should().BeTrue();
        doc.Components.Should().HaveCount(2);
        doc.Components[0].File.Should().Be("part.json");
        doc.Components[0].Count.Should().Be(2);
        doc.Components[1].File.Should().Be("std.json");
        doc.Components[1].Count.Should().Be(4);
    }

    [Fact]
    public async Task Число_в_JSON_может_прийти_строкой()
    {
        var json = """
            {
              "type": "Part",
              "designation": "АБВГ.301245.001",
              "name": "Вал",
              "properties": { "material": "Сталь 45", "mass": "0.391" }
            }
            """;

        var doc = await ReadJson(json, "part.json");

        doc.GetMassKg().Should().Be(0.391m);
    }

    [Fact]
    public async Task Неизвестный_тип_не_роняет_чтение_файла()
    {
        var json = """
            {
              "type": "Gadget",
              "name": "Странный объект"
            }
            """;

        var doc = await ReadJson(json, "gadget.json");

        // Неизвестный тип становится типом по умолчанию — «нераспознанным»
        doc.Type.Should().Be(ObjectType.StandardPart);
        doc.DeclaredType.Should().Be("Gadget");
        doc.GetIsTypeRecognized().Should().BeFalse();
    }

    [Fact]
    public async Task Отсутствующий_тип_тоже_нераспознан()
    {
        var json = """
            {
              "name": "Объект без типа"
            }
            """;

        var doc = await ReadJson(json, "notype.json");

        doc.DeclaredType.Should().BeNull();
        doc.Type.Should().Be(ObjectType.StandardPart);
        doc.GetIsTypeRecognized().Should().BeFalse();
    }

    [Fact]
    public async Task Содержимое_null_это_ошибка_чтения()
    {
        Func<Task<CadDocument>> act = () => ReadJson("null", "null.json");

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task Комментарии_и_хвостовые_запятые_допустимы()
    {
        var json = """
            {
              // комментарий в файле
              "type": "Part",
              "name": "Вал", // ещё комментарий
              "properties": { "mass": 2 },
            }
            """;

        var doc = await ReadJson(json, "part.json");

        doc.Name.Should().Be("Вал");
        doc.GetMassKg().Should().Be(2m);
    }

    [Fact]
    public async Task Отсутствующее_наименование_становится_пустой_строкой()
    {
        var json = """
            {
              "type": "Part",
              "designation": "АБВГ.301245.001"
            }
            """;

        var doc = await ReadJson(json, "part.json");

        doc.Name.Should().BeEmpty();
    }
}