using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;

namespace MiniPdm.Wpf.ViewModels;

// Узел дерева состава для TreeView. Дети собираются один раз при создании.
public sealed class BomNodeViewModel
{
    public BomNodeViewModel(BomTreeNode node)
    {
        Row = node.Row;

        // Рекурсивно оборачиваем всех детей
        for (int i = 0; i < node.Children.Count; i++)
        {
            BomTreeNode child = node.Children[i];
            Children.Add(new BomNodeViewModel(child));
        }
    }

    public BomTreeRow Row { get; }

    public List<BomNodeViewModel> Children { get; } = new List<BomNodeViewModel>();

    // Раскрыт ли узел. Привязка двусторонняя: значение нужно только для
    // начального состояния — корень открыт сразу, остальные по клику
    public bool IsExpanded { get; set; }

    // Есть ли дети — для стрелочки в TreeView
    public bool HasChildren
    {
        get
        {
            if (Children.Count > 0)
            {
                return true;
            }

            return false;
        }
    }

    // Заголовок узла
    public string Title
    {
        get
        {
            return Row.GetTitle();
        }
    }

    // Количество: «×4»
    public string QuantityText
    {
        get
        {
            return "×" + Row.Quantity;
        }
    }

    // Тип объекта текстом
    public string TypeText
    {
        get
        {
            return Row.Type.ToCadString();
        }
    }

    // Масса текстом. У сборки своя масса не задаётся
    public string MassText
    {
        get
        {
            if (Row.Type == ObjectType.Assembly)
            {
                return "вычисляется";
            }

            if (Row.MassKg == null)
            {
                return "не указана";
            }

            return Row.MassKg.Value.ToString("0.###") + " кг";
        }
    }

    // Замыкающее ребро цикла
    public bool IsCycle
    {
        get
        {
            return Row.IsCycle;
        }
    }

    // Состояние версии текстом
    public string StateText
    {
        get
        {
            if (Row.State == null)
            {
                return "—";
            }

            return Row.State.Value.ToDisplay();
        }
    }
}