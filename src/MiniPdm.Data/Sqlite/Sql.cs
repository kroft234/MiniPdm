namespace MiniPdm.Data.Sqlite;

// SQL-запросы хранилища. Диалект SQLite изолирован здесь.
internal static class Sql
{
    // ── Чтение ───────────────────────────────────────────────────────────────

    // Все объекты с указателем текущей версии
    public const string ObjectsSql = @"
SELECT o.id                 AS Id,
       o.object_type        AS Type,
       o.designation        AS Designation,
       o.name               AS Name,
       o.source_file        AS SourceFile,
       o.current_version_id AS CurrentVersionId
  FROM pdm_object o";

    public const string VersionsSql = @"
SELECT v.id         AS Id,
       v.object_id  AS ObjectId,
       v.version_no AS VersionNo,
       v.state      AS State,
       v.material   AS Material,
       v.mass_kg    AS MassKg
  FROM object_version v";

    // Состав версий по списку ID родителей
    public static string ComponentsSql(IReadOnlyCollection<long> parentVersionIds)
    {
        // Пустой список: возвращаем запрос, который ничего не найдёт
        if (parentVersionIds.Count == 0)
        {
            return EmptyComponentsSql;
        }

        // ID — числа, поэтому подставляем их в текст безопасно
        string list = string.Join(", ", parentVersionIds);

        return $@"
SELECT bl.parent_version_id AS ParentVersionId,
       bl.child_object_id   AS ChildObjectId,
       bl.quantity          AS Quantity,
       co.object_type       AS ChildType,
       co.designation       AS ChildDesignation,
       co.name              AS ChildName,
       co.source_file       AS ChildFile
  FROM bom_link bl
  JOIN pdm_object co ON co.id = bl.child_object_id
 WHERE bl.parent_version_id IN ({list})";
    }

    // Заглушка на случай пустого списка: пустой результат с теми же колонками
    private const string EmptyComponentsSql = @"
SELECT 0 AS ParentVersionId, 0 AS ChildObjectId, 0 AS Quantity,
       '' AS ChildType, NULL AS ChildDesignation, '' AS ChildName, '' AS ChildFile
 WHERE 0";

    // Рекурсивный CTE: плоское дерево состава одним запросом.
    // is_cycle = 1 помечает замыкающее ребро цикла — дальше оно не раскрывается
    public static string BomTreeSql(int maxDepth)
    {
        // В SQLite нет параметра глубины рекурсии — ограничиваем условием
        int depthLimit = maxDepth <= 0 ? 64 : maxDepth;

        return $@"
WITH RECURSIVE bom AS (
    SELECT o.id                AS object_id,
           o.current_version_id AS version_id,
           o.object_type       AS object_type,
           o.designation       AS designation,
           o.name              AS name,
           v.state             AS state,
           v.material          AS material,
           v.mass_kg           AS mass_kg,
           0                   AS level,
           1                   AS quantity,
           1                   AS edge_quantity,
           NULL                AS parent_object_id,
           ',' || o.id || ','   AS path_ids,
           COALESCE(o.designation, o.name) AS path,
           0                   AS is_cycle
      FROM pdm_object o
      LEFT JOIN object_version v ON v.id = o.current_version_id
     WHERE o.id = @ObjectId

    UNION ALL

    SELECT child.id                AS object_id,
           child.current_version_id AS version_id,
           child.object_type       AS object_type,
           child.designation       AS designation,
           child.name              AS name,
           cv.state                AS state,
           cv.material             AS material,
           cv.mass_kg              AS mass_kg,
           parent.level + 1        AS level,
           parent.quantity * link.quantity AS quantity,
           link.quantity           AS edge_quantity,
           parent.object_id        AS parent_object_id,
           parent.path_ids || child.id || ',' AS path_ids,
           parent.path || ' / ' || COALESCE(child.designation, child.name) AS path,
           CASE WHEN INSTR(parent.path_ids, ',' || child.id || ',') > 0 THEN 1 ELSE 0 END AS is_cycle
      FROM bom parent
      JOIN bom_link link    ON link.parent_version_id = parent.version_id
      JOIN pdm_object child ON child.id = link.child_object_id
      LEFT JOIN object_version cv ON cv.id = child.current_version_id
     WHERE parent.level < {depthLimit}
       AND parent.is_cycle = 0
)
SELECT object_id        AS ObjectId,
       version_id       AS VersionId,
       object_type      AS Type,
       designation      AS Designation,
       name             AS Name,
       state            AS State,
       material         AS Material,
       mass_kg          AS MassKg,
       level            AS Level,
       quantity         AS Quantity,
       edge_quantity    AS EdgeQuantity,
       parent_object_id AS ParentObjectId,
       path_ids         AS PathIds,
       path             AS Path,
       is_cycle         AS IsCycle
  FROM bom
 ORDER BY level, path";
    }

