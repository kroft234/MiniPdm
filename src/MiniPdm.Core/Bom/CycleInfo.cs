namespace MiniPdm.Core.Bom;

// Кольцевая ссылка в составе. NodeIds — имена файлов по кругу.
public sealed class CycleInfo
{
    public CycleInfo(IReadOnlyList<string> nodeIds)
    {
        NodeIds = nodeIds;
    }

    // Узлы кольца в порядке обхода.
    public IReadOnlyList<string> NodeIds { get; }

    // Описание для привязки в интерфейсе: XAML не умеет вызывать методы
    public string Description
    {
        get { 
            return GetDescription(); 
        }
    }

    // Описание вида "A → B → C → A" — последний узел повторяет первый.
    public string GetDescription()
    {
        if (NodeIds.Count == 0)
        {
            return string.Empty;
        }

        string result = string.Empty;

        for (int i = 0; i < NodeIds.Count; i++)
        {
            if (i > 0)
            {
                result += " → ";
            }

            result += NodeIds[i];
        }

        // Замыкаем кольцо первым узлом.
        result += " → " + NodeIds[0];

        return result;
    }

    public override string ToString()
    {
        return "Цикл: " + GetDescription();
    }
}