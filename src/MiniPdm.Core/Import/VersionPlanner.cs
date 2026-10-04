using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Persistence;

namespace MiniPdm.Core.Import;

// Планировщик повторного импорта: решает, что делать с каждым объектом.
// Правила: не изменилось — ничего; «В работе» — обновить;
// «Утверждено» — новая версия.
// Планирование отделено от записи, поэтому тестируется без базы.
public static class VersionPlanner
{
    // План записи для одного документа. existing — null, если объекта нет.
    public static ObjectWriteModel Plan(CadDocument document, StoredObject? existing)
    {
        ArgumentNullException.ThrowIfNull(document);

        ObjectKey key = document.ToKey();
        List<ComponentWriteModel> components = BuildComponents(document);

        // Новый объект — первая версия «В работе».
        if (existing == null)
        {
            return new ObjectWriteModel(
                key, document.Name, ImportAction.Create, 1, ObjectState.InWork,
                document.GetMaterial(), document.GetMassKg(),
                components, document.FileName, null);
        }

        bool attributesChanged = !HasSameAttributes(existing, document);
        bool componentsChanged = !existing.HasSameComponentsAs(components);

        // Ничего не изменилось — база не трогается.
        if (!attributesChanged && !componentsChanged)
        {
            return new ObjectWriteModel(
                key, document.Name, ImportAction.Unchanged, existing.GetCurrentVersionNo(),
                existing.CurrentVersion?.State ?? ObjectState.InWork,
                null, null, new List<ComponentWriteModel>(),
                document.FileName, existing.CurrentVersion?.Id);
        }

        // Текущей версии нет (все аннулированы) — создаём новую.
        if (existing.CurrentVersion == null)
        {
            return CreateNewVersion(existing, key, document, components);
        }

        // Текущая «В работе» — обновляем на месте.
        if (existing.CurrentVersion.State == ObjectState.InWork)
        {
            return new ObjectWriteModel(
                key, document.Name, ImportAction.UpdateInPlace, existing.CurrentVersion.VersionNo,
                ObjectState.InWork, document.GetMaterial(), document.GetMassKg(),
                components, document.FileName, existing.CurrentVersion.Id);
        }

        // Текущая «Утверждено» — создаём новую версию.
        return CreateNewVersion(existing, key, document, components);
    }

    // Новая версия «В работе» после максимального номера.
    private static ObjectWriteModel CreateNewVersion(
        StoredObject existing,
        ObjectKey key,
        CadDocument document,
        List<ComponentWriteModel> components)
    {
        return new ObjectWriteModel(
            key, document.Name, ImportAction.NewVersion, existing.MaxVersionNo + 1,
            ObjectState.InWork, document.GetMaterial(), document.GetMassKg(),
            components, document.FileName, null);
    }

    // Совпадают ли материал и масса с документом.
    // У сборки масса считается по составу — её не сравниваем.
    private static bool HasSameAttributes(StoredObject existing, CadDocument document)
    {
        StoredVersion? current = existing.CurrentVersion;
        if (current == null)
        {
            return false;
        }

        // Материал.
        if (current.Material != document.GetMaterial())
        {
            return false;
        }

        // Сборка — масса не важна.
        if (!document.Type.RequiresOwnMass())
        {
            return true;
        }

        // Обе массы null — совпадение.
        if (current.MassKg == null && document.GetMassKg() == null)
        {
            return true;
        }

        // Одна null, другая нет — разные.
        if (current.MassKg == null || document.GetMassKg() == null)
        {
            return false;
        }

        // Допуск: разница меньше грамма — не изменение.
        decimal diff = Math.Abs(current.MassKg.Value - document.GetMassKg().Value);
        return diff < 0.000001m;
    }

    // Компоненты документа в форме записи.
    private static List<ComponentWriteModel> BuildComponents(CadDocument document)
    {
        var components = new List<ComponentWriteModel>();

        for (int i = 0; i < document.Components.Count; i++)
        {
            CadComponent component = document.Components[i];
            components.Add(new ComponentWriteModel(component.File, component.Count));
        }

        return components;
    }

    // План импорта для всего набора документов.
    public static IReadOnlyList<ObjectWriteModel> PlanAll(
        IReadOnlyList<CadDocument> documents,
        IReadOnlyList<StoredObject> storedObjects)
    {
        var plans = new List<ObjectWriteModel>();

        for (int i = 0; i < documents.Count; i++)
        {
            CadDocument document = documents[i];
            StoredObject? existing = FindExisting(document, storedObjects);
            plans.Add(Plan(document, existing));
        }

        return plans;
    }

    // Объект из базы с тем же ключом. Не найден — null.
    private static StoredObject? FindExisting(
        CadDocument document,
        IReadOnlyList<StoredObject> storedObjects)
    {
        ObjectKey key = document.ToKey();

        for (int i = 0; i < storedObjects.Count; i++)
        {
            if (storedObjects[i].Key.Equals(key))
            {
                return storedObjects[i];
            }
        }

        return null;
    }
}