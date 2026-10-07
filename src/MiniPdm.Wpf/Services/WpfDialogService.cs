using System.Windows;
using Microsoft.Win32;

namespace MiniPdm.Wpf.Services;

// Диалоги на средствах WPF. Весь MessageBox-код собран здесь,
// поэтому в разметке и ViewModel его нет.
public sealed class WpfDialogService : IDialogService
{
    // Выбор папки. null — пользователь отказался
    public string? PickFolder(string title)
    {
        // OpenFolderDialog есть в .NET 8, отдельный пакет не нужен
        var dialog = new OpenFolderDialog();
        dialog.Title = title;
        dialog.Multiselect = false;

        bool? result = dialog.ShowDialog();

        if (result == true)
        {
            return dialog.FolderName;
        }

        return null;
    }

    // Вопрос с подтверждением: true — нажали «Да»
    public bool Confirm(string message)
    {
        MessageBoxResult result = MessageBox.Show(
            message,
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            return true;
        }

        return false;
    }

    // Простое сообщение
    public void Info(string message)
    {
        MessageBox.Show(
            message,
            "Mini-PDM",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Сообщение об ошибке
    public void Error(string message)
    {
        MessageBox.Show(
            message,
            "Mini-PDM — ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}