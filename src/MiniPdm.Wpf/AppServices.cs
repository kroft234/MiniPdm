using Jab;
using MiniPdm.Cad.FileSystem;
using MiniPdm.Cad.Json;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;
using MiniPdm.Wpf.Services;
using MiniPdm.Wpf.ViewModels;

namespace MiniPdm.Wpf;

// Композиция приложения: здесь решается, какая реализация стоит за каждым портом.
// Окно получает готовый MainViewModel и не знает про SQLite и JSON.
[ServiceProvider]
[Singleton(typeof(IPdmStore), Factory = nameof(CreateStore))]              // хранилище — через метод
[Singleton(typeof(ICadDocumentReader), typeof(JsonCadDocumentReader))]    // читатель одного CAD-файла
[Singleton(typeof(ICadExportReader), typeof(FileSystemCadExportReader))]  // читатель папки экспорта
[Singleton(typeof(FolderImportService))]                                   // сервис импорта
[Singleton(typeof(IDialogService), typeof(WpfDialogService))]              // диалоги
[Singleton(typeof(MainViewModel))]                                         // главная ViewModel
public sealed partial class AppServices
{
    // Путь к базе. Задаётся до первого запроса зависимостей
    public string DatabasePath { get; set; } = "minipdm.db";

    private IPdmStore? _store;

    private IPdmStore CreateStore()
    {
        return _store ??= new MiniPdm.Data.Sqlite.SqlitePdmStore(DatabasePath);
    }
}