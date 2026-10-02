-- Схема данных Mini-PDM «Состав изделия»

-- Объект: сборка, деталь или стандартное изделие
CREATE TABLE IF NOT EXISTS pdm_object (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    object_type        TEXT     NOT NULL CHECK (object_type IN ('Assembly', 'Part', 'StandardPart')),
    designation        TEXT     NULL,
    name               TEXT     NOT NULL,
    source_file        TEXT     NOT NULL,
    current_version_id INTEGER NULL,

    -- Обозначение: обязательно у сборок и деталей, отсутствует у стандартных изделий
    CHECK (
        (object_type = 'StandardPart' AND designation IS NULL)
        OR (object_type IN ('Assembly', 'Part') AND designation IS NOT NULL)
    )
);

-- Обозначение уникально (у стандартных изделий оно NULL — NULL не конфликтуют)
CREATE UNIQUE INDEX IF NOT EXISTS ux_pdm_object_designation
    ON pdm_object (designation);

CREATE INDEX IF NOT EXISTS ix_pdm_object_type
    ON pdm_object (object_type);

CREATE INDEX IF NOT EXISTS ix_pdm_object_current_version
    ON pdm_object (current_version_id);

-- Наименование уникально только среди стандартных изделий
CREATE UNIQUE INDEX IF NOT EXISTS ux_pdm_object_standard_part_name
    ON pdm_object (name)
    WHERE object_type = 'StandardPart';

-- Версия объекта и её атрибуты
CREATE TABLE IF NOT EXISTS object_version (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    object_id  INTEGER NOT NULL REFERENCES pdm_object (id) ON DELETE CASCADE,
    version_no INTEGER NOT NULL CHECK (version_no > 0),
    state      TEXT    NOT NULL CHECK (state IN ('InWork', 'Approved', 'Cancelled')),
    material   TEXT    NULL,
    mass_kg    REAL    NULL CHECK (mass_kg IS NULL OR mass_kg >= 0),
    created_at TEXT    NOT NULL,

    -- Номер версии уникален в пределах объекта
    UNIQUE (object_id, version_no)
);

CREATE INDEX IF NOT EXISTS ix_object_version_object
    ON object_version (object_id, version_no DESC);

CREATE INDEX IF NOT EXISTS ix_object_version_state
    ON object_version (state);

-- Связь «состоит из»: версия-родитель → дочерний объект
-- Ссылка идёт на ОБЪЕКТ, а не на версию: в расчётах берётся текущая версия
CREATE TABLE IF NOT EXISTS bom_link (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    parent_version_id INTEGER NOT NULL REFERENCES object_version (id) ON DELETE CASCADE,
    child_object_id   INTEGER NOT NULL REFERENCES pdm_object (id),
    quantity          INTEGER NOT NULL CHECK (quantity > 0),

    UNIQUE (parent_version_id, child_object_id)
);

CREATE INDEX IF NOT EXISTS ix_bom_link_parent
    ON bom_link (parent_version_id);

CREATE INDEX IF NOT EXISTS ix_bom_link_child
    ON bom_link (child_object_id);

-- Журнал импорта
CREATE TABLE IF NOT EXISTS import_log (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at       TEXT    NOT NULL,
    finished_at      TEXT    NULL,
    source_folder    TEXT    NOT NULL,
    total_files      INTEGER NOT NULL DEFAULT 0,
    imported         INTEGER NOT NULL DEFAULT 0,
    rejected         INTEGER NOT NULL DEFAULT 0,
    with_warnings    INTEGER NOT NULL DEFAULT 0,
    created          INTEGER NOT NULL DEFAULT 0,
    updated_in_place INTEGER NOT NULL DEFAULT 0,
    new_versions     INTEGER NOT NULL DEFAULT 0,
    unchanged        INTEGER NOT NULL DEFAULT 0
);

-- Текущая версия — последняя неаннулированная
-- Триггеры ниже поддерживают current_version_id автоматически

-- При вставке версии: обновить указатель, если версия новее текущей
CREATE TRIGGER IF NOT EXISTS trg_object_version_after_insert
AFTER INSERT ON object_version
BEGIN
    UPDATE pdm_object
       SET current_version_id = NEW.id
     WHERE id = NEW.object_id
       AND (NEW.state <> 'Cancelled')
       AND (current_version_id IS NULL
            OR current_version_id IN (
                    SELECT ov.id
                      FROM object_version ov
                     WHERE ov.object_id = NEW.object_id
                       AND ov.state <> 'Cancelled'
                       AND ov.version_no < NEW.version_no));
END;

-- При аннулировании: перенести указатель на предыдущую неаннулированную
CREATE TRIGGER IF NOT EXISTS trg_object_version_after_update
AFTER UPDATE OF state ON object_version
WHEN NEW.state = 'Cancelled'
BEGIN
    UPDATE pdm_object
       SET current_version_id = (
               SELECT ov.id
                 FROM object_version ov
                WHERE ov.object_id = NEW.object_id
                  AND ov.state <> 'Cancelled'
                ORDER BY ov.version_no DESC
                LIMIT 1)
     WHERE id = NEW.object_id;
END;

-- При удалении текущей версии: перенести указатель
CREATE TRIGGER IF NOT EXISTS trg_object_version_after_delete
AFTER DELETE ON object_version
BEGIN
    UPDATE pdm_object
       SET current_version_id = (
               SELECT ov.id
                 FROM object_version ov
                WHERE ov.object_id = OLD.object_id
                  AND ov.state <> 'Cancelled'
                ORDER BY ov.version_no DESC
                LIMIT 1)
     WHERE id = OLD.object_id
       AND current_version_id = OLD.id;
END;