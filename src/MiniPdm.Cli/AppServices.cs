using Jab;
using MiniPdm.Cad.FileSystem;
using MiniPdm.Cad.Json;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;

namespace MiniPdm.Cli;

// Композиция приложения в одном месте.
// Jab — контейнер с генерацией кода: ошибки регистрации видны на сборке.
[ServiceProvider]
[Singleton(typeof(IPdmStore), Factory = nameof(CreateStore))]              // хранилище — через метод
[Singleton(typeof(ICadDocumentReader), typeof(JsonCadDocumentReader))]    // читатель одного CAD-файла
[Singleton(typeof(ICadExportReader), typeof(FileSystemCadExportReader))]  // читатель папки экспорта
[Singleton(typeof(FolderImportService))]                                   // сервис импорта
public sealed partial class AppServices
{
    // Путь к базе. Меняется (тесты, демо, аргументы командной строки),
    // поэтому задаётся до первого разрешения зависимостей.
    public string DatabasePath { get; set; } = "minipdm.db";

    private IPdmStore? _store;

    private IPdmStore CreateStore()
    {
        return _store ??= new MiniPdm.Data.Sqlite.SqlitePdmStore(DatabasePath);
    }
}