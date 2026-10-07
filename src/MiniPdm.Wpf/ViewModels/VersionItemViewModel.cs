using MiniPdm.Core.Domain;
using MiniPdm.Core.Persistence;

namespace MiniPdm.Wpf.ViewModels;

// Строка таблицы версий. Состояние показывается текстом на русском.
public sealed class VersionItemViewModel
{
    public VersionItemViewModel(StoredVersion version)
    {
        VersionNo = version.VersionNo;
        State = version.State;
        Material = version.Material;
        MassKg = version.MassKg;
    }

    // Номер версии
    public int VersionNo { get; }

    // Состояние как перечисление
    public ObjectState State { get; }

    // Материал; null — не задан
    public string? Material { get; }

    // Масса одной штуки; null — не задана
    public decimal? MassKg { get; }

    // Состояние текстом: «В работе», «Утверждено», «Аннулировано»
    public string StateText
    {
        get
        {
            return State.ToDisplay();
        }
    }
}