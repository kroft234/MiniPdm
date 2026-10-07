using MiniPdm.Core.Domain;
using MiniPdm.Core.Persistence;

namespace MiniPdm.Wpf.ViewModels;

// Строка списка объектов. Обёртка над PdmObjectSummary:
// интерфейсу удобнее показывать готовые строки, чем форматировать числа и перечисления.
public sealed class ObjectItemViewModel
{
    public ObjectItemViewModel(PdmObjectSummary summary)
    {
        Id = summary.Id;
        Type = summary.Type;
        Designation = summary.Designation;
        Name = summary.Name;
        VersionNo = summary.VersionNo;
        State = summary.State;
        Material = summary.Material;
        MassKg = summary.MassKg;
    }

    public long Id { get; }

    public ObjectType Type { get; }

    public string? Designation { get; }

    public string Name { get; }

    public int VersionNo { get; }

    public ObjectState? State { get; }

    public string? Material { get; }

    public decimal? MassKg { get; }

    // Тип текстом
    public string TypeText
    {
        get
        {
            return Type.ToCadString();
        }
    }

    // Состояние текстом. Нет версии — пишем «нет версии»
    public string StateText
    {
        get
        {
            if (State == null)
            {
                return "нет версии";
            }

            return State.Value.ToDisplay();
        }
    }

    // Номер версии: «v2» или «—»
    public string VersionText
    {
        get
        {
            if (VersionNo > 0)
            {
                return "v" + VersionNo;
            }

            return "—";
        }
    }

    // Масса текстом. У сборки своя масса не задаётся
    public string MassText
    {
        get
        {
            if (MassKg != null)
            {
                return MassKg.Value.ToString("0.###");
            }

            if (Type == ObjectType.Assembly)
            {
                return "вычисляется";
            }

            return "не указана";
        }
    }

    // Заголовок: обозначение + имя, или только имя
    public string Title
    {
        get
        {
            if (Designation == null)
            {
                return Name;
            }

            return Designation + " «" + Name + "»";
        }
    }
}