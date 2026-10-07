using MiniPdm.Core.Bom;

namespace MiniPdm.Wpf.ViewModels;

// Строка сводной спецификации в таблице.
// Обёртка нужна, потому что XAML привязывается только к свойствам,
// а в домене расчёт массы — метод.
public sealed class SpecificationItemViewModel
{
    public SpecificationItemViewModel(SpecificationRow row)
    {
        Designation = row.Designation;
        Name = row.Name;
        TotalQuantity = row.TotalQuantity;

        // Метод вызываем один раз здесь — XAML читает уже готовое свойство
        TotalMassKg = row.GetTotalMassKg();
    }

    // Обозначение; null — у стандартных изделий
    public string? Designation { get; }

    // Наименование
    public string Name { get; }

    // Общее количество по всем путям
    public long TotalQuantity { get; }

    // Масса всех штук; null — если масса одной неизвестна
    public decimal? TotalMassKg { get; }
}