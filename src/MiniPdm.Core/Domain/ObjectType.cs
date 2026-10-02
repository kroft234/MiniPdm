namespace MiniPdm.Core.Domain;

// Тип объекта
public enum ObjectType
{
    Assembly = 0,      // сборка
    Part = 1,          // деталь
    StandardPart = 2   // стандартное изделие
}

public static class ObjectTypeExtensions
{
 
    // Обозначение ЕСКД(все кроме стандартных изделий)
    public static bool RequiresDesignation(this ObjectType type)
    {
        return type != ObjectType.StandardPart;
    }

    // У кого есть собственная масса(В сборке считает массу по составу)
    public static bool RequiresOwnMass(this ObjectType type)
    {
        return type != ObjectType.Assembly;
    }

    // Материал обязателен только у деталей
    public static bool RequiresMaterial(this ObjectType type)
    {
        return type == ObjectType.Part;
    }

    // Тип в том виде, в каком он записан в документе CAD
    public static string ToCadString(this ObjectType type)
    {
        switch (type)
        {
            case ObjectType.Assembly:
                return "Assembly";
            case ObjectType.Part:
                return "Part";
            case ObjectType.StandardPart:
                return "StandardPart";
            default:
                throw new ArgumentOutOfRangeException("Неизвестный тип объекта: " + type);
        }
    }

    // Разбор типа из документа CAD ("Assembly", "Part", "StandardPart")
    public static ObjectType ParseCadString(string? value)
    {
        switch (value)
        {
            case "Assembly":
                return ObjectType.Assembly;
            case "Part":
                return ObjectType.Part;
            case "StandardPart":
                return ObjectType.StandardPart;
            default:
                throw new FormatException("Неизвестный тип объекта: " + (value ?? "<не указан>"));
        }
    }
}