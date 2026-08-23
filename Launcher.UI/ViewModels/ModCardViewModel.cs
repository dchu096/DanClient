using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;

namespace Launcher.UI.ViewModels;

public sealed partial class ModCardViewModel : ViewModelBase
{
    private readonly Func<ModInfo, bool, CancellationToken, Task<ModInfo>> _setEnabled;
    private readonly Func<ModInfo, CancellationToken, Task> _uninstall;

    [ObservableProperty]
    private ModInfo _model;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    public ModCardViewModel(
        ModInfo model,
        Func<ModInfo, bool, CancellationToken, Task<ModInfo>> setEnabled,
        Func<ModInfo, CancellationToken, Task> uninstall)
    {
        _model = model;
        _isEnabled = model.IsEnabled;
        _setEnabled = setEnabled;
        _uninstall = uninstall;
        Icon = LoadIcon(model.IconPath);
    }

    public string Name => Model.Name;
    public string Version => Model.Version;
    public string Source => Model.Source;
    public string FileName => Model.FileName;
    public Bitmap? Icon { get; }
    public bool CanUninstall => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUninstall));
        UninstallCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Model = await _setEnabled(Model, IsEnabled, cancellationToken);
            IsEnabled = Model.IsEnabled;
            OnPropertyChanged(nameof(FileName));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _uninstall(Model, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Bitmap? LoadIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            return new Bitmap(iconPath);
        }
        catch
        {
            return null;
        }
    }
}
