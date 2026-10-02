using System.Text.RegularExpressions;

namespace MiniPdm.Core.Domain;


// Ключ (идентификатор) объекта

public readonly record struct ObjectKey(ObjectType Type, string Value)
{
    public string GetDisplay()
    {
        if (Type == ObjectType.StandardPart)
        {
            return Value + " (стандартное изделие)";
        }

        return Value;
    }

    // Создание ключа из входящих данных. Проверяет только обязательность, формат обозначения ЕСКД проверяет валидатор документов.
    public static ObjectKey From(ObjectType type, string? designation, string? name)
    {
        if (type == ObjectType.StandardPart)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("У стандартного изделия обязательно наименование", nameof(name));
            }

            return new ObjectKey(type, NormalizeName(name));
        }

        if (string.IsNullOrWhiteSpace(designation))
        {
            throw new ArgumentException(
                "Для типа " + type.ToCadString() + " обязательно обозначение ЕСКД", nameof(designation));
        }

        return new ObjectKey(type, designation.Trim());
    }

    // Наименования стандартных изделий хранятся в нижнем регистре
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        var collapsed = Regex.Replace(trimmed, @"\s+", " ");
        return collapsed.ToLowerInvariant();
    }

    public override string ToString()
    {
        return GetDisplay();
    }
}

// Объект системы: сборка, деталь или стандартное изделие.
// Объект сам по себе неизменяем — меняются его версии.
public sealed class PdmObject
{
    public PdmObject(long id, ObjectKey key, string name, long? currentVersionId)
    {
        Id = id;
        Key = key;
        Name = name;
        CurrentVersionId = currentVersionId;
    }

    public long Id { get; }

    public ObjectKey Key { get; }

    public string Name { get; }

    // Идентификатор текущей версии — последней неаннулированной.
    public long? CurrentVersionId { get; private set; }

    public bool GetHasCurrentVersion()
    {
        return CurrentVersionId is not null;
    }

    public void SetCurrentVersion(long versionId)
    {
        CurrentVersionId = versionId;
    }

    public void ClearCurrentVersion()
    {
        CurrentVersionId = null;
    }

    public override string ToString()
    {
        return "#" + Id + " " + Key;
    }
}

// Связь состава «Состоит из»: версия сборки-родителя ссылается на дочерний объект и хранит количество
public sealed class BomLink
{
    public BomLink(long id, long parentVersionId, long childObjectId, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
                "Количество должно быть целым числом больше нуля");
        }

        Id = id;
        ParentVersionId = parentVersionId;
        ChildObjectId = childObjectId;
        Quantity = quantity;
    }

    public long Id { get; }

    public long ParentVersionId { get; }

    public long ChildObjectId { get; }

    public int Quantity { get; }

    public override string ToString()
    {
        return "v#" + ParentVersionId + " -> #" + ChildObjectId + " x " + Quantity;
    }
}