using System.Reflection;
using Microsoft.Data.Sqlite;

namespace MiniPdm.Data.Sqlite;

// Создание схемы базы. Скрипт schema.sql встроен в сборку
// как ресурс, поэтому внешних файлов не нужно.
public static class SchemaInitializer
{
    // Имя ресурса внутри сборки
    public const string ResourceName = "MiniPdm.Data.Sqlite.schema.sql";

    // Асинхронно выполняет скрипт схемы
    public static async Task InitializeAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        // Читаем текст скрипта из ресурсов сборки
        string script = await ReadScriptAsync(ct);

        // Выполняем скрипт на соединении
        using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync(ct);
    }

    // Синхронно выполняет скрипт схемы
    public static void Initialize(SqliteConnection connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        string script = ReadScript();

        using var command = connection.CreateCommand();
        command.CommandText = script;
        command.ExecuteNonQuery();
    }

    // Читает скрипт из ресурсов сборки (асинхронно)
    private static async Task<string> ReadScriptAsync(CancellationToken ct)
    {
        // Текущая сборка — та, где лежит schema.sql
        Assembly assembly = Assembly.GetExecutingAssembly();

        // Открываем поток с ресурсом
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(BuildMissingResourceMessage(assembly));
        }

        // Читаем поток как текст
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    // Читает скрипт из ресурсов сборки (синхронно)
    private static string ReadScript()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(BuildMissingResourceMessage(assembly));
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Общее сообщение об ошибке, если ресурс не найден
    private static string BuildMissingResourceMessage(Assembly assembly)
    {
        // Список всех доступных ресурсов — чтобы было понятно, что искать
        string[] available = assembly.GetManifestResourceNames();

        return "Схема не найдена среди ресурсов сборки. "
             + "Ожидалось: " + ResourceName + ". "
             + "Доступно: " + string.Join(", ", available);
    }

    // Создаёт файл базы и сразу инициализирует его
    public static async Task<SqliteConnection> CreateAndInitializeAsync(
        string databasePath,
        CancellationToken ct = default)
    {
        // Папка для файла базы
        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Открываем соединение в режиме «чтение-запись-создать»
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString());

        await connection.OpenAsync(ct);
        await InitializeAsync(connection, ct);

        return connection;
    }
}