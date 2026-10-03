using MiniPdm.Core.Cad;

// Абстракция над CAD-системой. Работа с файлами идёт только через интерфейс,
// чтобы остальной код не знал про JSON и файловую систему.
public interface ICadDocumentReader
{
    Task<CadDocument> ReadAsync(string path, CancellationToken ct);
}

// Чтение всей папки экспорта. Отдельный порт, чтобы логика импорта
// не работала с путями. Ошибка файла не прерывает чтение, а попадает в CadReadResult.
public interface ICadExportReader
{
    Task<IReadOnlyList<CadReadResult>> ReadFolderAsync(string folderPath, CancellationToken ct);
}