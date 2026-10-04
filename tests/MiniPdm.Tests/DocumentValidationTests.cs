using FluentAssertions;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Validation;

namespace MiniPdm.Tests;

public class DocumentValidationTests
{

    private static CadDocument Part(
        string? designation = "АБВГ.301245.001",
        CadProperties? properties = null,
        string? declaredFileName = "part.json",
        int formatVersion = 1,
        string name = "Вал",
        CadComponent[]? components = null)
    {
        return new CadDocument(
            "part.json",
            formatVersion,
            ObjectType.Part,
            name,
            declaredFileName,
            "Part",
            designation,
            properties ?? new CadProperties("Сталь 45", 1.5m),
            components);
    }

    private static CadDocument StdPart(string? designation = null)
    {
        return new CadDocument(
            "std.json",
            1,
            ObjectType.StandardPart,
            "Подшипник 208",
            "std.json",
            "StandardPart",
            designation,
            new CadProperties(null, 0.4m),
            null);
    }

    private static CadDocument Assembly(
        string? designation = "АБВГ.301245.001",
        CadProperties? properties = null,
        CadComponent[]? components = null)
    {
        return new CadDocument(
            "asm1.json",
            1,
            ObjectType.Assembly,
            "Узел",
            "asm1.json",
            "Assembly",
            designation,
            properties ?? new CadProperties(null, null),
            components ?? new CadComponent[] { new CadComponent("part.json", 1) });
    }

    private static List<ImportIssue> ValidateDoc(CadDocument document)
    {
        return DocumentValidator.Validate(document).ToList();
    }

    // Есть ли среди проблем такая, где важность и текст совпадают
    private static bool ContainsIssue(List<ImportIssue> issues, IssueSeverity severity, string textPart)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            ImportIssue issue = issues[i];

            if (issue.Severity == severity && issue.Reason.Contains(textPart))
            {
                return true;
            }
        }

        return false;
    }

    // Есть ли среди проблем такая, где текст совпадает (важность любая)
    private static bool ContainsIssueText(List<ImportIssue> issues, string textPart)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Reason.Contains(textPart))
            {
                return true;
            }
        }

        return false;
    }

    // Есть ли проблема с указанной важностью
    private static bool HasIssueWithSeverity(List<ImportIssue> issues, IssueSeverity severity)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == severity)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Валидная_деталь_не_имеет_проблем()
    {
        ValidateDoc(Part()).Should().BeEmpty();
    }

    [Fact]
    public void Валидное_стандартное_изделие_не_имеет_ошибок()
    {
        var issues = ValidateDoc(StdPart());

        HasIssueWithSeverity(issues, IssueSeverity.Error).Should().BeFalse();
    }

    [Fact]
    public void Валидная_сборка_не_имеет_проблем()
    {
        ValidateDoc(Assembly()).Should().BeEmpty();
    }

    [Fact]
    public void Расхождение_имени_файла_и_поля_fileName_это_ошибка()
    {
        var issues = ValidateDoc(Part(declaredFileName: "other.json"));

        issues.Should().ContainSingle();
        issues[0].Reason.Should().Contain("расходится с именем файла");
    }

    [Fact]
    public void Неизвестная_версия_формата_это_ошибка()
    {
        var issues = ValidateDoc(Part(formatVersion: 2));

        issues.Should().ContainSingle();
        issues[0].Reason.Should().Contain("не поддерживается");
    }

    [Fact]
    public void Деталь_без_обозначения_отклоняется()
    {
        var issues = ValidateDoc(Part(designation: null));

        ContainsIssue(issues, IssueSeverity.Error, "Обозначение не указано").Should().BeTrue();
    }

    [Fact]
    public void Стандартное_изделие_не_должно_иметь_обозначения()
    {
        var issues = ValidateDoc(StdPart(designation: "АБВГ.301245.001"));

        ContainsIssue(issues, IssueSeverity.Error, "не должно иметь обозначения").Should().BeTrue();
    }

    [Fact]
    public void Документ_без_наименования_отклоняется()
    {
        var issues = ValidateDoc(Part(name: "   "));

        ContainsIssueText(issues, "Наименование не заполнено").Should().BeTrue();
    }


    [Fact]
    public void Деталь_без_материала_отклоняется()
    {
        var issues = ValidateDoc(Part(properties: new CadProperties(null, 1.5m)));

        ContainsIssue(issues, IssueSeverity.Error, "материал").Should().BeTrue();
    }

    [Fact]
    public void Отсутствие_массы_у_детали_это_предупреждение()
    {
        var issues = ValidateDoc(Part(properties: new CadProperties("Сталь 45", null)));

        ContainsIssue(issues, IssueSeverity.Warning, "не указана").Should().BeTrue();
    }

    [Fact]
    public void Отрицательная_масса_это_ошибка()
    {
        var issues = ValidateDoc(Part(properties: new CadProperties("Сталь 45", -1m)));

        ContainsIssue(issues, IssueSeverity.Error, "положительной").Should().BeTrue();
    }

    [Fact]
    public void Масса_у_сборки_это_предупреждение()
    {
        var issues = ValidateDoc(Assembly(properties: new CadProperties(null, 5m)));

        ContainsIssue(issues, IssueSeverity.Warning, "вычисляется по составу").Should().BeTrue();
    }


    [Fact]
    public void Сборка_без_компонентов_это_предупреждение()
    {
        var issues = ValidateDoc(Assembly(components: Array.Empty<CadComponent>()));

        ContainsIssue(issues, IssueSeverity.Warning, "Состав сборки пуст").Should().BeTrue();
    }

    [Fact]
    public void У_детали_не_может_быть_компонентов()
    {
        var issues = ValidateDoc(Part(components: new CadComponent[]
            { new CadComponent("x.json", 1) }));

        ContainsIssue(issues, IssueSeverity.Error, "не может иметь состав").Should().BeTrue();
    }

    [Fact]
    public void Нулевое_количество_компонента_это_ошибка()
    {
        var issues = ValidateDoc(Assembly(components: new CadComponent[]
            { new CadComponent("part.json", 0) }));

        ContainsIssue(issues, IssueSeverity.Error, "положительным").Should().BeTrue();
    }

    [Fact]
    public void Двойной_компонент_в_составе_это_ошибка()
    {
        var issues = ValidateDoc(Assembly(components: new CadComponent[]
        {
            new CadComponent("part.json", 1),
            new CadComponent("part.json", 2)
        }));

        ContainsIssue(issues, IssueSeverity.Error, "более одного раза").Should().BeTrue();
    }
}