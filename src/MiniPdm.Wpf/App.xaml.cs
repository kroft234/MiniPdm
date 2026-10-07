using System.IO;
using System.Windows;
using MiniPdm.Wpf.ViewModels;

namespace MiniPdm.Wpf;

// Точка входа WPF. Здесь выбирается файл базы и собирается контейнер.
// Окно создаётся само через StartupUri и берёт готовую модель из ViewModel.
public partial class App : Application
{
    // Окно читает это свойство в конструкторе — оно уже заполнено
    public MainViewModel? ViewModel { get; private set; }

    // База рядом с программой; путь можно задать переменной MINIPDM_DB
    private static string DefaultDatabasePath()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("MINIPDM_DB");
        if (fromEnvironment != null)
        {
            return fromEnvironment;
        }

        return Path.Combine(AppContext.BaseDirectory, "minipdm.db");
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string databasePath = DefaultDatabasePath();
        AppServices services;
        MainViewModel viewModel;

        // Сборка сервисов. Ошибка — критичная, выходим
        try
        {
            services = new AppServices { DatabasePath = databasePath };
            viewModel = services.GetService<MainViewModel>();
            ViewModel = viewModel;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось подготовить сервисы.\n\n" + ex.Message,
                "Mini-PDM — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Создание схемы и первая загрузка. Ошибка не критична — показываем и живём
        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось открыть базу «" + services.DatabasePath + "».\n\n" + ex.Message,
                "Mini-PDM — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}