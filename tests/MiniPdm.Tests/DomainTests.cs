using FluentAssertions;
using MiniPdm.Core.Domain;

namespace MiniPdm.Tests;


// Ключ хранится в нормализованном виде: «Подшипник 208» и «подшипник  208» — один объект
public class ObjectKeyTests
{
    [Fact]
    public void Стандартные_изделия_с_разным_регистром_и_пробелами_равны()
    {
        var key1 = ObjectKey.From(ObjectType.StandardPart, null, "Подшипник 208");
        var key2 = ObjectKey.From(ObjectType.StandardPart, null, "подшипник  208");

        key1.Should().Be(key2);
        key1.GetHashCode().Should().Be(key2.GetHashCode());
    }

    [Fact]
    public void У_детали_наименование_не_входит_в_ключ()
    {
        var key1 = ObjectKey.From(ObjectType.Part, "ПРИ.238111.517", "Какое-то имя");
        var key2 = ObjectKey.From(ObjectType.Part, "ПРИ.238111.517", "Другое имя");

        key1.Should().Be(key2);
    }

    [Fact]
    public void Разные_обозначения_или_типы_не_равны()
    {
        var a = ObjectKey.From(ObjectType.Part, "ПРИ.238111.517", null);
        var b = ObjectKey.From(ObjectType.Part, "ПРИ.238111.518", null);
        var c = ObjectKey.From(ObjectType.Assembly, "ПРИ.238111.517", null);

        a.Should().NotBe(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void Для_стандартного_изделия_без_имени_From_бросает()
    {
        Action act = () => ObjectKey.From(ObjectType.StandardPart, null, "   ");

        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Для_детали_без_обозначения_From_бросает()
    {
        Action act = () => ObjectKey.From(ObjectType.Part, null, "Важное имя");

        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void GetDisplay_для_стандартного_изделия_добавляет_пояснение()
    {
        var key = ObjectKey.From(ObjectType.StandardPart, null, "Подшипник 208");

        key.GetDisplay().Should().Be("подшипник 208 (стандартное изделие)");
    }
}

public class StateTransitionsTests
{
    [Fact]
    public void Правильные_переходы_разрешены()
    {
        StateTransitions.CanTransition(ObjectState.InWork, ObjectState.Approved).Should().BeTrue();
        StateTransitions.CanTransition(ObjectState.InWork, ObjectState.Cancelled).Should().BeTrue();
        StateTransitions.CanTransition(ObjectState.Approved, ObjectState.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void Неправильные_переходы_запрещены()
    {
        StateTransitions.CanTransition(ObjectState.Approved, ObjectState.InWork).Should().BeFalse();
        StateTransitions.CanTransition(ObjectState.Cancelled, ObjectState.Approved).Should().BeFalse();
        StateTransitions.CanTransition(ObjectState.Approved, ObjectState.Approved).Should().BeFalse();
    }

    [Fact]
    public void Редактировать_можно_только_в_работе()
    {
        StateTransitions.IsEditable(ObjectState.InWork).Should().BeTrue();
        StateTransitions.IsEditable(ObjectState.Approved).Should().BeFalse();
        StateTransitions.IsEditable(ObjectState.Cancelled).Should().BeFalse();
    }
}

public class ObjectVersionTests
{
    private static ObjectVersion InWorkVersion()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new ObjectVersion(1, 1, 1, ObjectState.InWork, "Сталь", 1.5m, created);
    }

    [Fact]
    public void Номер_версии_не_может_быть_меньше_единицы()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Action act = () => new ObjectVersion(1, 1, 0, ObjectState.InWork, null, null, created);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Масса_не_может_быть_отрицательной()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Action act = () => new ObjectVersion(1, 1, 1, ObjectState.InWork, null, -1m, created);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void В_работе_атрибуты_меняются()
    {
        var version = InWorkVersion();

        version.ChangeAttributes("Алюминий", 0.9m);

        version.Material.Should().Be("Алюминий");
        version.MassKg.Should().Be(0.9m);
    }

    [Fact]
    public void Утверждённую_версию_править_нельзя()
    {
        var version = InWorkVersion();
        version.ChangeState(ObjectState.Approved, "Объект");

        Action act = () => version.ChangeAttributes("Алюминий", 0.9m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Правильный_переход_меняет_состояние()
    {
        var version = InWorkVersion();

        version.ChangeState(ObjectState.Approved, "Объект");

        version.State.Should().Be(ObjectState.Approved);
        version.GetIsEditable().Should().BeFalse();
    }

    [Fact]
    public void Недопустимый_переход_бросает()
    {
        var version = InWorkVersion();
        version.ChangeState(ObjectState.Approved, "Объект");

        Action act = () => version.ChangeState(ObjectState.InWork, "Объект");

        act.Should().Throw<InvalidOperationException>();
    }
}

public class DesignationValidatorTests
{
    [Fact]
    public void Правильное_обозначение_принимается()
    {
        var result = DesignationValidator.Validate("АБВГ.301245.001");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Пустое_обозначение_отклоняется()
    {
        DesignationValidator.Validate(null).IsValid.Should().BeFalse();
        DesignationValidator.Validate("   ").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Обозначение_с_пробелами_по_краям_отклоняется()
    {
        var result = DesignationValidator.Validate(" АБВГ.301245.001");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("пробелы по краям");
    }

    [Fact]
    public void Латинские_буквы_отклоняются()
    {
        var result = DesignationValidator.Validate("WXYZ.301245.001");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("кириллическими");
    }

    [Fact]
    public void Неверное_число_цифр_даёт_понятную_причину()
    {
        var result = DesignationValidator.Validate("АБВГ.30124.001");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("6 цифр");
    }

    [Fact]
    public void Normalize_убирает_пробелы_по_краям_и_возвращает_нуль_для_пустого()
    {
        DesignationValidator.Normalize("  АБВГ.301245.001  ").Should().Be("АБВГ.301245.001");
        DesignationValidator.Normalize(null).Should().BeNull();
    }
}