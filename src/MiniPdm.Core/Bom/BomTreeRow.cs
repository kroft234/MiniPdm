using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Bom;

// Одна строка результата рекурсивного запроса к базе (дерево + спецификация + масса)
public sealed class BomTreeRow
{
    public BomTreeRow(
        long objectId,
        ObjectType type,
        string name,
        int level,
        long quantity,
        long edgeQuantity,
        IReadOnlyList<long> pathIds,
        string path,
        long? versionId = null,
        string? designation = null,
        ObjectState? state = null,
        string? material = null,
        decimal? massKg = null,
        long? parentObjectId = null,
        bool isCycle = false)
    {
        ObjectId = objectId;
        Type = type;
        Name = name;
        Level = level;
        Quantity = quantity;
        EdgeQuantity = edgeQuantity;
        PathIds = pathIds;
        Path = path;
        VersionId = versionId;
        Designation = designation;
        State = state;
        Material = material;
        MassKg = massKg;
        ParentObjectId = parentObjectId;
        IsCycle = isCycle;
    }

    // Объект в базе
    public long ObjectId { get; }

    // Текущая версия объекта; null — текущей версии ещё нет
    public long? VersionId { get; }

    public ObjectType Type { get; }

    // Обозначение; null у стандартных изделий
    public string? Designation { get; }

    public string Name { get; }

    // Состояние версии; null — версии нет
    public ObjectState? State { get; }

    // Материал; null — не задан
    public string? Material { get; }

    // Собственная масса одной штуки в кг. null у сборок и у деталей без массы
    public decimal? MassKg { get; }

    // Глубина в дереве. Корень — 0
    public int Level { get; }

    public long Quantity { get; }

    // Количество на связи «родитель → этот узел» 
    public long EdgeQuantity { get; }

    // Объект непосредственно над этим узлом; null у корня
    public long? ParentObjectId { get; }

    // Идентификаторы от корня до узла — основа для обнаружения циклов
    public IReadOnlyList<long> PathIds { get; }

    public bool IsCycle { get; }

    public string Path { get; }

    // Название: обозначение + наименование
    public string GetTitle()
    {
        if (Designation == null)
        {
            return Name;
        }

        return Designation + " " + Name;
    }

    public override string ToString()
    {
        string indent = new string(' ', Level * 2);
        return "[" + indent + GetTitle() + "] ×" + Quantity;
    }
}

// Узел дерева, собранный из строк
public sealed class BomTreeNode
{
    public BomTreeNode(BomTreeRow row)
    {
        Row = row;
    }

    public BomTreeRow Row { get; }

    public List<BomTreeNode> Children { get; } = new List<BomTreeNode>();

    public BomTreeNode? Parent { get; internal set; }

    public string GetTitle()
    {
        return Row.GetTitle();
    }

    public override string ToString()
    {
        return Row.ToString();
    }
}

// Результат расчёта массы. IsComplete = false, если у кого-то нет массы
public sealed class MassResult
{
    public MassResult(
        decimal totalKg,
        bool isComplete,
        IReadOnlyList<MissingMass> missingMass,
        IReadOnlyList<CycleInfo> cycles)
    {
        TotalKg = totalKg;
        IsComplete = isComplete;
        MissingMass = missingMass;
        Cycles = cycles;
    }

    public decimal TotalKg { get; }

    // true — все компоненты имели массу и сумма точна
    public bool IsComplete { get; }

    // Компоненты без массы: путь, наименование и причина
    public IReadOnlyList<MissingMass> MissingMass { get; }

    // Циклы, найденные в поддереве
    public IReadOnlyList<CycleInfo> Cycles { get; }

    // Все массы известны
    public static MassResult Complete(decimal totalKg, IReadOnlyList<CycleInfo> cycles)
    {
        return new MassResult(totalKg, true, Array.Empty<MissingMass>(), cycles);
    }

    // Часть масс неизвестна — сумма занижена
    public static MassResult Incomplete(
        decimal partialKg,
        IReadOnlyList<MissingMass> missing,
        IReadOnlyList<CycleInfo> cycles)
    {
        return new MassResult(partialKg, false, missing, cycles);
    }
}

// Компонент без массы и причина
public sealed class MissingMass
{
    public MissingMass(string path, string name, string? designation, string reason)
    {
        Path = path;
        Name = name;
        Designation = designation;
        Reason = reason;
    }

    public string Path { get; }

    public string Name { get; }

    public string? Designation { get; }

    public string Reason { get; }
}

// Строка сводной спецификации
public sealed class SpecificationRow
{
    public SpecificationRow(
        ObjectType type,
        string? designation,
        string name,
        long totalQuantity,
        decimal? unitMassKg = null,
        string? material = null)
    {
        Type = type;
        Designation = designation;
        Name = name;
        TotalQuantity = totalQuantity;
        UnitMassKg = unitMassKg;
        Material = material;
    }

    public ObjectType Type { get; }

    public string? Designation { get; }

    public string Name { get; }

    public long TotalQuantity { get; }

    public decimal? UnitMassKg { get; }

    public string? Material { get; }

    // Масса всех штук; null — если неизвестна масса одной
    public decimal? GetTotalMassKg()
    {
        if (UnitMassKg == null)
        {
            return null;
        }

        return UnitMassKg.Value * TotalQuantity;
    }

    public bool GetHasMass()
    {
        return UnitMassKg != null;
    }

    public string GetTitle()
    {
        if (Designation == null)
        {
            return Name;
        }

        return Designation + " " + Name;
    }

    public override string ToString()
    {
        string mass = "—";
        if (UnitMassKg != null)
        {
            decimal total = UnitMassKg.Value * TotalQuantity;
            mass = total.ToString("0.###");
        }

        return GetTitle() + " ×" + TotalQuantity + " (" + mass + " кг)";
    }
}