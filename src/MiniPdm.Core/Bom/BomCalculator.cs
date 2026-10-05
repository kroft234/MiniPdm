using System.Globalization;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Bom;

// Расчёты по дереву состава: сборка дерева, масса, спецификация
public static class BomCalculator
{
    // Склеивает плоские строки в дерево. Возвращает корень; null — строк не было.
    public static BomTreeNode? BuildTree(IReadOnlyList<BomTreeRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        // Узел ищется по пути, а не по ObjectId
        var nodesByPath = new Dictionary<string, BomTreeNode>(StringComparer.Ordinal);
        var nodes = new List<BomTreeNode>(rows.Count);
        BomTreeNode? root = null;

        // Первый проход — создать узлы
        for (int i = 0; i < rows.Count; i++)
        {
            BomTreeRow row = rows[i];
            var node = new BomTreeNode(row);
            nodes.Add(node);
            nodesByPath[GetPathKey(row, false)] = node;

            if (row.ParentObjectId == null)
            {
                root = node;
            }
        }

        // Второй проход — расставить по родителям
        for (int i = 0; i < nodes.Count; i++)
        {
            BomTreeNode node = nodes[i];
            BomTreeRow row = node.Row;

            if (row.PathIds.Count < 2)
            {
                continue;
            }

            if (nodesByPath.TryGetValue(GetPathKey(row, true), out BomTreeNode? parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
        }

        return root;
    }

    // Ключ пути: id через запятую. parent=true — без последнего элемента (путь родителя)
    private static string GetPathKey(BomTreeRow row, bool parent)
    {
        int lastIndex = row.PathIds.Count;
        if (parent)
        {
            lastIndex = lastIndex - 1;
        }

        string key = string.Empty;
        for (int i = 0; i < lastIndex; i++)
        {
            if (i > 0)
            {
                key += ",";
            }

            key += row.PathIds[i].ToString(CultureInfo.InvariantCulture);
        }

        return key;
    }

    // Расчёт массы сборки
    public static MassResult CalculateMass(BomTreeNode? root)
    {
        if (root == null)
        {
            return MassResult.Complete(0m, Array.Empty<CycleInfo>());
        }

        IReadOnlyList<CycleInfo> cycles = CycleDetector.FindCyclesInTree(Flatten(root));
        var missing = new List<MissingMass>();
        decimal total = Accumulate(root, missing);

        if (missing.Count == 0)
        {
            return MassResult.Complete(total, cycles);
        }

        return MassResult.Incomplete(total, missing, cycles);
    }

    // Сумма масс детей узла
    private static decimal Accumulate(BomTreeNode node, List<MissingMass> missing)
    {
        decimal sum = 0m;

        for (int i = 0; i < node.Children.Count; i++)
        {
            BomTreeNode child = node.Children[i];
            BomTreeRow row = child.Row;

            if (row.Type == ObjectType.Assembly)
            {
                // Масса сборки считается из её состава
                sum += Accumulate(child, missing) * row.EdgeQuantity;
                continue;
            }

            if (row.MassKg == null)
            {
                missing.Add(new MissingMass(
                    row.Path,
                    row.Name,
                    row.Designation,
                    "масса одной штуки не указана"));
                continue;
            }

            sum += row.MassKg.Value * row.EdgeQuantity;
        }

        return sum;
    }

    public static MassResult CalculateMass(IReadOnlyList<BomTreeRow> rows)
    {
        return CalculateMass(BuildTree(rows));
    }

    public static IReadOnlyList<SpecificationRow> BuildSpecification(IReadOnlyList<BomTreeRow> rows)
    {
        // Группировка по объекту с сохранением порядка первого появления
        var order = new List<long>();
        var groups = new Dictionary<long, List<BomTreeRow>>();

        for (int i = 0; i < rows.Count; i++)
        {
            BomTreeRow row = rows[i];

            if (row.Type == ObjectType.Assembly)
            {
                continue; // сборки раскрываются до деталей
            }

            if (!groups.TryGetValue(row.ObjectId, out List<BomTreeRow>? group))
            {
                group = new List<BomTreeRow>();
                groups[row.ObjectId] = group;
                order.Add(row.ObjectId);
            }

            group.Add(row);
        }

        var result = new List<SpecificationRow>(order.Count);

        for (int i = 0; i < order.Count; i++)
        {
            List<BomTreeRow> group = groups[order[i]];
            BomTreeRow first = group[0];

            decimal? unitMass = null;
            string? material = null;
            long totalQuantity = 0;

            for (int j = 0; j < group.Count; j++)
            {
                if (unitMass == null && group[j].MassKg != null)
                {
                    unitMass = group[j].MassKg;
                }

                if (material == null && group[j].Material != null)
                {
                    material = group[j].Material;
                }

                totalQuantity += group[j].Quantity;
            }

            result.Add(new SpecificationRow(
                first.Type,
                first.Designation,
                first.Name,
                totalQuantity,
                unitMass,
                material));
        }

        // Сортировка: детали, потом ст. изделия; внутри — по обозначению/имени
        result.Sort(CompareRows);

        return result;
    }

    private static int CompareRows(SpecificationRow a, SpecificationRow b)
    {
        int byType = GetTypeOrder(a.Type).CompareTo(GetTypeOrder(b.Type));
        if (byType != 0)
        {
            return byType;
        }

        string keyA = a.Designation ?? a.Name;
        string keyB = b.Designation ?? b.Name;

        return string.Compare(keyA, keyB, StringComparison.Ordinal);
    }

    private static int GetTypeOrder(ObjectType type)
    {
        return type == ObjectType.StandardPart ? 1 : 0;
    }

    // Дерево обратно в плоский список
    public static IReadOnlyList<BomTreeRow> Flatten(BomTreeNode? root)
    {
        var result = new List<BomTreeRow>();

        if (root != null)
        {
            Walk(root, result);
        }

        return result;
    }

    private static void Walk(BomTreeNode node, List<BomTreeRow> result)
    {
        var pending = new List<BomTreeNode>();
        pending.Add(node);

        while (pending.Count > 0)
        {
            // Берём последний элемент списка.
            int lastIndex = pending.Count - 1;
            BomTreeNode current = pending[lastIndex];
            pending.RemoveAt(lastIndex);

            result.Add(current.Row);

            // Детей тоже кладём в конец списка, в обратном порядке.
            for (int i = current.Children.Count - 1; i >= 0; i--)
            {
                pending.Add(current.Children[i]);
            }
        }
    }
}