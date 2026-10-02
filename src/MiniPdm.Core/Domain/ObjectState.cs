namespace MiniPdm.Core.Domain;

// Состояние версии объекта
public enum ObjectState
{
    InWork = 0,       // «В работе»
    Approved = 1,     // «Утверждено»
    Cancelled = 2     // «Аннулировано»
}

public static class ObjectStateExtensions
{

    // Аннулированные версии исключаются, а текущие - всегда последняя неаннулированная версия.
    public static bool ParticipatesInCalculations(this ObjectState state)
    {
        return state != ObjectState.Cancelled;
    }

    //Русское имя состояния для отчётов и сообщений об ошибках.
    public static string ToDisplay(this ObjectState state)
    {
        switch (state)
        {
            case ObjectState.InWork:
                return "В работе";
            case ObjectState.Approved:
                return "Утверждено";
            case ObjectState.Cancelled:
                return "Аннулировано";
            default:
                return state.ToString();
        }
    }
}