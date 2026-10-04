using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Persistence;

// Одна версия документа из базы.
public sealed class StoredVersion
{
    public StoredVersion(long id, int versionNo, ObjectState state, string? material, decimal? massKg)
    {
        Id = id;
        VersionNo = versionNo;
        State = state;
        Material = material;
        MassKg = massKg;
    }

    public long Id { get; }

    public int VersionNo { get; }

    public ObjectState State { get; }

    // Материал; null — не задан.
    public string? Material { get; }

    // Масса одной штуки в кг; null — не задана.
    public decimal? MassKg { get; }
}

// Один компонент внутри версии сборки.
public sealed class StoredComponent
{
    public StoredComponent(ObjectKey childKey, int quantity, string childFile, string? childName = null)
    {
        ChildKey = childKey;
        Quantity = quantity;
        ChildFile = childFile;
        ChildName = childName;
    }

    public ObjectKey ChildKey { get; }

    public int Quantity { get; }

    // Имя CAD-файла компонента. По нему сравниваем состав:
    // у новых объектов ещё нет ID в базе.
    public string ChildFile { get; }

    // Наименование компонента для отображения в карточке.
    // Ключ его не хранит (у деталей ключ — обозначение), поэтому идёт отдельно.
    public string? ChildName { get; }
}

// Состояние объекта в базе на момент чтения.
public sealed class StoredObject
{
    public StoredObject(
        long id,
        ObjectKey key,
        int maxVersionNo,
        StoredVersion? currentVersion,
        IReadOnlyList<StoredComponent> components)
    {
        Id = id;
        Key = key;
        MaxVersionNo = maxVersionNo;
        CurrentVersion = currentVersion;
        Components = components;
    }

    public long Id { get; }

    public ObjectKey Key { get; }

    // Максимальный номер версии — от него считаем следующий.
    public int MaxVersionNo { get; }

    // Текущая версия; null — все аннулированы.
    public StoredVersion? CurrentVersion { get; }

    public IReadOnlyList<StoredComponent> Components { get; }

    // Номер текущей версии; 0 — если её нет.
    public int GetCurrentVersionNo()
    {
        if (CurrentVersion == null)
        {
            return 0;
        }

        return CurrentVersion.VersionNo;
    }

    // Совпадает ли сохранённый состав с пришедшим.
    // Порядок не важен, поэтому сортируем оба списка одинаково и сравниваем.
    public bool HasSameComponentsAs(IReadOnlyList<ComponentWriteModel> incoming)
    {
        if (Components.Count != incoming.Count)
        {
            return false;
        }

        // Превращаем оба состава в простые списки пар "имя + количество".
        var stored = new List<(string File, int Quantity)>();
        for (int i = 0; i < Components.Count; i++)
        {
            stored.Add((Components[i].ChildFile, Components[i].Quantity));
        }

        var current = new List<(string File, int Quantity)>();
        for (int i = 0; i < incoming.Count; i++)
        {
            current.Add((incoming[i].ChildFile, incoming[i].Quantity));
        }

        // Сортируем одинаково, чтобы сравнить поэлементно.
        stored.Sort(CompareByFileAndQuantity);
        current.Sort(CompareByFileAndQuantity);

        // Ищем хотя бы одно расхождение.
        for (int i = 0; i < stored.Count; i++)
        {
            if (stored[i].File != current[i].File)
            {
                return false;
            }

            if (stored[i].Quantity != current[i].Quantity)
            {
                return false;
            }
        }

        return true;
    }

    // Сортировка: сначала по имени файла, потом по количеству.
    private static int CompareByFileAndQuantity(
        (string File, int Quantity) a,
        (string File, int Quantity) b)
    {
        int byFile = string.Compare(a.File, b.File, StringComparison.Ordinal);
        if (byFile != 0)
        {
            return byFile;
        }

        return a.Quantity.CompareTo(b.Quantity);
    }
}

// Полная карточка объекта для интерфейса: текущая версия, все версии и состав.
public sealed class PdmObjectDetails
{
    public PdmObjectDetails(
        long id,
        ObjectKey key,
        string name,
        StoredVersion? currentVersion,
        IReadOnlyList<StoredVersion> allVersions,
        IReadOnlyList<StoredComponent> components)
    {
        Id = id;
        Key = key;
        Name = name;
        CurrentVersion = currentVersion;
        AllVersions = allVersions;
        Components = components;
    }

    public long Id { get; }

    public ObjectKey Key { get; }

    // Наименование для отображения; ключ его не хранит.
    public string Name { get; }

    // Текущая версия; null — все версии аннулированы.
    public StoredVersion? CurrentVersion { get; }

    public IReadOnlyList<StoredVersion> AllVersions { get; }

    public IReadOnlyList<StoredComponent> Components { get; }
}

// Краткая карточка для списка каталога и результатов поиска.
public sealed class PdmObjectSummary
{
    public PdmObjectSummary(
        long id,
        ObjectType type,
        string? designation,
        string name,
        int versionNo,
        ObjectState? state,
        decimal? massKg,
        string? material)
    {
        Id = id;
        Type = type;
        Designation = designation;
        Name = name;
        VersionNo = versionNo;
        State = state;
        MassKg = massKg;
        Material = material;
    }

    public long Id { get; }

    public ObjectType Type { get; }

    // Обозначение; null у стандартных изделий.
    public string? Designation { get; }

    public string Name { get; }

    // Номер текущей версии; 0 — если версии нет.
    public int VersionNo { get; }

    // Состояние текущей версии; null — если версии нет.
    public ObjectState? State { get; }

    // Масса одной штуки в кг; null — не задана.
    public decimal? MassKg { get; }

    public string? Material { get; }
}