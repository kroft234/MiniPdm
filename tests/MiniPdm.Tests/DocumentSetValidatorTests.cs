using FluentAssertions;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Validation;

namespace MiniPdm.Tests;

// Тесты проверки набора документов.
// Наборы повторяют ситуации реальной папки: дубли обозначений,
// ссылка на отсутствующий файл, битый файл, каскад, цикл.
public sealed class DocumentSetValidatorTests
{
    [Fact]
    public void Ссылка_на_отсутствующий_файл_отклоняет_сборку()
    {
        var part = Ok(Part("РДЦЛ.304112.801", "Пробка сливная", 0.05m));
        var assembly = Ok(Assembly("РДЦЛ.304112.900", "Маслоуказатель в сборе",
            new CadComponent("Пробка сливная.m3d", 1),
            new CadComponent("Насос шестерённый НШ-10.m3d", 1)));

        var result = DocumentSetValidator.Validate(new CadReadResult[] { part, assembly });

        result.GetRejected().Should().Be(1);

        DocumentVerdict verdict = result.ForFile("Маслоуказатель в сборе.a3d")!;
        verdict.RejectedBecauseOf.Should().Be("Насос шестерённый НШ-10.m3d");
    }

    [Fact]
    public void Дубликат_обозначения_отклоняет_оба_документа()
    {
        // Ситуация из реальной папки: два файла с одним обозначением
        var first = Ok(Part("РДЦЛ.304112.710", "Шайба стопорная", 0.002m));
        var second = Ok(Part("РДЦЛ.304112.710", "Шайба упорная", 0.003m));

        var result = DocumentSetValidator.Validate(new CadReadResult[] { first, second });

        result.GetAccepted().Should().Be(0);
        result.GetRejected().Should().Be(2);

        IReadOnlyList<ImportIssue> issues = result.GetAllIssues();

        ContainsReason(issues, "Шайба стопорная.m3d", "Шайба упорная.m3d").Should().BeTrue();
    }

    [Fact]
    public void Одинаковое_наименование_деталей_не_считается_дубликатом()
    {
        // Наименование уникально только среди стандартных изделий:
        // две детали с одним названием, но разными обозначениями — разные объекты.
        var first = Ok(Standard("Вал.m3d", "РДЦЛ.304112.301", "Вал"));
        var second = Ok(Standard("Вал2.m3d", "РДЦЛ.304112.302", "Вал"));

        var result = DocumentSetValidator.Validate(new CadReadResult[] { first, second });

        result.GetRejected().Should().Be(0);
    }

    [Fact]
    public void Одинаковое_наименование_стандартных_изделий_отклоняется()
    {
        // Для стандартных изделий наименование — идентификатор,
        // совпадение означает неоднозначность.
        var first = StandardPart("Подшипник.m3d", "Подшипник 208 ГОСТ 8338-75");
        var second = StandardPart("Подшипник2.m3d", "подшипник 208 ГОСТ 8338-75");

        var result = DocumentSetValidator.Validate(new CadReadResult[] { Ok(first), Ok(second) });

        result.GetRejected().Should().Be(2);

        ContainsReason(result.GetAllIssues(), "Наименование стандартного изделия").Should().BeTrue();
    }

    [Fact]
    public void Циклическая_ссылка_даёт_предупреждение_а_не_отклонение()
    {
        var lever = Ok(Assembly("РДЦЛ.305001.000", "Рычаг переключения",
            new CadComponent("Тяга переключения.a3d", 1)));
        var rod = Ok(Assembly("РДЦЛ.305002.000", "Тяга переключения",
            new CadComponent("Рычаг переключения.a3d", 1)));

        var result = DocumentSetValidator.Validate(new CadReadResult[] { lever, rod });

        // Цикл делает дерево нераскрываемым, но данные корректны
        result.GetRejected().Should().Be(0);
        result.GetWithWarnings().Should().Be(2);

        ContainsReason(result.GetAllIssues(), "циклическая ссылка").Should().BeTrue();
    }

    [Fact]
    public void Каскад_отклонения_помечает_причину_в_отчёте()
    {
        var brokenPart = CadReadResult.Failed("Насос шестерённый НШ-10.m3d",
            "Не удалось разобрать JSON");
        var assembly = Ok(Assembly("РДЦЛ.304112.100", "Насос в сборе",
            new CadComponent("Насос шестерённый НШ-10.m3d", 1)));

        var result = DocumentSetValidator.Validate(new CadReadResult[] { brokenPart, assembly });

        result.GetRejected().Should().Be(2);

        DocumentVerdict verdict = result.ForFile("Насос в сборе.a3d")!;
        verdict.RejectedBecauseOf.Should().Be("Насос шестерённый НШ-10.m3d");
    }

    [Fact]
    public void Счётчики_отчёта_согласованы_между_собой()
    {
        var results = new CadReadResult[]
        {
            Ok(Part("РДЦЛ.304112.301", "Вал промежуточный", 3.42m)),
            Ok(Part("РДЦЛ.304112.904", "Прокладка маслоуказателя", null)),
            CadReadResult.Failed("Отдушина.m3d", "Не удалось разобрать JSON")
        };

        var result = DocumentSetValidator.Validate(results);

        result.GetTotalFiles().Should().Be(3);
        result.GetAccepted().Should().Be(2);
        result.GetRejected().Should().Be(1);
        result.GetWithWarnings().Should().Be(1);
        result.GetAcceptedWithoutWarnings().Should().Be(1);

        int sum = result.GetAcceptedWithoutWarnings() + result.GetWithWarnings();
        result.GetAccepted().Should().Be(sum);
    }

    // Есть ли среди проблем такая, где текст содержит все указанные куски
    private static bool ContainsReason(IReadOnlyList<ImportIssue> issues, params string[] parts)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            string reason = issues[i].Reason;
            bool allParts = true;

            for (int j = 0; j < parts.Length; j++)
            {
                if (!reason.Contains(parts[j]))
                {
                    allParts = false;
                    break;
                }
            }

            if (allParts)
            {
                return true;
            }
        }

        return false;
    }

    private static CadReadResult Ok(CadDocument document)
    {
        return CadReadResult.Ok(document);
    }

    private static CadDocument Part(string designation, string name, decimal? mass)
    {
        return new CadDocument(
            name + ".m3d",
            1,
            ObjectType.Part,
            name,
            name + ".m3d",
            "Part",
            designation,
            new CadProperties("Сталь 45", mass));
    }

    private static CadDocument Assembly(
        string designation, string name, params CadComponent[] components)
    {
        return new CadDocument(
            name + ".a3d",
            1,
            ObjectType.Assembly,
            name,
            name + ".a3d",
            "Assembly",
            designation,
            new CadProperties(null, null),
            components);
    }

    private static CadDocument StandardPart(string fileName, string name)
    {
        return new CadDocument(
            fileName,
            1,
            ObjectType.StandardPart,
            name,
            fileName,
            "StandardPart",
            null,
            new CadProperties(null, 0.37m));
    }

    private static CadDocument Standard(string fileName, string designation, string name)
    {
        return new CadDocument(
            fileName,
            1,
            ObjectType.Part,
            name,
            fileName,
            "Part",
            designation,
            new CadProperties("Сталь 45", 0.05m));
    }
}