using System.Text.RegularExpressions;

namespace MiniPdm.Core.Domain;

// Проверка обозначения ЕСКД вида АБВГ.301245.001
public static class DesignationValidator
{
    public const string Pattern = @"^[А-ЯЁ]{4}\.\d{6}\.\d{3}$";

    // Результат проверки: валидно ли и причина ошибки
    public sealed class Result
    {
        public bool IsValid { get; }

        public string? Error { get; }

        private Result(bool isValid, string? error)
        {
            IsValid = isValid;
            Error = error;
        }

        public static Result Valid()
        {
            return new Result(true, null);
        }

        public static Result Invalid(string error)
        {
            return new Result(false, error);
        }
    }

    // Быстрая проверка «да/нет» без причины
    public static bool IsValid(string? designation)
    {
        return Validate(designation).IsValid;
    }

    public static Result Validate(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation))
        {
            return Result.Invalid("Обозначение не указано");
        }

        if (designation != designation.Trim())
        {
            return Result.Invalid("Обозначение содержит пробелы по краям");
        }

        // CultureInvariant: без него латинская P и кириллическая Р считаются одной буквой
        if (Regex.IsMatch(designation, Pattern, RegexOptions.CultureInvariant))
        {
            return Result.Valid();
        }

        return Result.Invalid(DescribeMismatch(designation));
    }

    // Подробная причина несоответствия формату
    private static string DescribeMismatch(string designation)
    {
        var parts = designation.Split('.');
        string reason;

        if (parts.Length != 3)
        {
            reason = "ожидалось 3 части, разделённые точками, а получено " + parts.Length;
        }
        else if (ContainsLatinLetter(designation))
        {
            reason = "буквы должны быть кириллическими, найдены латинские";
        }
        else if (parts[0].Length != 4)
        {
            reason = "в коде должно быть 4 буквы, а в «" + parts[0] + "» — " + parts[0].Length;
        }
        else if (parts[1].Length != 6)
        {
            reason = "в номере должно быть 6 цифр, а в «" + parts[1] + "» — " + parts[1].Length;
        }
        else if (parts[2].Length != 3)
        {
            reason = "в коде версии должно быть 3 цифры, а в «" + parts[2] + "» — " + parts[2].Length;
        }
        else
        {
            reason = "все части верной длины, но есть другие несоответствия формату";
        }

        return "Обозначение «" + designation + "» не соответствует ЕСКД: " + reason;
    }

    // Проверка по коду символа: Р (U+0420) и P (U+0050) выглядят одинаково, но это разные буквы
    private static bool ContainsLatinLetter(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetter(ch) && ch < 0x0400 && ch != '.')
            {
                return true;
            }
        }

        return false;
    }

    // Нормализация: убрать пробелы по краям
    public static string? Normalize(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation))
        {
            return null;
        }

        return designation.Trim();
    }
}