    // Поиск по обозначению или наименованию
    public const string SearchSql = @"
SELECT o.id          AS Id,
       o.object_type AS Type,
       o.designation AS Designation,
       o.name        AS Name,
       v.version_no  AS VersionNo,
       v.state       AS State,
       v.mass_kg     AS MassKg,
       v.material    AS Material
  FROM pdm_object o
  LEFT JOIN object_version v ON v.id = o.current_version_id
 WHERE o.designation LIKE @Pattern ESCAPE '\'
    OR o.name        LIKE @Pattern ESCAPE '\'
 ORDER BY o.object_type, COALESCE(o.designation, o.name)";

    // Полный каталог объектов
    public const string ListSql = @"
SELECT o.id          AS Id,
       o.object_type AS Type,
       o.designation AS Designation,
       o.name        AS Name,
       v.version_no  AS VersionNo,
       v.state       AS State,
       v.mass_kg     AS MassKg,
       v.material    AS Material
  FROM pdm_object o
  LEFT JOIN object_version v ON v.id = o.current_version_id
 ORDER BY o.object_type, COALESCE(o.designation, o.name)";

    // Корни: объекты, на которые не ссылается ни одна версия состава
    public const string RootsSql = @"
SELECT o.id          AS Id,
       o.object_type AS Type,
       o.designation AS Designation,
       o.name        AS Name,
       v.version_no  AS VersionNo,
       v.state       AS State,
       v.mass_kg     AS MassKg,
       v.material    AS Material
  FROM pdm_object o
  LEFT JOIN object_version v ON v.id = o.current_version_id
 WHERE o.id NOT IN (
           SELECT bl.child_object_id
             FROM bom_link bl
             JOIN object_version pv ON pv.id = bl.parent_version_id
            WHERE pv.state <> 'Cancelled')
 ORDER BY o.object_type, COALESCE(o.designation, o.name)";

    // ── Запись ───────────────────────────────────────────────────────────────

    public const string InsertObjectSql = @"
INSERT INTO pdm_object (object_type, designation, name, source_file)
VALUES (@Type, @Designation, @Name, @SourceFile);
SELECT last_insert_rowid()";

    // current_version_id ставит триггер after insert
    public const string InsertVersionSql = @"
INSERT INTO object_version (object_id, version_no, state, material, mass_kg, created_at)
VALUES (@ObjectId, @VersionNo, @State, @Material, @MassKg, @CreatedAt);
SELECT last_insert_rowid()";

    public const string UpdateVersionSql = @"
UPDATE object_version
   SET material = @Material,
       mass_kg  = @MassKg
 WHERE id = @VersionId";

    public const string ChangeStateSql = @"
UPDATE object_version
   SET state = @State
 WHERE id = @VersionId";

    public const string InsertComponentSql = @"
INSERT INTO bom_link (parent_version_id, child_object_id, quantity)
VALUES (@ParentVersionId, @ChildObjectId, @Quantity)";

    public const string DeleteComponentsSql = @"
DELETE FROM bom_link WHERE parent_version_id = @ParentVersionId";

    // Журнал импорта. finished_at проставляет сама база
    public const string InsertLogSql = @"
INSERT INTO import_log (started_at, finished_at, source_folder, total_files, imported,
                        rejected, with_warnings, created, updated_in_place,
                        new_versions, unchanged)
VALUES (@StartedAt,
        strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now'),
        @SourceFolder, @TotalFiles, @Imported,
        @Rejected, @WithWarnings, @Created, @UpdatedInPlace, @NewVersions, @Unchanged)";

    public const string RefreshCurrentVersionSql = @"
UPDATE pdm_object
   SET current_version_id = (
           SELECT v.id
             FROM object_version v
            WHERE v.object_id = pdm_object.id
              AND v.state <> 'Cancelled'
            ORDER BY v.version_no DESC
            LIMIT 1)
 WHERE id = @ObjectId";

    // Полная очистка. Порядок важен из-за внешних ключей
    public const string DropAllSql = @"
DELETE FROM bom_link;
DELETE FROM object_version;
DELETE FROM import_log;
DELETE FROM pdm_object;
DELETE FROM sqlite_sequence";

    // Поиск объекта по обозначению (кроме стандартных изделий)
    public const string FindByDesignationSql = @"
SELECT id FROM pdm_object WHERE designation = @Designation AND object_type <> 'StandardPart'";

    public const string StandardPartsSql = @"
SELECT id AS Id, name AS Name FROM pdm_object WHERE object_type = 'StandardPart'";

    // Текущая версия объекта для обновления на месте
    public const string GetEditableVersionSql = @"
SELECT id
  FROM object_version
 WHERE object_id = @ObjectId
   AND state = 'InWork'
 ORDER BY version_no DESC
 LIMIT 1";
}