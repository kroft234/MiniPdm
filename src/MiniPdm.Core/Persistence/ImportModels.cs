using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Persistence;

// Что сделать с объектом по итогам повторного импорта.
public enum ImportAction
{

    Create = 0,// Объекта нет — создать объект и первую версию.
    UpdateInPlace = 1,// Данные изменились, текущая версия «В работе» — обновить.
    NewVersion = 2,// Данные изменились, текущая версия «Утверждено» — создать новую.
    Unchanged = 3// Данные не изменились — ничего не писать.
}

// Задание на запись одного объекта.
// Планировщик решает, что делать; хранилище просто пишет.
public sealed class ObjectWriteModel
{
    public ObjectWriteModel(
        ObjectKey key,
        string name,
        ImportAction action,
        int versionNo,
        ObjectState state,
        string? material,
        decimal? massKg,
        IReadOnlyList<ComponentWriteModel> components,
        string? sourceFile,
        long? sourceVersionId)
    {
        Key = key;
        Name = name;
        Action = action;
        VersionNo = versionNo;
        State = state;
        Material = material;
        MassKg = massKg;
        Components = components;
        SourceFile = sourceFile;
        SourceVersionId = sourceVersionId;
    }

    public ObjectKey Key { get; }

    // Наименование как в документе CAD. Ключ его не хранит (у деталей и сборок
    // ключ — обозначение), а базе имя нужно для отображения в интерфейсе.
    public string Name { get; }

    public ImportAction Action { get; }

    // Номер версии, которая станет текущей. Для Unchanged не используется.
    public int VersionNo { get; }

    public ObjectState State { get; }

    public string? Material { get; }

    public decimal? MassKg { get; }

    public IReadOnlyList<ComponentWriteModel> Components { get; }

    // Имя CAD-файла — по нему ссылки состава разрешаются в ID объектов.
    public string? SourceFile { get; }

    // ID версии, к которой относятся изменения.
    // UpdateInPlace — существующая версия; Create/NewVersion — null.
    public long? SourceVersionId { get; }
}

// Один компонент в записываемой версии.
// Ссылка идёт по имени файла, а не по ключу: у новых объектов ещё нет ID.
public sealed class ComponentWriteModel
{
    public ComponentWriteModel(string childFile, int quantity)
    {
        ChildFile = childFile;
        Quantity = quantity;
    }

    public string ChildFile { get; }

    public int Quantity { get; }
}