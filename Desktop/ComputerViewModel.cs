using CommunityToolkit.Mvvm.Input;
using SC.ScriptForge.Core;
using SC.ScriptForge.Infrastructure;

namespace SC.ScriptForge;

public sealed class ComputerViewModel : ObservableObject, IDisposable
{
    private readonly ComputerInventory _inventory;
    private CancellationTokenSource? _cancellation;
    private InventorySnapshot? _snapshot;
    private bool _busy;
    private string _message = "Обновите характеристики компьютера";
    public ComputerViewModel(ComputerInventory inventory)
    {
        _inventory = inventory;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
    }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }
    public InventorySnapshot? Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); RefreshCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        _cancellation = new(); IsBusy = true; Message = "Получаем данные из Windows…";
        try
        {
            Snapshot = await _inventory.RefreshAsync(_cancellation.Token);
            var failed = Snapshot.Categories.Count(c => c.Error is not null || c.Records.Any(r => r.Fields.Any(f => f.Status is DataStatus.Error or DataStatus.AccessDenied or DataStatus.Unsupported)));
            Message = $"Обновлено в {Snapshot.CapturedAt.LocalDateTime:HH:mm:ss}" + (failed > 0 ? $" · часть источников недоступна ({failed})" : " · данные получены");
        }
        catch (OperationCanceledException) { Message = "Обновление отменено · предыдущие данные сохранены"; }
        finally { IsBusy = false; _cancellation.Dispose(); _cancellation = null; }
    }
    public InventoryField? Find(string category, string key) => Snapshot?.Categories.FirstOrDefault(c => c.Id == category)?.Records.SelectMany(r => r.Fields).FirstOrDefault(f => f.Key == key);
    public void Dispose() => _cancellation?.Cancel();
}
