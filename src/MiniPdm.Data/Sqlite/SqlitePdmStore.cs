using Dapper;
using Microsoft.Data.Sqlite;
using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Persistence;

namespace MiniPdm.Data.Sqlite;

// Хранилище на SQLite. Реализует порт IPdmStore.
public sealed class SqlitePdmStore : IPdmStore
{
    private readonly string _databasePath;

    public SqlitePdmStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Путь к файлу базы не указан", nameof(databasePath));
        }

        _databasePath = databasePath;
    }

    // Создаёт схему
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await SchemaInitializer.InitializeAsync(connection, ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString());

        await connection.OpenAsync(ct);
        return connection;
    }

    // Полный снимок базы для планирования повторного импорта.
    public async Task<IReadOnlyList<StoredObject>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        var objectRows = new List<ObjectRow>(await connection.QueryAsync<ObjectRow>(Sql.ObjectsSql));
        if (objectRows.Count == 0)
        {
            return Array.Empty<StoredObject>();
        }

        var versionRows = new List<VersionRow>(await connection.QueryAsync<VersionRow>(Sql.VersionsSql));

        // ID текущих версий — фильтр для запроса состава
        var currentVersionIds = new List<long>();
        for (int i = 0; i < objectRows.Count; i++)
        {
            if (objectRows[i].CurrentVersionId != null)
            {
                currentVersionIds.Add(objectRows[i].CurrentVersionId.Value);
            }
        }

        var componentRows = new List<ComponentRow>(
            await connection.QueryAsync<ComponentRow>(Sql.ComponentsSql(currentVersionIds)));

        // Версии по ID + максимальный номер версии по объекту
        var versionsById = new Dictionary<long, VersionRow>();
        var maxVersionByObject = new Dictionary<long, int>();

        for (int i = 0; i < versionRows.Count; i++)
        {
            VersionRow version = versionRows[i];
            versionsById[version.Id] = version;

            if (!maxVersionByObject.TryGetValue(version.ObjectId, out int max)
                || version.VersionNo > max)
            {
                maxVersionByObject[version.ObjectId] = version.VersionNo;
            }
        }

        // Компоненты по родительской версии
        var componentsByVersion = new Dictionary<long, List<ComponentRow>>();

        for (int i = 0; i < componentRows.Count; i++)
        {
            ComponentRow component = componentRows[i];

            if (!componentsByVersion.TryGetValue(component.ParentVersionId, out var list))
            {
                list = new List<ComponentRow>();
                componentsByVersion[component.ParentVersionId] = list;
            }

            list.Add(component);
        }

        // Собираем доменные объекты
        var result = new List<StoredObject>(objectRows.Count);

        for (int i = 0; i < objectRows.Count; i++)
        {
            ObjectRow row = objectRows[i];

            // Текущая версия
            StoredVersion? current = null;
            if (row.CurrentVersionId != null)
            {
                long currentId = row.CurrentVersionId.Value;
                if (versionsById.TryGetValue(currentId, out var currentRow))
                {
                    current = MapVersion(currentRow);
                }
            }

            // Компоненты текущей версии
            var components = Array.Empty<StoredComponent>();
            if (row.CurrentVersionId != null)
            {
                long versionId = row.CurrentVersionId.Value;
                if (componentsByVersion.TryGetValue(versionId, out var list))
                {
                    components = new StoredComponent[list.Count];
                    for (int j = 0; j < list.Count; j++)
                    {
                        components[j] = MapComponent(list[j]);
                    }
                }
            }

            result.Add(new StoredObject(
                row.Id,
                CreateKey(row),
                maxVersionByObject.GetValueOrDefault(row.Id),
                current,
                components));
        }

        return result;
    }

    // Дерево состава одним запросом с рекурсивным CTE.
    public async Task<IReadOnlyList<BomTreeRow>> GetBomTreeRowsAsync(
        long objectId,
        int maxDepth,
        CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        var rows = new List<BomRow>(await connection.QueryAsync<BomRow>(
            Sql.BomTreeSql(maxDepth), new { ObjectId = objectId }));

        var result = new List<BomTreeRow>(rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            result.Add(MapBomRow(rows[i]));
        }

        return result;
    }

    public async Task<PdmObjectDetails?> GetDetailsAsync(long objectId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        var obj = await connection.QuerySingleOrDefaultAsync<ObjectRow>(
            Sql.ObjectsSql + " WHERE o.id = @ObjectId", new { ObjectId = objectId });

        if (obj == null)
        {
            return null;
        }

        var versionRows = new List<VersionRow>(await connection.QueryAsync<VersionRow>(
            Sql.VersionsSql + " WHERE object_id = @ObjectId ORDER BY version_no",
            new { ObjectId = objectId }));

        var versions = new List<StoredVersion>(versionRows.Count);
        StoredVersion? current = null;

        for (int i = 0; i < versionRows.Count; i++)
        {
            VersionRow row = versionRows[i];
            var version = MapVersion(row);
            versions.Add(version);

            if (row.Id == obj.CurrentVersionId)
            {
                current = version;
            }
        }

        IReadOnlyList<StoredComponent> components = Array.Empty<StoredComponent>();

        if (obj.CurrentVersionId != null)
        {
            long currentId = obj.CurrentVersionId.Value;
            var componentRows = new List<ComponentRow>(await connection.QueryAsync<ComponentRow>(
                Sql.ComponentsSql(new[] { currentId })));

            var list = new List<StoredComponent>(componentRows.Count);
            for (int i = 0; i < componentRows.Count; i++)
            {
                list.Add(MapComponent(componentRows[i]));
            }

            components = list;
        }

        return new PdmObjectDetails(
            obj.Id,
            CreateKey(obj),
            obj.Name,
            current,
            versions,
            components);
    }

    // Поиск по обозначению или наименованию
    public async Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(
        string? query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<PdmObjectSummary>();
        }

        await using var connection = await OpenAsync(ct);

        // Экранируем символы LIKE
        string pattern = "%" + EscapeLike(query.Trim()) + "%";

        var rows = new List<SummaryRow>(await connection.QueryAsync<SummaryRow>(
            Sql.SearchSql, new { Pattern = pattern }));

        var result = new List<PdmObjectSummary>(rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            result.Add(MapSummary(rows[i]));
        }

        return result;
    }

    public async Task<IReadOnlyList<PdmObjectSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        var rows = new List<SummaryRow>(await connection.QueryAsync<SummaryRow>(Sql.ListSql));

        var result = new List<PdmObjectSummary>(rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            result.Add(MapSummary(rows[i]));
        }

        return result;
    }

    public async Task<IReadOnlyList<PdmObjectSummary>> GetRootsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        var rows = new List<SummaryRow>(await connection.QueryAsync<SummaryRow>(Sql.RootsSql));

        var result = new List<PdmObjectSummary>(rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            result.Add(MapSummary(rows[i]));
        }

        return result;
    }

    // Применяет импорт в одной транзакции
    public async Task<ImportApplyResult> ApplyImportAsync(
        IReadOnlyList<ObjectWriteModel> models,
        ImportLogEntry log,
        CancellationToken ct = default)
    {
        if (models == null)
        {
            throw new ArgumentNullException(nameof(models));
        }

        if (log == null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            // Сначала создаём объекты — на них будут ссылаться связи
            var createdIds = new Dictionary<ObjectKey, long>();

            for (int i = 0; i < models.Count; i++)
            {
                ObjectWriteModel model = models[i];
                if (model.Action == ImportAction.Create)
                {
                    ct.ThrowIfCancellationRequested();
                    createdIds[model.Key] = await CreateObjectAsync(connection, transaction, model);
                }
            }

            // Словарь «имя файла → ID» собираем после создания объектов
            var fileToId = await ResolveSourceFilesAsync(connection, transaction, models, createdIds);

            int created = 0;
            int updated = 0;
            int newVersions = 0;
            int unchanged = 0;

            for (int i = 0; i < models.Count; i++)
            {
                ObjectWriteModel model = models[i];
                ct.ThrowIfCancellationRequested();

                if (model.Action == ImportAction.Unchanged)
                {
                    unchanged++;
                    continue;
                }

                if (model.Action == ImportAction.Create)
                {
                    long objectId = createdIds[model.Key];
                    long versionId = await InsertVersionAsync(connection, transaction, objectId, model);
                    await WriteComponentsAsync(connection, transaction, versionId, model, fileToId, ct);
                    created++;
                    continue;
                }

                if (model.Action == ImportAction.UpdateInPlace)
                {
                    await UpdateInPlaceAsync(connection, transaction, model, fileToId, ct);
                    updated++;
                    continue;
                }

                if (model.Action == ImportAction.NewVersion)
                {
                    await AddVersionAsync(connection, transaction, model, fileToId, ct);
                    newVersions++;
                    continue;
                }

                throw new ArgumentOutOfRangeException(
                    nameof(model), model.Action, "Неизвестное действие импорта");
            }

            await WriteLogAsync(connection, transaction, log);
            await transaction.CommitAsync(ct);

            return new ImportApplyResult(created, updated, newVersions, unchanged);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RefreshCurrentVersionAsync(long objectId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(Sql.RefreshCurrentVersionSql, new { ObjectId = objectId });
    }

    // Смена состояния версии
    public async Task ChangeVersionStateAsync(
        long versionId,
        ObjectState state,
        CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(
            Sql.ChangeStateSql, new { VersionId = versionId, State = state.ToString() });
    }

    // Полная очистка.
    public async Task DropAllAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(Sql.DropAllSql);
    }

    private static async Task<long> CreateObjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ObjectWriteModel model)
    {
        // У стандартного изделия обозначение NULL (CHECK), ключ — наименование
        string? designation = model.Key.Type == ObjectType.StandardPart
            ? null
            : model.Key.Value;

        return await connection.QuerySingleAsync<long>(Sql.InsertObjectSql, new
        {
            Type = model.Key.Type.ToCadString(),
            Designation = designation,
            model.Name,
            SourceFile = model.SourceFile ?? string.Empty
        }, transaction);
    }

    private static async Task UpdateInPlaceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ObjectWriteModel model,
        IReadOnlyDictionary<string, long> fileToId,
        CancellationToken ct)
    {
        long objectId = await ResolveObjectIdAsync(connection, transaction, model.Key);

        // Ищем редактируемую версию
        long? editableVersionNullable = await connection.QuerySingleOrDefaultAsync<long?>(
            Sql.GetEditableVersionSql, new { ObjectId = objectId }, transaction);

        if (editableVersionNullable == null)
        {
            throw new InvalidOperationException(
                "У объекта «" + model.Key.GetDisplay() + "» нет версии «В работе», "
              + "а импорт требует обновления на месте");
        }

        long editableVersion = editableVersionNullable.Value;

        await connection.ExecuteAsync(Sql.UpdateVersionSql, new
        {
            VersionId = editableVersion,
            model.Material,
            MassKg = (double?)model.MassKg
        }, transaction);

        // Состав перезаписывается целиком
        await connection.ExecuteAsync(
            Sql.DeleteComponentsSql, new { ParentVersionId = editableVersion }, transaction);

        await WriteComponentsAsync(connection, transaction, editableVersion, model, fileToId, ct);
    }

    private static async Task AddVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ObjectWriteModel model,
        IReadOnlyDictionary<string, long> fileToId,
        CancellationToken ct)
    {
        long objectId = await ResolveObjectIdAsync(connection, transaction, model.Key);
        long versionId = await InsertVersionAsync(connection, transaction, objectId, model);
        await WriteComponentsAsync(connection, transaction, versionId, model, fileToId, ct);
    }

    private static async Task<long> InsertVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long objectId,
        ObjectWriteModel model)
    {
        return await connection.QuerySingleAsync<long>(Sql.InsertVersionSql, new
        {
            ObjectId = objectId,
            model.VersionNo,
            State = model.State.ToString(),
            model.Material,
            MassKg = (double?)model.MassKg,
            CreatedAt = DateTimeOffset.Now.ToString("O")
        }, transaction);
    }

    private static async Task<long> ResolveObjectIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ObjectKey key)
    {
        if (key.Type == ObjectType.StandardPart)
        {
            var rows = new List<StandardPartRow>(await connection.QueryAsync<StandardPartRow>(
                Sql.StandardPartsSql, transaction: transaction));

            for (int i = 0; i < rows.Count; i++)
            {
                StandardPartRow row = rows[i];
                if (string.Equals(ObjectKey.NormalizeName(row.Name), key.Value, StringComparison.Ordinal))
                {
                    return row.Id;
                }
            }
        }
        else
        {
            long? foundId = await connection.QuerySingleOrDefaultAsync<long?>(
                Sql.FindByDesignationSql, new { Designation = key.Value }, transaction);

            if (foundId != null)
            {
                return foundId.Value;
            }
        }

        throw new InvalidOperationException(
            "Объект «" + key.GetDisplay() + "» не найден в базе: "
          + "невозможно создать для него новую версию");
    }

    // Строит «имя файла → ID объекта», включая созданные в этом импорте.
    private static async Task<Dictionary<string, long>> ResolveSourceFilesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ObjectWriteModel> models,
        IReadOnlyDictionary<ObjectKey, long> createdIds)
    {
        var rows = new List<SourceFileRow>(await connection.QueryAsync<SourceFileRow>(
            "SELECT id AS Id, source_file AS SourceFile FROM pdm_object",
            transaction: transaction));

        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            SourceFileRow row = rows[i];
            if (!string.IsNullOrWhiteSpace(row.SourceFile))
            {
                map[row.SourceFile] = row.Id;
            }
        }

        // Объекты текущего импорта добавляем отдельно
        for (int i = 0; i < models.Count; i++)
        {
            ObjectWriteModel model = models[i];
            if (model.SourceFile != null && createdIds.TryGetValue(model.Key, out long id))
            {
                map[model.SourceFile] = id;
            }
        }

        return map;
    }

    private static async Task WriteComponentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long versionId,
        ObjectWriteModel model,
        IReadOnlyDictionary<string, long> fileToId,
        CancellationToken ct)
    {
        for (int i = 0; i < model.Components.Count; i++)
        {
            ComponentWriteModel component = model.Components[i];
            ct.ThrowIfCancellationRequested();

            if (!fileToId.TryGetValue(component.ChildFile, out long childId))
            {
                throw new InvalidOperationException(
                    "Компонент «" + component.ChildFile + "» не найден среди импортируемых объектов: "
                  + "ссылка на отсутствующий или отклонённый документ");
            }

            await connection.ExecuteAsync(Sql.InsertComponentSql, new
            {
                ParentVersionId = versionId,
                ChildObjectId = childId,
                component.Quantity
            }, transaction);
        }
    }

    private static async Task WriteLogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ImportLogEntry log)
    {
        await connection.ExecuteAsync(Sql.InsertLogSql, new
        {
            StartedAt = log.StartedAt.ToString("O"),
            log.SourceFolder,
            log.TotalFiles,
            log.Imported,
            log.Rejected,
            log.WithWarnings,
            log.Created,
            log.UpdatedInPlace,
            log.NewVersions,
            log.Unchanged
        }, transaction);
    }

    private static StoredVersion MapVersion(VersionRow row)
    {
        return new StoredVersion(
            row.Id,
            row.VersionNo,
            ParseState(row.State),
            row.Material,
            RoundMass(row.MassKg));
    }

    private static StoredComponent MapComponent(ComponentRow row)
    {
        return new StoredComponent(
            CreateKey(row.ChildType, row.ChildDesignation, row.ChildName),
            row.Quantity,
            row.ChildFile,
            row.ChildName);
    }

    private static PdmObjectSummary MapSummary(SummaryRow row)
    {
        ObjectState? state = null;
        if (row.State != null)
        {
            state = ParseState(row.State);
        }

        return new PdmObjectSummary(
            row.Id,
            ParseType(row.Type),
            row.Designation,
            row.Name,
            row.VersionNo ?? 0,
            state,
            RoundMass(row.MassKg),
            row.Material);
    }

    private static BomTreeRow MapBomRow(BomRow row)
    {
        ObjectState? state = null;
        if (row.State != null)
        {
            state = ParseState(row.State);
        }

        return new BomTreeRow(
            row.ObjectId,
            ParseType(row.Type),
            row.Name,
            row.Level,
            row.Quantity,
            row.EdgeQuantity,
            ParseIds(row.PathIds),
            row.Path,
            versionId: row.VersionId,
            designation: row.Designation,
            state: state,
            material: row.Material,
            massKg: RoundMass(row.MassKg),
            parentObjectId: row.ParentObjectId,
            isCycle: row.IsCycle != 0);
    }

    // Ключ объекта: обозначение у деталей и сборок, наименование у стандартных
    private static ObjectKey CreateKey(ObjectRow row)
    {
        return CreateKey(row.Type, row.Designation, row.Name);
    }

    private static ObjectKey CreateKey(string type, string? designation, string name)
    {
        ObjectType objectType = ParseType(type);
        return ObjectKey.From(objectType, designation, name);
    }

    private static IReadOnlyList<long> ParseIds(string? pathIds)
    {
        if (string.IsNullOrWhiteSpace(pathIds))
        {
            return Array.Empty<long>();
        }

        string[] parts = pathIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<long>(parts.Length);

        for (int i = 0; i < parts.Length; i++)
        {
            if (long.TryParse(parts[i], out long id) && id != 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    // Масса в SQLite — REAL(округление до 6 знаков)
    private static decimal? RoundMass(double? massKg)
    {
        if (massKg == null)
        {
            return null;
        }

        decimal value = (decimal)massKg.Value;
        return Math.Round(value, 6);
    }

    private static ObjectType ParseType(string value)
    {
        return ObjectTypeExtensions.ParseCadString(value);
    }

    private static ObjectState ParseState(string value)
    {
        switch (value)
        {
            case "InWork":
                return ObjectState.InWork;
            case "Approved":
                return ObjectState.Approved;
            case "Cancelled":
                return ObjectState.Cancelled;
            default:
                throw new InvalidOperationException("Неизвестное состояние версии в базе: " + value);
        }
    }

    // Экранирование символов
    private static string EscapeLike(string value)
    {
        string result = value.Replace("\\", "\\\\");
        result = result.Replace("%", "\\%");
        result = result.Replace("_", "\\_");

        return result;
    }

    // Строки результата
    private sealed class ObjectRow
    {
        public long Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public string? Designation { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? SourceFile { get; init; }
        public long? CurrentVersionId { get; init; }
    }

    private sealed class SourceFileRow
    {
        public long Id { get; init; }
        public string? SourceFile { get; init; }
    }

    private sealed class StandardPartRow
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    private sealed class VersionRow
    {
        public long Id { get; init; }
        public long ObjectId { get; init; }
        public int VersionNo { get; init; }
        public string State { get; init; } = string.Empty;
        public string? Material { get; init; }
        public double? MassKg { get; init; }
    }

    private sealed class ComponentRow
    {
        public long ParentVersionId { get; init; }
        public long ChildObjectId { get; init; }
        public string ChildType { get; init; } = string.Empty;
        public string? ChildDesignation { get; init; }
        public string ChildName { get; init; } = string.Empty;
        public string ChildFile { get; init; } = string.Empty;
        public int Quantity { get; init; }
    }

    private sealed class SummaryRow
    {
        public long Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public string? Designation { get; init; }
        public string Name { get; init; } = string.Empty;
        public int? VersionNo { get; init; }
        public string? State { get; init; }
        public double? MassKg { get; init; }
        public string? Material { get; init; }
    }

    private sealed class BomRow
    {
        public long ObjectId { get; init; }
        public long? VersionId { get; init; }
        public string Type { get; init; } = string.Empty;
        public string? Designation { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? State { get; init; }
        public string? Material { get; init; }
        public double? MassKg { get; init; }
        public int Level { get; init; }
        public long Quantity { get; init; }
        public long EdgeQuantity { get; init; }
        public long? ParentObjectId { get; init; }
        public string? PathIds { get; init; }
        public long IsCycle { get; init; }
        public string Path { get; init; } = string.Empty;
    }
}