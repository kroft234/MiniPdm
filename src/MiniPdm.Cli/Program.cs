using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;
using MiniPdm.Core.Validation;

namespace MiniPdm.Cli;

// Консольный интерфейс Mini-PDM: импорт, расчёты, смена состояний.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка: " + ex.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);

        // Сборка зависимостей — только здесь известны конкретные реализации
        var services = new AppServices { DatabasePath = options.DatabasePath };
        IPdmStore store = services.GetService<IPdmStore>();

        await store.InitializeAsync(CancellationToken.None);

        switch (options.Command)
        {
            case "import":
                return await ImportAsync(options, services);
            case "tree":
                return await TreeAsync(options, store);
            case "spec":
                return await SpecAsync(options, store);
            case "mass":
                return await MassAsync(options, store);
            case "search":
                return await SearchAsync(options, store);
            case "roots":
                return await RootsAsync(store);
            case "approve":
                return await SetStateAsync(options, store, ObjectState.Approved);
            case "cancel":
                return await SetStateAsync(options, store, ObjectState.Cancelled);
            case "reset":
                return await ResetAsync(store);
            default:
                return UnknownCommand(options.Command);
        }
    }

    private static async Task<int> ImportAsync(CommandLineOptions options, AppServices services)
    {
        FolderImportService service = services.GetService<FolderImportService>();

        // Подписка на прогресс: обработчик — отдельный метод ниже
        service.ProgressChanged += OnProgressChanged;

        ImportResult result = await service.ImportAsync(options.Folder!, CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine();
        PrintReport(result);
        return 0;
    }

    // Обработчик прогресса: перерисовывает строку через \r
    private static void OnProgressChanged(ImportProgress p)
    {
        if (p.Total != null && p.Total.Value > 0)
        {
            string line = "  " + p.Stage + ": " + p.Processed + "/" + p.Total
                + " (" + p.GetPercent() + "%)";

            // Новая строка короче старой — добиваем пробелами,
            // иначе от старой останется хвост вроде « 0%)»
            if (line.Length < _progressLength)
            {
                line = line.PadRight(_progressLength);
            }

            _progressLength = line.Length;
            Console.Write("\r" + line);
        }
    }

    // Длина последней напечатанной строки прогресса
    private static int _progressLength;

    // Отчёт об импорте: ошибки, предупреждения, итоги
    public static void PrintReport(ImportResult result)
    {
        Console.WriteLine("------ ОТЧЁТ ОБ ИМПОРТЕ ------");
        Console.WriteLine("Папка: " + result.Validation.GetTotalFiles()
            + " файлов обработано, завершено за " + result.GetDuration().TotalMilliseconds.ToString("F0") + " мс");
        Console.WriteLine();

        IReadOnlyList<ImportIssue> issues = result.GetAllIssues();

        // Ошибки раньше предупреждений, внутри — по имени файла
        var ordered = new List<ImportIssue>(issues);
        ordered.Sort(CompareIssues);

        for (int i = 0; i < ordered.Count; i++)
        {
            ImportIssue issue = ordered[i];
            string severity = issue.Severity == IssueSeverity.Error ? "Ошибка" : "Предупреждение";
            Console.WriteLine("  " + PadRight(issue.FileName, 42) + " "
                + PadRight(severity, 14) + " " + issue.Reason);
        }

        if (ordered.Count == 0)
        {
            Console.WriteLine("  Замечаний нет.");
        }

        Console.WriteLine();
        Console.WriteLine("------ Итоги ------");
        Console.WriteLine("  Всего файлов:      " + result.Validation.GetTotalFiles());
        Console.WriteLine("  Принято:           " + result.Validation.GetAccepted());
        Console.WriteLine("  Отклонено:         " + result.Validation.GetRejected());
        Console.WriteLine("  С предупреждением: " + result.Validation.GetWithWarnings());
        Console.WriteLine("  Без замечаний:     " + result.Validation.GetAcceptedWithoutWarnings());
        Console.WriteLine();
        Console.WriteLine("------ Запись в базу ------");
        Console.WriteLine("  Создано объектов:   " + result.Applied.Created);
        Console.WriteLine("  Обновлено на месте: " + result.Applied.UpdatedInPlace);
        Console.WriteLine("  Новых версий:       " + result.Applied.NewVersions);
        Console.WriteLine("  Без изменений:      " + result.Applied.Unchanged);
    }

    private static int CompareIssues(ImportIssue left, ImportIssue right)
    {
        // Error (0) раньше Warning (1)
        int bySeverity = left.Severity.CompareTo(right.Severity);
        if (bySeverity != 0)
        {
            return bySeverity;
        }

        return string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> TreeAsync(CommandLineOptions options, IPdmStore store)
    {
        long objectId = await ResolveObjectAsync(options, store);
        if (objectId == 0)
        {
            return 1;
        }

        IReadOnlyList<BomTreeRow> rows =
            await store.GetBomTreeRowsAsync(objectId, options.MaxDepth, CancellationToken.None);

        if (rows.Count == 0)
        {
            Console.WriteLine("Дерево пусто.");
            return 0;
        }

        IReadOnlyList<CycleInfo> cycles = CycleDetector.FindCyclesInTree(rows);

        Console.WriteLine("------ ДЕРЕВО СОСТАВА (строк: " + rows.Count + ") ------");

        for (int i = 0; i < rows.Count; i++)
        {
            BomTreeRow row = rows[i];
            string indent = new string(' ', row.Level * 2);

            string mass;
            if (row.Type == ObjectType.Assembly)
            {
                mass = "вычисляется";
            }
            else if (row.MassKg == null)
            {
                mass = "НЕ УКАЗАНА";
            }
            else
            {
                mass = row.MassKg + " кг";
            }

            Console.WriteLine("  " + indent + row.GetTitle() + " ×" + row.Quantity
                + "  [" + row.Type.ToCadString() + "]  масса: " + mass);

            if (row.MassKg == null && row.Type != ObjectType.Assembly)
            {
                Console.WriteLine("  " + indent + "    ! масса не указана в документе CAD");
            }
        }

        if (cycles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("------ Циклические ссылки ------");

            for (int i = 0; i < cycles.Count; i++)
            {
                Console.WriteLine("  " + cycles[i].GetDescription());
            }
        }

        return 0;
    }

    private static async Task<int> SpecAsync(CommandLineOptions options, IPdmStore store)
    {
        long objectId = await ResolveObjectAsync(options, store);
        if (objectId == 0)
        {
            return 1;
        }

        IReadOnlyList<BomTreeRow> rows =
            await store.GetBomTreeRowsAsync(objectId, options.MaxDepth, CancellationToken.None);

        IReadOnlyList<SpecificationRow> spec = BomCalculator.BuildSpecification(rows);

        Console.WriteLine("------ СВОДНАЯ СПЕЦИФИКАЦИЯ ------");
        Console.WriteLine("  " + PadRight("Обозначение", 18) + " " + PadRight("Наименование", 38)
            + " " + PadLeft("Кол-во", 8) + " " + PadLeft("Масса, кг", 12));
        Console.WriteLine("  " + new string('-', 18) + " " + new string('-', 38)
            + " " + new string('-', 8) + " " + new string('-', 12));

        for (int i = 0; i < spec.Count; i++)
        {
            SpecificationRow row = spec[i];
            string designation = row.Designation ?? "—";
            decimal? totalMass = row.GetTotalMassKg();
            string mass = totalMass == null ? "—" : totalMass.Value.ToString("0.###");

            Console.WriteLine("  " + PadRight(designation, 18) + " "
                + PadRight(Truncate(row.Name, 38), 38) + " "
                + PadLeft(row.TotalQuantity.ToString(), 8) + " "
                + PadLeft(mass, 12));
        }

        Console.WriteLine();

        decimal known = 0;
        int unknown = 0;

        for (int i = 0; i < spec.Count; i++)
        {
            if (spec[i].GetHasMass())
            {
                known += spec[i].GetTotalMassKg()!.Value;
            }
            else
            {
                unknown++;
            }
        }

        Console.WriteLine("  Позиций: " + spec.Count + ", из них без массы: " + unknown);
        Console.WriteLine("  Суммарная известная масса: " + known.ToString("0.###") + " кг");

        if (unknown > 0)
        {
            Console.WriteLine("  ВНИМАНИЕ: строки с пустой массой — масса в документе CAD не указана.");
        }

        return 0;
    }

    private static async Task<int> MassAsync(CommandLineOptions options, IPdmStore store)
    {
        long objectId = await ResolveObjectAsync(options, store);
        if (objectId == 0)
        {
            return 1;
        }

        PdmObjectDetails? details = await store.GetDetailsAsync(objectId, CancellationToken.None);

        IReadOnlyList<BomTreeRow> rows =
            await store.GetBomTreeRowsAsync(objectId, options.MaxDepth, CancellationToken.None);

        BomTreeNode? tree = BomCalculator.BuildTree(rows);
        MassResult mass = BomCalculator.CalculateMass(tree);

        Console.WriteLine("------ РАСЧЁТ МАССЫ ------");
        Console.WriteLine("  Объект: " + (details == null ? "—" : details.Key.GetDisplay()));

        if (mass.IsComplete)
        {
            Console.WriteLine("  Масса: " + mass.TotalKg.ToString("0.###") + " кг");
        }
        else
        {
            Console.WriteLine("  Масса НЕ МОЖЕТ БЫТЬ РАССЧИТАНА: у компонентов не указана масса.");
            Console.WriteLine("  Промежуточная сумма (неполная): " + mass.TotalKg.ToString("0.###") + " кг");
            Console.WriteLine();
            Console.WriteLine("  Компоненты без массы:");

            for (int i = 0; i < mass.MissingMass.Count; i++)
            {
                MissingMass missing = mass.MissingMass[i];
                Console.WriteLine("    " + PadRight(missing.Designation ?? "—", 18) + " " + missing.Name);
                Console.WriteLine("      путь: " + missing.Path);
                Console.WriteLine("      причина: " + missing.Reason);
            }
        }

        if (mass.Cycles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Обнаружены циклические ссылки:");

            for (int i = 0; i < mass.Cycles.Count; i++)
            {
                Console.WriteLine("    " + mass.Cycles[i].GetDescription());
            }
        }

        return 0;
    }

    private static async Task<int> SearchAsync(CommandLineOptions options, IPdmStore store)
    {
        IReadOnlyList<PdmObjectSummary> results =
            await store.SearchAsync(options.Query, CancellationToken.None);

        Console.WriteLine("------ ПОИСК: «" + options.Query + "» (найдено " + results.Count + ") ------");

        for (int i = 0; i < results.Count; i++)
        {
            PdmObjectSummary item = results[i];
            string designation = item.Designation ?? "—";

            // Состояние: если версии нет — пишем «нет версии»
            string state;
            if (item.State == null)
            {
                state = "нет версии";
            }
            else
            {
                state = item.State.Value.ToDisplay();
            }

            Console.WriteLine("  " + PadRight(designation, 18) + " "
                + PadRight(Truncate(item.Name, 40), 40) + " v" + item.VersionNo + " [" + state + "]");
        }

        return 0;
    }

    private static async Task<int> RootsAsync(IPdmStore store)
    {
        IReadOnlyList<PdmObjectSummary> roots = await store.GetRootsAsync(CancellationToken.None);

        Console.WriteLine("------ КОРНЕВЫЕ ОБЪЕКТЫ (" + roots.Count + ") ------");

        for (int i = 0; i < roots.Count; i++)
        {
            PdmObjectSummary root = roots[i];
            string designation = root.Designation ?? "—";

            Console.WriteLine("  " + PadRight(root.Type.ToCadString(), 14) + " "
                + PadRight(designation, 18) + " " + Truncate(root.Name, 44));
        }

        return 0;
    }

    // Смена состояния. Проверка перехода — в домене
    private static async Task<int> SetStateAsync(
        CommandLineOptions options,
        IPdmStore store,
        ObjectState target)
    {
        if (options.All)
        {
            return await SetStateForAllAsync(store, target);
        }

        long objectId = await ResolveObjectAsync(options, store);
        if (objectId == 0)
        {
            return 1;
        }

        return await SetStateForOneAsync(store, objectId, target);
    }

    // Массовая смена состояния. Недопустимые переходы пропускаются
    private static async Task<int> SetStateForAllAsync(IPdmStore store, ObjectState target)
    {
        IReadOnlyList<PdmObjectSummary> all = await store.ListAsync(CancellationToken.None);

        int changed = 0;
        int skipped = 0;

        for (int i = 0; i < all.Count; i++)
        {
            PdmObjectDetails? details =
                await store.GetDetailsAsync(all[i].Id, CancellationToken.None);

            StoredVersion? version = details == null ? null : details.CurrentVersion;
            if (version == null)
            {
                continue;
            }

            if (!StateTransitions.CanTransition(version.State, target))
            {
                skipped++;
                continue;
            }

            await store.ChangeVersionStateAsync(version.Id, target, CancellationToken.None);
            await store.RefreshCurrentVersionAsync(all[i].Id, CancellationToken.None);
            changed++;
        }

        Console.WriteLine("Переведено в «" + target.ToDisplay() + "»: " + changed
            + ", пропущено: " + skipped);
        return 0;
    }

    private static async Task<int> SetStateForOneAsync(IPdmStore store, long objectId, ObjectState target)
    {
        PdmObjectDetails? details = await store.GetDetailsAsync(objectId, CancellationToken.None);

        StoredVersion? current = details == null ? null : details.CurrentVersion;
        if (current == null)
        {
            Console.WriteLine("У объекта нет текущей версии.");
            return 1;
        }

        string label = details!.Key.GetDisplay();

        try
        {
            StateTransitions.EnsureCanTransition(current.State, target, label);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("Переход запрещён: " + ex.Message);
            Console.WriteLine("Допустимые переходы из «" + current.State.ToDisplay() + "»: "
                + string.Join(", ", AllowedTransitionsFrom(current.State)));
            return 1;
        }

        await store.ChangeVersionStateAsync(current.Id, target, CancellationToken.None);
        await store.RefreshCurrentVersionAsync(objectId, CancellationToken.None);

        PdmObjectDetails? after = await store.GetDetailsAsync(objectId, CancellationToken.None);

        Console.WriteLine(label + ": версия " + current.VersionNo + " → «" + target.ToDisplay() + "»");

        StoredVersion? now = after == null ? null : after.CurrentVersion;
        Console.WriteLine("  Текущая версия теперь: "
            + (now == null
                ? "нет (все аннулированы)"
                : "v" + now.VersionNo + " [" + now.State.ToDisplay() + "]"));

        return 0;
    }

    private static IReadOnlyList<string> AllowedTransitionsFrom(ObjectState from)
    {
        var allowed = new List<string>();

        // GetValues возвращает Array — приводим к массиву ObjectState
        ObjectState[] candidates = (ObjectState[])Enum.GetValues(typeof(ObjectState));

        for (int i = 0; i < candidates.Length; i++)
        {
            if (StateTransitions.CanTransition(from, candidates[i]))
            {
                allowed.Add(candidates[i].ToDisplay());
            }
        }

        return allowed;
    }

    private static async Task<int> ResetAsync(IPdmStore store)
    {
        await store.DropAllAsync(CancellationToken.None);
        Console.WriteLine("База очищена.");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine("Неизвестная команда: " + command);
        PrintUsage();
        return 1;
    }

    // Ищет объект по обозначению или наименованию.
    private static async Task<long> ResolveObjectAsync(CommandLineOptions options, IPdmStore store)
    {
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            Console.Error.WriteLine("Укажите обозначение или наименование объекта.");
            return 0;
        }

        IReadOnlyList<PdmObjectSummary> found =
            await store.SearchAsync(options.Query, CancellationToken.None);

        if (found.Count == 0)
        {
            Console.Error.WriteLine("Объект «" + options.Query + "» не найден.");
            return 0;
        }

        if (found.Count > 1)
        {
            Console.Error.WriteLine("По запросу «" + options.Query + "» найдено "
                + found.Count + " объектов, уточните запрос:");

            for (int i = 0; i < found.Count; i++)
            {
                Console.Error.WriteLine("  " + PadRight(found[i].Designation ?? "—", 18)
                    + " " + found[i].Name);
            }

            return 0;
        }

        return found[0].Id;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 1)] + "…";
    }

    private static string PadRight(string value, int width)
    {
        if (value.Length >= width)
        {
            return value;
        }

        return value + new string(' ', width - value.Length);
    }

    private static string PadLeft(string value, int width)
    {
        if (value.Length >= width)
        {
            return value;
        }

        return new string(' ', width - value.Length) + value;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Mini-PDM — консольный инструмент проверки

            Использование:
              import <папка>                 Импорт папки экспорта CAD
              tree <объект>                  Дерево состава
              spec <объект>                  Сводная спецификация
              mass <объект>                  Расчёт массы сборки
              search <строка>                Поиск по обозначению или наименованию
              roots                          Корневые объекты
              approve <объект> [--all]       Утвердить текущую версию
              cancel <объект> [--all]        Аннулировать текущую версию
              reset                          Очистить базу

            Общие параметры:
              --db <путь>                    Файл базы (по умолчанию minipdm.db)
              --depth <n>                    Глубина дерева (по умолчанию 64)
              --all                          Для approve/cancel: все объекты

            Примеры:
              dotnet run --project src/MiniPdm.Cli -- import cad-export/cad-export --db demo.db
              dotnet run --project src/MiniPdm.Cli -- mass РДЦЛ.304112.300 --db demo.db
              dotnet run --project src/MiniPdm.Cli -- approve --all --db demo.db
            """);
    }
}

// Разбор аргументов командной строки
public sealed record CommandLineOptions
{
    public string Command { get; init; } = string.Empty;

    public string? Folder { get; init; }

    public string? Query { get; init; }

    public string DatabasePath { get; init; } = "minipdm.db";

    public int MaxDepth { get; init; } = 64;

    public bool All { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        var positional = new List<string>();
        string databasePath = "minipdm.db";
        int maxDepth = 64;
        bool all = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--db" && i + 1 < args.Length)
            {
                databasePath = args[i + 1];
                i++;
                continue;
            }

            if (args[i] == "--depth" && i + 1 < args.Length
                && int.TryParse(args[i + 1], out int depth))
            {
                maxDepth = depth;
                i++;
                continue;
            }

            if (args[i] == "--all")
            {
                all = true;
                continue;
            }

            positional.Add(args[i]);
        }

        string command = string.Empty;
        string? second = null;

        if (positional.Count > 0)
        {
            command = positional[0];
        }

        if (positional.Count > 1)
        {
            second = positional[1];
        }

        return new CommandLineOptions
        {
            Command = command,
            Folder = second,
            Query = second,
            DatabasePath = databasePath,
            MaxDepth = maxDepth,
            All = all
        };
    }
}