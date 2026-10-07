using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniPdm.Core.Bom;
using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;
using MiniPdm.Core.Persistence;
using MiniPdm.Core.Validation;
using MiniPdm.Wpf.Services;

namespace MiniPdm.Wpf.ViewModels;

// Модель главного окна. Вся логика интерфейса — здесь, а не в code-behind.
public sealed partial class MainViewModel : ObservableObject
{
    // Задержка перед поиском — защита от запроса на каждую букву
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);

    private readonly IPdmStore _store;
    private readonly FolderImportService _importService;
    private readonly IDialogService _dialogs;

    // UI-поток: прогресс импорта приходит из фонового потока
    private readonly Dispatcher _dispatcher;

    private CancellationTokenSource? _searchCts;

    public MainViewModel(IPdmStore store, FolderImportService importService, IDialogService dialogs)
    {
        _store = store;
        _importService = importService;
        _dialogs = dialogs;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _searchCts = new CancellationTokenSource();
        SearchText = string.Empty;
    }

    [ObservableProperty]
    private string _statusText = "Готово. Импортируйте папку экспорта CAD.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ObjectItemViewModel? _selectedObject;

    [ObservableProperty]
    private int _maxDepth = 64;

    public ObservableCollection<ObjectItemViewModel> Objects { get; } = new ObservableCollection<ObjectItemViewModel>();

    public ObservableCollection<BomNodeViewModel> TreeNodes { get; } = new ObservableCollection<BomNodeViewModel>();

    public ObservableCollection<SpecificationItemViewModel> Specification { get; } = new ObservableCollection<SpecificationItemViewModel>();

    public ObservableCollection<VersionItemViewModel> Versions { get; } = new ObservableCollection<VersionItemViewModel>();

    public ObservableCollection<MissingMass> MissingMass { get; } = new ObservableCollection<MissingMass>();

    public ObservableCollection<CycleInfo> Cycles { get; } = new ObservableCollection<CycleInfo>();

    // Показывается, когда объектов нет
    public bool IsEmptyList
    {
        get
        {
            if (Objects.Count == 0)
            {
                return true;
            }

            return false;
        }
    }

    public bool HasSelection
    {
        get
        {
            if (SelectedObject != null)
            {
                return true;
            }

            return false;
        }
    }

    // Заголовок правой части окна
    public string SelectionTitle
    {
        get
        {
            if (SelectedObject == null)
            {
                return "Объект не выбран";
            }

            ObjectItemViewModel item = SelectedObject;
            return item.Title + "  ·  " + item.VersionText + " [" + item.StateText + "]";
        }
    }

    // Итог расчёта массы одной строкой
    public string MassSummary
    {
        get
        {
            if (Mass == null)
            {
                return string.Empty;
            }

            MassResult mass = Mass;

            if (mass.IsComplete)
            {
                return mass.TotalKg.ToString("0.###") + " кг";
            }

            return "неполный расчёт, известно " + mass.TotalKg.ToString("0.###") + " кг";
        }
    }

    public string MassHeadline
    {
        get
        {
            if (Mass == null)
            {
                return string.Empty;
            }

            MassResult mass = Mass;

            if (mass.IsComplete)
            {
                return "Масса сборки: " + mass.TotalKg.ToString("0.###") + " кг";
            }

            return "Масса НЕ МОЖЕТ БЫТЬ РАССЧИТАНА: у компонентов не указана масса";
        }
    }

    public string SpecificationSummary
    {
        get
        {
            if (Specification.Count == 0)
            {
                return string.Empty;
            }

            int withoutMass = 0;
            decimal known = 0;

            for (int i = 0; i < Specification.Count; i++)
            {
                SpecificationItemViewModel row = Specification[i];

                if (row.TotalMassKg != null)
                {
                    known += row.TotalMassKg.Value;
                }
                else
                {
                    withoutMass++;
                }
            }

            return "Позиций: " + Specification.Count
                + ", без массы: " + withoutMass
                + ", суммарная известная масса: " + known.ToString("0.###") + " кг";
        }
    }

    // Результат последнего расчёта массы. null — расчёта не было
    public MassResult? Mass { get; private set; }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await _store.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await ReloadObjectsAsync().ConfigureAwait(true);
    }

    // Поле поиска меняется на каждое нажатие — запускаем отложенный поиск
    partial void OnSearchTextChanged(string value)
    {
        RestartSearch();
    }

    private void RestartSearch()
    {
        CancellationTokenSource previous = Interlocked.Exchange(ref _searchCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        _ = SearchAfterDelayAsync(_searchCts.Token);
    }

    // Задержка не блокирует поток: после await управление возвращается в UI-поток
    private async Task SearchAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SearchDelay, ct);
            await SearchAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Отмена — ожидаемое поведение
        }
    }

    private async Task SearchAsync(CancellationToken ct)
    {
        string query = SearchText.Trim();

        IReadOnlyList<PdmObjectSummary> found;
        if (query.Length == 0)
        {
            found = await _store.ListAsync(ct).ConfigureAwait(true);
        }
        else
        {
            found = await _store.SearchAsync(query, ct).ConfigureAwait(true);
        }

        Objects.Clear();

        for (int i = 0; i < found.Count; i++)
        {
            Objects.Add(new ObjectItemViewModel(found[i]));
        }

        OnPropertyChanged(nameof(IsEmptyList));

        if (query.Length == 0)
        {
            StatusText = "Всего объектов: " + Objects.Count;
        }
        else
        {
            StatusText = "По запросу «" + query + "» найдено: " + Objects.Count;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await ReloadObjectsAsync().ConfigureAwait(true);
    }

    // Список пересобирается целиком, поэтому выделение запоминаем заранее
    private async Task ReloadObjectsAsync()
    {
        long? selectedId = null;
        if (SelectedObject != null)
        {
            selectedId = SelectedObject.Id;
        }

        await SearchAsync(CancellationToken.None).ConfigureAwait(true);

        if (selectedId != null)
        {
            // Присваивание вызовет OnSelectedObjectChanged
            SelectedObject = FindById(selectedId.Value);
        }
    }

    private ObjectItemViewModel? FindById(long id)
    {
        for (int i = 0; i < Objects.Count; i++)
        {
            if (Objects[i].Id == id)
            {
                return Objects[i];
            }
        }

        return null;
    }

    // Пересчитывает правую часть окна под выбранный объект
    partial void OnSelectedObjectChanged(ObjectItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectionTitle));

        if (value == null)
        {
            ClearDetails();
            return;
        }

        _ = LoadDetailsAsync(value.Id);
    }

    private async Task LoadDetailsAsync(long objectId)
    {
        PdmObjectDetails? details =
            await _store.GetDetailsAsync(objectId, CancellationToken.None).ConfigureAwait(true);
        IReadOnlyList<BomTreeRow> rows =
            await _store.GetBomTreeRowsAsync(objectId, MaxDepth, CancellationToken.None).ConfigureAwait(true);

        Versions.Clear();
        Cycles.Clear();
        MissingMass.Clear();

        if (details != null)
        {
            for (int i = 0; i < details.AllVersions.Count; i++)
            {
                Versions.Add(new VersionItemViewModel(details.AllVersions[i]));
            }
        }

        // Дерево
        TreeNodes.Clear();

        BomTreeNode? root = BomCalculator.BuildTree(rows);
        if (root != null)
        {
            BomNodeViewModel rootNode = new BomNodeViewModel(root);
            rootNode.IsExpanded = true;
            TreeNodes.Add(rootNode);
        }

        // Спецификация
        Specification.Clear();

        IReadOnlyList<SpecificationRow> specRows = BomCalculator.BuildSpecification(rows);
        for (int i = 0; i < specRows.Count; i++)
        {
            Specification.Add(new SpecificationItemViewModel(specRows[i]));
        }

        OnPropertyChanged(nameof(SpecificationSummary));

        // Масса и циклы
        Mass = BomCalculator.CalculateMass(rows);

        for (int i = 0; i < Mass.MissingMass.Count; i++)
        {
            MissingMass.Add(Mass.MissingMass[i]);
        }

        IReadOnlyList<CycleInfo> cycles = CycleDetector.FindCyclesInTree(rows);
        for (int i = 0; i < cycles.Count; i++)
        {
            Cycles.Add(cycles[i]);
        }

        OnPropertyChanged(nameof(Mass));
        OnPropertyChanged(nameof(MassSummary));
        OnPropertyChanged(nameof(MassHeadline));
    }

    private void ClearDetails()
    {
        TreeNodes.Clear();
        Specification.Clear();
        Versions.Clear();
        MissingMass.Clear();
        Cycles.Clear();

        Mass = null;

        OnPropertyChanged(nameof(Mass));
        OnPropertyChanged(nameof(MassSummary));
        OnPropertyChanged(nameof(MassHeadline));
        OnPropertyChanged(nameof(SpecificationSummary));
    }


    [RelayCommand]
    private async Task ImportAsync()
    {
        string? folder = _dialogs.PickFolder("Выберите папку экспорта CAD");

        if (folder == null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Импорт: чтение " + folder;

        _importService.ProgressChanged += OnImportProgress;

        try
        {
            ImportResult result =
                await _importService.ImportAsync(folder, CancellationToken.None).ConfigureAwait(true);

            ShowImportReport(result);

            await ReloadObjectsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Импорт не выполнен: " + ex.Message;
            _dialogs.Error("Импорт не выполнен.\n\n" + ex.Message);
        }
        finally
        {
            _importService.ProgressChanged -= OnImportProgress;
            IsBusy = false;
        }
    }

    private void OnImportProgress(ImportProgress progress)
    {
        string text;

        if (progress.Total != null && progress.Total.Value > 0)
        {
            text = progress.Stage + ": " + progress.Processed + "/" + progress.Total
                + " (" + progress.GetPercent() + "%)";
        }
        else
        {
            text = progress.Stage;
        }

        // Из фонового потока — только через диспетчер
        if (_dispatcher.CheckAccess())
        {
            StatusText = text;
        }
        else
        {
            _dispatcher.BeginInvoke(() => StatusText = text);
        }
    }

    // Отчёт об импорте — тот же формат, что в CLI
    private void ShowImportReport(ImportResult result)
    {
        ValidationResult v = result.Validation;
        ImportApplyResult a = result.Applied;

        StatusText = "Импорт завершён за " + result.GetDuration().TotalMilliseconds.ToString("F0")
            + " мс: всего " + v.GetTotalFiles()
            + ", принято " + v.GetAccepted()
            + ", отклонено " + v.GetRejected()
            + ", с предупреждениями " + v.GetWithWarnings()
            + ", без замечаний " + v.GetAcceptedWithoutWarnings()
            + ". Создано " + a.Created
            + ", обновлено " + a.UpdatedInPlace
            + ", новых версий " + a.NewVersions
            + ", без изменений " + a.Unchanged + ".";

        if (v.GetRejected() == 0 && v.GetWithWarnings() == 0)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine("Принято: " + v.GetAccepted()
            + ", отклонено: " + v.GetRejected()
            + ", с предупреждениями: " + v.GetWithWarnings());

        var issues = new List<ImportIssue>(result.GetAllIssues());
        issues.Sort(CompareIssues);

        for (int i = 0; i < issues.Count; i++)
        {
            ImportIssue issue = issues[i];
            text.AppendLine();
            text.AppendLine(issue.FileName);
            text.AppendLine("  " + issue.Reason);

            if (issue.RelatedFile != null)
            {
                text.AppendLine("  связано с: " + issue.RelatedFile);
            }
        }

        _dialogs.Info(text.ToString());
    }

    // Ошибки раньше предупреждений, внутри — по имени файла
    private static int CompareIssues(ImportIssue left, ImportIssue right)
    {
        int bySeverity = left.Severity.CompareTo(right.Severity);
        if (bySeverity != 0)
        {
            return bySeverity;
        }

        return string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
    }

    // Кнопки гасятся, когда переход запрещён доменом
    private bool CanChangeState(ObjectState target)
    {
        if (SelectedObject == null || SelectedObject.State == null)
        {
            return false;
        }

        return StateTransitions.CanTransition(SelectedObject.State.Value, target);
    }

    private bool CanApprove()
    {
        if (IsBusy)
        {
            return false;
        }

        return CanChangeState(ObjectState.Approved);
    }

    private bool CanCancel()
    {
        if (IsBusy)
        {
            return false;
        }

        return CanChangeState(ObjectState.Cancelled);
    }

    [RelayCommand(CanExecute = nameof(CanApprove))]
    private async Task ApproveAsync()
    {
        await ChangeStateAsync(ObjectState.Approved).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        await ChangeStateAsync(ObjectState.Cancelled).ConfigureAwait(true);
    }

    private async Task ChangeStateAsync(ObjectState target)
    {
        if (SelectedObject == null)
        {
            return;
        }

        ObjectItemViewModel item = SelectedObject;

        PdmObjectDetails? details =
            await _store.GetDetailsAsync(item.Id, CancellationToken.None).ConfigureAwait(true);

        if (details == null || details.CurrentVersion == null)
        {
            _dialogs.Info("У объекта нет текущей версии — менять состояние нечего.");
            return;
        }

        StoredVersion version = details.CurrentVersion;

        string question = "Перевести «" + item.Title + "» версию " + version.VersionNo
            + " в состояние «" + target.ToDisplay() + "»?";

        if (!_dialogs.Confirm(question))
        {
            return;
        }

        try
        {
            // Проверка перехода — в домене
            StateTransitions.EnsureCanTransition(version.State, target, details.Key.GetDisplay());

            await _store.ChangeVersionStateAsync(version.Id, target, CancellationToken.None)
                .ConfigureAwait(true);

            await _store.RefreshCurrentVersionAsync(item.Id, CancellationToken.None)
                .ConfigureAwait(true);

            StatusText = item.Title + ": версия " + version.VersionNo
                + " → «" + target.ToDisplay() + "»";

            await ReloadObjectsAsync().ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            StatusText = "Переход запрещён: " + ex.Message;
            _dialogs.Error(ex.Message + "\n\nДопустимые переходы: "
                + AllowedTransitionsText(version.State));
        }
    }

    // Подсказка «допустимые переходы» — перебором состояний
    private static string AllowedTransitionsText(ObjectState from)
    {
        var allowed = new List<string>();

        ObjectState[] candidates = (ObjectState[])Enum.GetValues(typeof(ObjectState));

        for (int i = 0; i < candidates.Length; i++)
        {
            if (StateTransitions.CanTransition(from, candidates[i]))
            {
                allowed.Add(candidates[i].ToDisplay());
            }
        }

        return string.Join(", ", allowed);
    }
}