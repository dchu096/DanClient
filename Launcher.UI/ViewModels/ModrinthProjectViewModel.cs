using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.UI.Helpers;

namespace Launcher.UI.ViewModels;

public sealed partial class ModrinthProjectViewModel : ViewModelBase
{
    private readonly Func<ModrinthProject, string?, CancellationToken, Task> _install;
    private readonly Func<ModrinthProject, CancellationToken, Task<IReadOnlyList<ModrinthProjectVersion>>> _loadVersions;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingVersions = true;

    [ObservableProperty]
    private ModrinthProjectVersion? _selectedVersion;

    [ObservableProperty]
    private string _versionsStatus = "Loading versions...";

    [ObservableProperty]
    private Bitmap? _icon;

    public ModrinthProjectViewModel(
        ModrinthProject model,
        Func<ModrinthProject, string?, CancellationToken, Task> install,
        Func<ModrinthProject, CancellationToken, Task<IReadOnlyList<ModrinthProjectVersion>>> loadVersions)
    {
        Model = model;
        _install = install;
        _loadVersions = loadVersions;
        _ = LoadVersionsAsync();
        _ = LoadIconAsync();
    }

    public ModrinthProject Model { get; }
    public ObservableCollection<ModrinthProjectVersion> Versions { get; } = [];
    public string Title => Model.Title;
    public string Description => Model.Description;
    public string Author => Model.Author;
    public string AuthorText => string.IsNullOrWhiteSpace(Model.Author) ? "Unknown author" : $"by {Model.Author}";
    public int Downloads => Model.Downloads;
    public string DownloadsText => $"{Model.Downloads:N0} downloads";
    public string? IconUrl => Model.IconUrl;
    public bool HasVersions => Versions.Count > 0;
    public bool IsVersionSelectorEnabled => !IsLoadingVersions && HasVersions;
    public bool CanInstall => HasVersions && SelectedVersion is not null && !IsBusy && !IsLoadingVersions;
    public string InstallButtonText => IsBusy ? "Installing..." : "Add";

    partial void OnSelectedVersionChanged(ModrinthProjectVersion? value) =>
        InstallCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(InstallButtonText));
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingVersionsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVersionSelectorEnabled));
        InstallCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (SelectedVersion is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _install(Model, SelectedVersion.Id, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadIconAsync()
    {
        Icon = await ModrinthIconLoader.LoadAsync(Model.IconUrl);
    }

    public async Task ReloadVersionsAsync() => await LoadVersionsAsync();

    private async Task LoadVersionsAsync()
    {
        try
        {
            IsLoadingVersions = true;
            VersionsStatus = "Loading versions...";
            var versions = await _loadVersions(Model, CancellationToken.None);
            Versions.Clear();
            foreach (var version in versions)
            {
                Versions.Add(version);
            }

            SelectedVersion = Versions.FirstOrDefault();
            VersionsStatus = Versions.Count == 0
                ? "No versions for this Minecraft release."
                : $"{Versions.Count} versions";
            OnPropertyChanged(nameof(HasVersions));
            OnPropertyChanged(nameof(IsVersionSelectorEnabled));
        }
        catch (Exception ex)
        {
            VersionsStatus = ex.Message;
        }
        finally
        {
            IsLoadingVersions = false;
            InstallCommand.NotifyCanExecuteChanged();
        }
    }
}
