namespace MiniPdm.Core.Domain;

// Правила переходов между состояниями версии. 
public static class StateTransitions
{
    // Можно ли перейти из состояния "from" в "to"
    // Переход «в то же самое состояние» (повторно утвердить утверждённое) — запрещён.
    public static bool CanTransition(ObjectState from, ObjectState to)
    {
        switch (from)
        {
            case ObjectState.InWork:
                return to == ObjectState.Approved || to == ObjectState.Cancelled;

            case ObjectState.Approved:
                return to == ObjectState.Cancelled;

            default:
                // Аннулированная версия — конечное состояние, из неё ходов нет.
                return false;
        }
    }


    //Правило, на котором строится повторный импорт: правка утверждённого
    //объекта создаёт новую версию, а не правит существующую.
    public static bool IsEditable(ObjectState state)
    {
        return state == ObjectState.InWork;
    }

    // Проверка разрешённости перехода с понятным текстом ошибки
    public static void EnsureCanTransition(ObjectState from, ObjectState to, string objectLabel)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                "Нельзя перевести " + objectLabel + " из состояния " + from.ToDisplay()
                + " в состояние " + to.ToDisplay());
        }
    }

    // Проверка редактируемости с понятным текстом ошибки
    public static void EnsureCanEdit(ObjectState state, string objectLabel)
    {
        if (!IsEditable(state))
        {
            throw new InvalidOperationException(
                objectLabel + " нельзя изменить, правка доступна только для версии «В работе»");
        }
    }
}