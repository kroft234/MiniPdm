using System.Text.Json;
using MiniPdm.Core.Cad;

namespace MiniPdm.Cad.FileSystem;

// Читает папку экспорта через файловую систему.
// Только перечисляет файлы и передаёт каждый в ICadDocumentReader.
public class FileSystemCadExportReader : ICadExportReader
{
    // Расширения файлов экспорта.
    private static readonly string[] ExportExtensions = { ".a3d", ".m3d" };

    // Читатель одного документа.
    private readonly ICadDocumentReader _documentReader;

    public FileSystemCadExportReader(ICadDocumentReader documentReader)
    {
        if (documentReader == null)
        {
            throw new ArgumentNullException(nameof(documentReader));
        }

        _documentReader = documentReader;
    }

    // Прочитать все файлы экспорта в папке.
    public async Task<IReadOnlyList<CadReadResult>> ReadFolderAsync(
        string folderPath,
        CancellationToken ct)
    {
        // Пустой путь — ошибка.
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        // Папки нет — ошибка.
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException("Папка экспорта не найдена: " + folderPath);
        }

        // Собираем имена подходящих файлов.
        List<string> names = CollectExportFileNames(folderPath);

        // Читаем каждый файл по очереди.
        var results = new List<CadReadResult>();

        for (int i = 0; i < names.Count; i++)
        {
            // Отмена операции — прекращаем.
            ct.ThrowIfCancellationRequested();

            string fileName = names[i];

            // Собираем полный путь из папки и имени.
            string path = Path.Combine(folderPath, fileName);

            try
            {
                CadDocument document = await _documentReader.ReadAsync(path, ct);
                results.Add(CadReadResult.Ok(document));
            }
            catch (OperationCanceledException)
            {
                // Отмену пробрасываем дальше.
                throw;
            }
            catch (Exception ex)
            {
                // Ошибка одного файла не срывает чтение остальных.
                results.Add(CadReadResult.Failed(fileName, DescribeError(ex)));
            }
        }

        return results;
    }

    // Собрать имена файлов экспорта и отсортировать их.
    private static List<string> CollectExportFileNames(string folderPath)
    {
        // Все файлы папки (только верхний уровень).
        string[] allFiles = Directory.GetFiles(folderPath);

        // Оставляем только нужные расширения и берём имя без пути.
        var names = new List<string>();

        for (int i = 0; i < allFiles.Length; i++)
        {
            string extension = Path.GetExtension(allFiles[i]);

            if (ExportExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(Path.GetFileName(allFiles[i]));
            }
        }

        // Сортировка по имени без учёта регистра — просто и понятно.
        names.Sort(StringComparer.OrdinalIgnoreCase);

        return names;
    }

    // Одна строка с причиной ошибки для отчёта.
    private static string DescribeError(Exception ex)
    {
        switch (ex)
        {
            case JsonException:
                return "Не удалось разобрать JSON: " + ex.Message;

            case IOException:
                return "Ошибка чтения файла: " + ex.Message;

            case UnauthorizedAccessException:
                return "Нет доступа к файлу: " + ex.Message;

            default:
                return ex.GetType().Name + ": " + ex.Message;
        }
    }
}