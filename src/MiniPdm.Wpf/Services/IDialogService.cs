namespace MiniPdm.Wpf.Services;

// Диалоги приложения. Порт нужен для тестов: подставляется заглушка,
// окно создавать не надо.
public interface IDialogService
{
    // Выбор папки. null — пользователь отказался
    string? PickFolder(string title);

    // Вопрос с подтверждением
    bool Confirm(string message);

    void Info(string message);

    void Error(string message);
}