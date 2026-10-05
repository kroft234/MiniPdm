using System.Globalization;

namespace MiniPdm.Core.Bom;

// Ищет циклы в графе «состоит из»
public static class CycleDetector
{
    // Поиск циклов в готовом графе: имя → список ссылок
    public static IReadOnlyList<CycleInfo> FindCycles(
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var found = new List<CycleInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Обходим вершины по алфавиту — результат стабильный
        var nodes = new List<string>(graph.Keys);
        nodes.Sort(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < nodes.Count; i++)
        {
            Visit(nodes[i], graph, stack, inStack, seen, found);
        }

        return found;
    }

    // Поиск циклов в плоском списке строк (из рекурсивного SQL)
    public static IReadOnlyList<CycleInfo> FindCyclesInTree(IReadOnlyList<BomTreeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var found = new List<CycleInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < rows.Count; i++)
        {
            IReadOnlyList<long> pathIds = rows[i].PathIds;

            // Ищем повтор ID в пути
            int repeatIndex = FindRepeatedIndex(pathIds);
            if (repeatIndex < 0)
            {
                continue;
            }

            // Кусок от первого вхождения повторившегося узла до конца
            var ids = new List<long>();
            for (int j = repeatIndex; j < pathIds.Count; j++)
            {
                ids.Add(pathIds[j]);
            }

            // Замыкающее ребро не считаем узлом кольца
            if (ids.Count > 1 && ids[ids.Count - 1] == ids[0])
            {
                ids.RemoveAt(ids.Count - 1);
            }

            // Каноническая форма: начинаем с наименьшего ID
            int anchor = FindMinIndex(ids);
            List<long> canonical = Rotate(ids, anchor);

            // Ключ для проверки на дубликат
            string key = BuildNumericKey(canonical);
            if (!seen.Add(key))
            {
                continue;
            }

            // Заменяем ID на названия
            found.Add(new CycleInfo(GetTitles(rows, canonical)));
        }

        return found;
    }

    // Индекс первого ID, который уже встречался раньше по пути; -1 — повторов нет
    private static int FindRepeatedIndex(IReadOnlyList<long> pathIds)
    {
        for (int i = 0; i < pathIds.Count; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (pathIds[j] == pathIds[i])
                {
                    return j;
                }
            }
        }

        return -1;
    }

    // Индекс минимального элемента списка
    private static int FindMinIndex(IReadOnlyList<long> ids)
    {
        int anchor = 0;

        for (int i = 1; i < ids.Count; i++)
        {
            if (ids[i] < ids[anchor])
            {
                anchor = i;
            }
        }

        return anchor;
    }

    // Поворот списка так, чтобы элемент anchor оказался первым
    // Пример: [C, A, B], anchor=1 → [A, B, C]
    private static List<long> Rotate(IReadOnlyList<long> ids, int anchor)
    {
        var result = new List<long>();

        // Сначала копируем от anchor до конца
        for (int i = anchor; i < ids.Count; i++)
        {
            result.Add(ids[i]);
        }

        // Потом — с начала до anchor
        for (int i = 0; i < anchor; i++)
        {
            result.Add(ids[i]);
        }

        return result;
    }

    // Числовой ключ: "1,2,3"
    private static string BuildNumericKey(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return string.Empty;
        }

        // Первый элемент — без запятой
        string key = ids[0].ToString(CultureInfo.InvariantCulture);

        // Остальные — с запятой спереди
        for (int i = 1; i < ids.Count; i++)
        {
            key += ",";
            key += ids[i].ToString(CultureInfo.InvariantCulture);
        }

        return key;
    }

    // Названия объектов по ID; неизвестный ID → "#123"
    private static IReadOnlyList<string> GetTitles(
        IReadOnlyList<BomTreeRow> rows,
        IReadOnlyList<long> ids)
    {
        var titles = new List<string>();

        for (int i = 0; i < ids.Count; i++)
        {
            string? title = FindTitle(rows, ids[i]);

            if (title == null)
            {
                title = "#" + ids[i].ToString(CultureInfo.InvariantCulture);
            }

            titles.Add(title);
        }

        return titles;
    }

    // Название объекта по ID; null — не найден
    private static string? FindTitle(IReadOnlyList<BomTreeRow> rows, long id)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].ObjectId == id)
            {
                return rows[i].GetTitle();
            }
        }

        return null;
    }

    // Обход в глубину. stack — текущий путь
    private static void Visit(
        string node,
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
        List<string> stack,
        HashSet<string> inStack,
        HashSet<string> seen,
        List<CycleInfo> found)
    {
        // Узел уже на пути — отрезаем цикл от первого вхождения
        if (inStack.Contains(node))
        {
            int start = IndexOfIgnoreCase(stack, node);

            var cycle = new List<string>();
            for (int i = start; i < stack.Count; i++)
            {
                cycle.Add(stack[i]);
            }

            AddCycle(cycle, seen, found);
            return;
        }

        // Детей нет — идти некуда
        if (!graph.TryGetValue(node, out IReadOnlyList<string> children) || children.Count == 0)
        {
            return;
        }

        // Ставим узел на путь и идём в детей
        inStack.Add(node);
        stack.Add(node);

        var sortedChildren = new List<string>(children);
        sortedChildren.Sort(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sortedChildren.Count; i++)
        {
            Visit(sortedChildren[i], graph, stack, inStack, seen, found);
        }

        // Убрали узел с пути — возвращаемся наверх
        stack.RemoveAt(stack.Count - 1);
        inStack.Remove(node);
    }

    // Добавляет цикл, если такого ещё не было
    private static void AddCycle(List<string> cycle, HashSet<string> seen, List<CycleInfo> found)
    {
        if (cycle.Count == 0)
        {
            return;
        }

        // Каноническая форма: начинаем с наименьшего имени
        int anchor = FindMinIndexIgnoreCase(cycle);
        var canonical = Rotate(cycle, anchor);

        // Ключ "A→B→C"
        string key = BuildStringKey(canonical);
        if (!seen.Add(key))
        {
            return;
        }

        found.Add(new CycleInfo(canonical));
    }

    // Индекс минимального имени без учёта регистра
    private static int FindMinIndexIgnoreCase(IReadOnlyList<string> names)
    {
        int anchor = 0;

        for (int i = 1; i < names.Count; i++)
        {
            if (string.Compare(names[i], names[anchor], StringComparison.OrdinalIgnoreCase) < 0)
            {
                anchor = i;
            }
        }

        return anchor;
    }

    // Поворот списка строк: [C, A, B], anchor=1 → [A, B, C]
    private static List<string> Rotate(IReadOnlyList<string> names, int anchor)
    {
        var result = new List<string>();

        // Сначала от anchor до конца
        for (int i = anchor; i < names.Count; i++)
        {
            result.Add(names[i]);
        }

        // Потом с начала до anchor
        for (int i = 0; i < anchor; i++)
        {
            result.Add(names[i]);
        }

        return result;
    }

    // Строковый ключ: "A→B→C"
    private static string BuildStringKey(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        // Первый элемент — без стрелки
        string key = names[0];

        // Остальные — со стрелкой спереди
        for (int i = 1; i < names.Count; i++)
        {
            key += "→";
            key += names[i];
        }

        return key;
    }

    // Индекс узла в списке без учёта регистра
    private static int IndexOfIgnoreCase(List<string> list, string node)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], node, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}