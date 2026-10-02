namespace MiniPdm.Core.Domain;


// Версия объекта: номер, состояние, материал и масса одной штуки.

public sealed class ObjectVersion
{
    public ObjectVersion(
        long id,
        long objectId,
        int versionNo,
        ObjectState state,
        string? material, // может быть nul
        decimal? massKg, //может быть nul
        DateTimeOffset createdAt)
    {
        if (versionNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNo), versionNo, "Номер версии начинается с 1");
        }

        if (massKg is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(massKg), massKg, "Масса не может быть отрицательной");
        }

        Id = id;
        ObjectId = objectId;
        VersionNo = versionNo;
        State = state;
        Material = material;
        MassKg = massKg;
        CreatedAt = createdAt;
    }

    public long Id { get; }

    public long ObjectId { get; }

    public int VersionNo { get; }

    public ObjectState State { get; private set; }

    public string? Material { get; private set; }

    // Масса одной штуки в килограммах(null - не задан)
    public decimal? MassKg { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    // Можно ли править эту версию (только в состоянии «В работе»)
    public bool GetIsEditable()
    {
        return StateTransitions.IsEditable(State);
    }

    // Изменение атрибутов версии
    public void ChangeAttributes(string? material, decimal? massKg)
    {
        StateTransitions.EnsureCanEdit(State, "версия " + VersionNo);

        if (massKg is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(massKg), massKg, "Масса не может быть отрицательной");
        }

        Material = material;
        MassKg = massKg;
    }

    // Смена состояния — только по правилам таблицы переходов
    public void ChangeState(ObjectState target, string objectLabel)
    {
        StateTransitions.EnsureCanTransition(State, target, objectLabel);
        State = target;
    }

    public override string ToString()
    {
        return "v" + VersionNo + " [" + State.ToDisplay() + "]";
    }
}