using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IVersionManifestService _versionManifestService;
    private readonly IMinecraftInstallationService _minecraftInstallationService;
    private readonly IModFileService _modFileService;
    private readonly IFabricInstallerService _fabricInstallerService;
    private readonly IJavaLauncherService _javaLauncherService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAccountSessionService _accountSessionService;
    private readonly ILauncherProfileService _profileService;
    private readonly IModrinthService _modrinthService;
    private readonly IDiscordPresenceService _discordPresenceService;
    private readonly IJavaRuntimeService _javaRuntimeService;
    private readonly CancellationTokenSource _lifetime = new();
    private MinecraftAccount? _minecraftAccount;
    private bool _isApplyingProfile;
    private bool _isGameRunning;

    [ObservableProperty]
    private LauncherProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanExecutePrimaryAction))]
    private MinecraftVersion? _selectedVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsModsVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlayTabActive))]
    [NotifyPropertyChangedFor(nameof(IsModsTabActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTabActive))]
    private string _activeTab = "Play";

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private bool _isCreateProfileVisible;

    [ObservableProperty]
    private bool _includeSnapshots;

    [ObservableProperty]
    private bool _installPerformanceMods = true;

    [ObservableProperty]
    private int _maxMemoryMegabytes = 4096;

    [ObservableProperty]
    private string _javaExecutable = string.Empty;

    [ObservableProperty]
    private string _extraJvmArgsText = string.Empty;

    [ObservableProperty]
    private bool _discordPresenceEnabled = true;

    [ObservableProperty]
    private string _discordStatusText = "Discord Rich Presence is not configured.";

    [ObservableProperty]
    private bool _hideModAlphaVersions;

    [ObservableProperty]
    private bool _hideModBetaVersions;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(CanExecutePrimaryAction))]
    [NotifyPropertyChangedFor(nameof(PlayHeadline))]
    [NotifyPropertyChangedFor(nameof(PlaySubtitle))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateReady))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateInstalling))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateRunning))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateCrashed))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayHeadline))]
    [NotifyPropertyChangedFor(nameof(PlaySubtitle))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateReady))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateInstalling))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateRunning))]
    [NotifyPropertyChangedFor(nameof(IsPlayStateCrashed))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(CanExecutePrimaryAction))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    private PlayScreenState _playScreenState = PlayScreenState.Ready;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayStateCrashed))]
    private string _crashDetails = string.Empty;

    private string _statusText = "Ready";

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, RedactDisplayText(value))
                && PlayScreenState == PlayScreenState.Installing)
            {
                OnPropertyChanged(nameof(PlaySubtitle));
            }
        }
    }

    [ObservableProperty]
    private string _accountName = "Sign in to play";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSignInCode))]
    private string _signInCode = string.Empty;

    [ObservableProperty]
    private string _modrinthQuery = string.Empty;

    [ObservableProperty]
    private ModrinthSortOption? _selectedModrinthSort;

    [ObservableProperty]
    private string _modrinthBrowseSummary = "Browse Fabric mods from Modrinth.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreModrinthCommand))]
    private bool _canLoadMoreModrinth;

    [ObservableProperty]
    private bool _isBrowsingModrinth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModrinthBrowseToggleText))]
    [NotifyPropertyChangedFor(nameof(InstalledModsPanelBorderThickness))]
    [NotifyPropertyChangedFor(nameof(ModrinthBrowsePanelWidth))]
    private bool _isModrinthBrowseVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameFilesActionVisible))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    private bool _isGameFilesInstalled;

    [ObservableProperty]
    private string _gameFilesMessage = "Choose a version to check files.";

    [ObservableProperty]
    private string _gameFilesActionText = "Install";

    public MainWindowViewModel(
        IVersionManifestService versionManifestService,
        IMinecraftInstallationService minecraftInstallationService,
        IModFileService modFileService,
        IFabricInstallerService fabricInstallerService,
        IJavaLauncherService javaLauncherService,
        IAuthenticationService authenticationService,
        IAccountSessionService accountSessionService,
        ILauncherProfileService profileService,
        IModrinthService modrinthService,
        IDiscordPresenceService discordPresenceService,
        IJavaRuntimeService javaRuntimeService)
    {
        _versionManifestService = versionManifestService;
        _minecraftInstallationService = minecraftInstallationService;
        _modFileService = modFileService;
        _fabricInstallerService = fabricInstallerService;
        _javaLauncherService = javaLauncherService;
        _authenticationService = authenticationService;
        _accountSessionService = accountSessionService;
        _profileService = profileService;
        _modrinthService = modrinthService;
        _discordPresenceService = discordPresenceService;
        _javaRuntimeService = javaRuntimeService;
        _discordPresenceService.IsEnabled = DiscordPresenceEnabled;
        _modFileService.ModsChanged += OnModsChanged;
        ApplyDiscordSettings();
        SelectedModrinthSort = ModrinthSortOptions[0];
        var uiSettings = LauncherUiSettingsStore.Load();
        IsModrinthBrowseVisible = uiSettings.ShowModrinthBrowse;
        HideModAlphaVersions = uiSettings.HideModAlphaVersions;
        HideModBetaVersions = uiSettings.HideModBetaVersions;
    }

    private const int ModrinthPageSize = 20;
    private int _modrinthBrowseOffset;
    private int _modrinthTotalHits;

    public IReadOnlyList<ModrinthSortOption> ModrinthSortOptions { get; } =
    [
        new ModrinthSortOption("Popular", "downloads"),
        new ModrinthSortOption("Recently updated", "updated"),
        new ModrinthSortOption("New", "new"),
        new ModrinthSortOption("Relevance", "relevance")
    ];
    public string ModrinthBrowseHeader => SelectedVersion is null
        ? "Browse Modrinth"
        : $"Browse Modrinth · Fabric · {SelectedVersion.Id}";

    public string ModrinthBrowseToggleText => IsModrinthBrowseVisible ? "Hide browse" : "Show browse";

    public double ModrinthBrowsePanelWidth => IsModrinthBrowseVisible ? 420 : 0;

    public Thickness InstalledModsPanelBorderThickness =>
        IsModrinthBrowseVisible ? new Thickness(0, 0, 1, 0) : new Thickness(0);

    public ObservableCollection<LauncherProfile> Profiles { get; } = [];
    public ObservableCollection<MinecraftVersion> Versions { get; } = [];
    public ObservableCollection<ModCardViewModel> Mods { get; } = [];
    public ObservableCollection<ModrinthProjectViewModel> ModrinthResults { get; } = [];

    public bool IsDashboardVisible => ActiveTab == "Play";
    public bool IsModsVisible => ActiveTab == "Mods";
    public bool IsSettingsVisible => ActiveTab == "Settings";
    public bool IsPlayTabActive => ActiveTab == "Play";
    public bool IsModsTabActive => ActiveTab == "Mods";
    public bool IsSettingsTabActive => ActiveTab == "Settings";
    public string ModsDirectory => ToDisplayPath(_modFileService.ModsDirectory);
    public string SelectedVersionName => SelectedVersion?.DisplayName ?? "Select a version";
    public string SelectedProfileName => SelectedProfile?.Name ?? "No profile";
    public string SelectedJavaText => SelectedVersion is null
        ? "Java: —"
        : $"Java: {RequiredJavaFeatureVersion}";
    public string PlayHeadline => PlayScreenState switch
    {
        PlayScreenState.Installing => "Installing",
        PlayScreenState.Running => "Running",
        PlayScreenState.Crashed => "Crashed",
        _ => "Ready to play"
    };
    public string PlaySubtitle => PlayScreenState switch
    {
        PlayScreenState.Installing => StatusText,
        PlayScreenState.Running => "Minecraft is running. Close the game to return here.",
        PlayScreenState.Crashed => StatusText,
        _ => "Select your version and profile below, then press the button to install or launch."
    };
    public bool IsPlayStateReady => PlayScreenState == PlayScreenState.Ready;
    public bool IsPlayStateInstalling => PlayScreenState == PlayScreenState.Installing;
    public bool IsPlayStateRunning => PlayScreenState == PlayScreenState.Running;
    public bool IsPlayStateCrashed => PlayScreenState == PlayScreenState.Crashed;
    public string InstalledModsTitle => SelectedVersion is null
        ? "Installed mods"
        : $"Installed mods for {SelectedVersion.Id}";
    public bool HasSignInCode => !string.IsNullOrWhiteSpace(SignInCode);
    public bool IsSignedIn => _minecraftAccount is not null;
    public bool IsSignInVisible => !IsSignedIn;
    public bool CanDeleteProfile => Profiles.Count > 1;
    public string AccountInitial => GetAccountInitial(AccountName);
    public int RequiredJavaFeatureVersion => _javaRuntimeService.GetRequiredJavaFeatureVersion(
        SelectedVersion?.Id,
        GetGameDirectoryForCurrentSelection());
    public string RequiredJavaText => SelectedVersion is null
        ? "Select a Minecraft version to see the required Java runtime."
        : $"Minecraft {SelectedVersion.Id} uses Java {RequiredJavaFeatureVersion}. DanClient downloads and keeps separate {JavaVersionResolver.GetSupportedVersionsText()} installs so you can run different versions side by side.";
    public string InstalledJavaText
    {
        get
        {
            var installed = _javaRuntimeService.GetInstalledJavaVersions();
            if (installed.Count == 0)
            {
                return "Bundled Java: none installed yet.";
            }

            return "Installed: " + string.Join(", ", installed.Select(version => $"Java {version}"));
        }
    }
    public bool IsGameFilesActionVisible => SelectedVersion is not null && !IsGameFilesInstalled;
    public string LaunchButtonText => PrimaryActionText;

    public string PrimaryActionText
    {
        get
        {
            if (_isGameRunning) return "RUNNING";
            if (SelectedVersion is null) return "SELECT VERSION";
            if (!IsGameFilesInstalled) return "INSTALL";
            if (!IsSignedIn) return "SIGN IN";
            return "LAUNCH";
        }
    }

    public bool CanExecutePrimaryAction => SelectedVersion is not null && !IsBusy && !_isGameRunning;

    [RelayCommand(CanExecute = nameof(CanExecutePrimaryAction))]
    private async Task PrimaryActionAsync()
    {
        if (SelectedVersion is null)
        {
            StatusText = "Choose a Minecraft version first.";
            return;
        }

        if (!IsGameFilesInstalled)
        {
            await InstallGameFilesAsync();
            return;
        }

        if (!IsSignedIn)
        {
            await SignInAsync();
            return;
        }

        await LaunchCoreAsync(_minecraftAccount!);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadAccountAsync();
        await LoadProfilesAsync();
        await Task.WhenAll(RefreshVersionsAsync(), RefreshModsAsync());
        await RefreshInstallStatusAsync();
        RefreshJavaStatus();
        await RefreshDiscordStatusAsync();
        await UpdateDiscordIdleAsync();
    }

    [RelayCommand]
    private async Task RefreshVersionsAsync()
    {
        await RunBusyAsync("Refreshing Minecraft versions.", async token =>
        {
            var versions = await _versionManifestService.GetVersionsAsync(IncludeSnapshots, cancellationToken: token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Versions.Clear();
                foreach (var version in versions)
                {
                    Versions.Add(version);
                }

                var preferred = SelectedProfile?.MinecraftVersionId;
                SelectedVersion = Versions.FirstOrDefault(version => version.Id == preferred)
                                  ?? Versions.FirstOrDefault();
            });
            StatusText = $"Loaded {versions.Count} Minecraft versions.";
        });
    }

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        var mods = await _modFileService.LoadModsAsync(_lifetime.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Mods.Clear();
            foreach (var mod in mods)
            {
                Mods.Add(new ModCardViewModel(
                    mod,
                    _modFileService.SetEnabledAsync,
                    UninstallModAsync));
            }

            OnPropertyChanged(nameof(ModsDirectory));
        });
        StatusText = $"Tracking {mods.Count} mods in {ModsDirectory}.";
    }

    private async Task UninstallModAsync(ModInfo mod, CancellationToken cancellationToken)
    {
        await _modFileService.DeleteModAsync(mod, cancellationToken);
        await RefreshModsAsync();
        StatusText = $"Removed {mod.Name}.";
    }

    [RelayCommand]
    private void SelectDashboard() => ActiveTab = "Play";

    [RelayCommand]
    private void SelectMods()
    {
        ActiveTab = "Mods";
        _ = EnsureModrinthBrowseAsync();
    }

    [RelayCommand]
    private void SelectSettings() => ActiveTab = "Settings";

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        await RunBusyAsync("Saving profile.", async token =>
        {
            await SaveCurrentProfileCoreAsync(token);
            StatusText = $"Saved profile {SelectedProfileName}.";
        });
    }

    [RelayCommand]
    private void CreateProfile()
    {
        NewProfileName = $"Profile {Profiles.Count + 1}";
        IsCreateProfileVisible = true;
    }

    [RelayCommand]
    private void CancelCreateProfile()
    {
        IsCreateProfileVisible = false;
        NewProfileName = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmCreateProfileAsync()
    {
        await RunBusyAsync("Creating profile.", async token =>
        {
            var id = $"profile-{Guid.NewGuid():N}"[..20];
            var name = string.IsNullOrWhiteSpace(NewProfileName)
                ? $"Profile {Profiles.Count + 1}"
                : NewProfileName.Trim();
            var profile = new LauncherProfile(
                id,
                name,
                SelectedVersion?.Id,
                IncludeSnapshots,
                InstallPerformanceMods,
                Math.Clamp(MaxMemoryMegabytes, 1024, 32768),
                string.IsNullOrWhiteSpace(JavaExecutable) ? string.Empty : JavaExecutable.Trim(),
                ExtraJvmArgsText.Trim(),
                AppPaths.GetProfileInstance(id, SelectedVersion?.Id));

            await _profileService.SaveProfileAsync(profile, select: true, token);
            IsCreateProfileVisible = false;
            NewProfileName = string.Empty;
            await LoadProfilesAsync();
            await RefreshInstallStatusAsync();
            StatusText = $"Created profile {profile.Name}.";
        });
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null || !CanDeleteProfile)
        {
            StatusText = "Keep at least one profile.";
            return;
        }

        var deletedName = SelectedProfile.Name;
        await RunBusyAsync($"Deleting profile {deletedName}.", async token =>
        {
            var next = await _profileService.DeleteProfileAsync(SelectedProfile.Id, token);
            await LoadProfilesAsync();
            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == next.Id) ?? Profiles.FirstOrDefault();
            await RefreshModsAsync();
            await RefreshInstallStatusAsync();
            StatusText = $"Deleted profile {deletedName}. Game files were kept on disk.";
        });
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        _modFileService.OpenModsFolder();
        StatusText = $"Opened {ModsDirectory}.";
    }

    [RelayCommand]
    private void ToggleModrinthBrowse() => IsModrinthBrowseVisible = !IsModrinthBrowseVisible;

    partial void OnIsModrinthBrowseVisibleChanged(bool value) => SaveUiSettings();

    partial void OnHideModAlphaVersionsChanged(bool value)
    {
        SaveUiSettings();
        _ = RefreshModrinthVersionListsAsync();
    }

    partial void OnHideModBetaVersionsChanged(bool value)
    {
        SaveUiSettings();
        _ = RefreshModrinthVersionListsAsync();
    }

    private void SaveUiSettings() =>
        LauncherUiSettingsStore.Save(new LauncherUiSettings(
            IsModrinthBrowseVisible,
            HideModAlphaVersions,
            HideModBetaVersions));

    private ModrinthVersionFilter CreateModrinthVersionFilter() =>
        new(HideModAlphaVersions, HideModBetaVersions);

    [RelayCommand]
    private async Task InstallFabricPerformanceAsync()
    {
        if (SelectedVersion is null)
        {
            StatusText = "Choose a Minecraft version first.";
            return;
        }

        await RunBusyAsync($"Installing Fabric performance stack for {SelectedVersion.Id}.", async token =>
        {
            var profile = await SaveCurrentProfileCoreAsync(token);
            await InstallPerformanceStackCoreAsync(profile, SelectedVersion.Id, token);
        });
    }

    [RelayCommand]
    private async Task InstallGameFilesAsync()
    {
        if (SelectedVersion is null)
        {
            StatusText = "Choose a Minecraft version first.";
            return;
        }

        await RunBusyAsync($"{GameFilesActionText} game files for {SelectedVersion.Id}.", async token =>
        {
            var profile = await SaveCurrentProfileCoreAsync(token);
            await _minecraftInstallationService.InstallVersionAsync(
                SelectedVersion,
                profile.GameDirectory,
                new Progress<string>(message => StatusText = message),
                token);
            await RefreshInstallStatusAsync();
            StatusText = $"Minecraft {SelectedVersion.Id} is installed for {profile.Name}.";
        });
    }

    [RelayCommand]
    private async Task InstallJavaAsync()
    {
        await RunBusyAsync("Installing Java runtimes.", async token =>
        {
            if (SelectedVersion is not null)
            {
                var requiredVersion = _javaRuntimeService.GetRequiredJavaFeatureVersion(
                    SelectedVersion.Id,
                    GetGameDirectoryForCurrentSelection());
                var javaExecutable = await _javaRuntimeService.EnsureJavaRuntimeAsync(
                    requiredVersion,
                    new Progress<string>(message => StatusText = message),
                    token);

                if (string.IsNullOrWhiteSpace(JavaExecutable))
                {
                    JavaExecutable = javaExecutable;
                    await SaveCurrentProfileCoreAsync(token);
                }

                RefreshJavaStatus();
                StatusText = $"{JavaVersionResolver.GetRuntimeLabel(requiredVersion)} is ready at {ToDisplayPath(javaExecutable)}.";
                return;
            }

            await _javaRuntimeService.EnsureAllSupportedRuntimesAsync(
                new Progress<string>(message => StatusText = message),
                token);

            RefreshJavaStatus();
            StatusText = $"{JavaVersionResolver.GetSupportedVersionsText()} are ready in your DanClient data folder.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (!IsSignedIn)
        {
            StatusText = "Sign in with Microsoft before launching Minecraft.";
            return;
        }

        if (SelectedVersion is null)
        {
            StatusText = "Choose a Minecraft version first.";
            return;
        }

        await LaunchCoreAsync(_minecraftAccount!);
    }

    private async Task LaunchCoreAsync(MinecraftAccount account, CancellationToken cancellationToken = default)
    {
        if (SelectedVersion is null)
        {
            StatusText = "Choose a Minecraft version first.";
            return;
        }

        await RunBusyAsync($"Preparing launch for {SelectedVersion.Id}.", async token =>
        {
            ClearCrashState();
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            var launchToken = linkedTokenSource.Token;
            var profile = await SaveCurrentProfileCoreAsync(launchToken);
            profile = await EnsureJavaForLaunchAsync(profile, launchToken);
            var install = await _minecraftInstallationService.InstallVersionAsync(
                SelectedVersion,
                profile.GameDirectory,
                new Progress<string>(message => StatusText = message),
                launchToken);

            var mainClass = install.MainClass;
            var useFabric = InstallPerformanceMods
                            || _fabricInstallerService.IsLoaderInstalled(profile.GameDirectory);
            if (InstallPerformanceMods)
            {
                await InstallFabricLoaderCoreAsync(profile, SelectedVersion.Id, launchToken);
            }

            if (useFabric)
            {
                mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient";
            }

            var handle = await _javaLauncherService.LaunchAsync(
                new GameLaunchOptions(
                    profile.GameDirectory,
                    profile.JavaExecutable,
                    install.VersionId,
                    install.AssetIndexId,
                    mainClass,
                    account.UserName,
                    account.AccessToken,
                    account.Uuid,
                    profile.MaxMemoryMegabytes,
                    ParseArguments(profile.ExtraJvmArgs)),
                launchToken);

            await _discordPresenceService.SetPlayingAsync(
                AccountName,
                profile.Name,
                SelectedVersion.Id,
                handle.ProcessId,
                launchToken);

            BeginGameRunning(handle);
        });
    }

    [RelayCommand]
    private void DismissCrash()
    {
        CrashDetails = string.Empty;
        PlayScreenState = PlayScreenState.Ready;
        StatusText = "Ready to play.";
    }

    private async Task<LauncherProfile> EnsureJavaForLaunchAsync(LauncherProfile profile, CancellationToken token)
    {
        if (UsesCustomJavaExecutable(profile.JavaExecutable)
            && _javaRuntimeService.IsJavaExecutableAvailable(profile.JavaExecutable))
        {
            return profile;
        }

        var requiredVersion = _javaRuntimeService.GetRequiredJavaFeatureVersion(
            SelectedVersion?.Id,
            profile.GameDirectory);
        StatusText = $"Ensuring Java {requiredVersion} for Minecraft {SelectedVersion?.Id}.";
        var javaExecutable = await _javaRuntimeService.EnsureJavaRuntimeAsync(
            requiredVersion,
            new Progress<string>(message => StatusText = message),
            token);

        if (string.IsNullOrWhiteSpace(JavaExecutable))
        {
            JavaExecutable = javaExecutable;
        }

        RefreshJavaStatus();

        var updated = profile with { JavaExecutable = javaExecutable };
        await _profileService.SaveProfileAsync(updated, select: true, token);
        var index = Profiles.ToList().FindIndex(existing => existing.Id == updated.Id);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (index >= 0)
            {
                Profiles[index] = updated;
            }

            SelectedProfile = updated;
        });

        return updated;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        await RunBusyAsync("Starting Microsoft sign-in.", async token =>
        {
            var account = await _authenticationService.SignInWithDeviceCodeAsync(
                code =>
                {
                    SignInCode = code.UserCode;
                    StatusText = $"Enter code {code.UserCode} in the Microsoft page.";
                    OpenBrowser(code.VerificationUri);
                    return Task.CompletedTask;
                },
                new Progress<AuthProgress>(progress => StatusText = progress.Message),
                token);

            await ApplyAccountAsync(account, save: true, token);
            SignInCode = string.Empty;
            StatusText = $"Welcome, {account.UserName}.";
        });
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await RunBusyAsync("Signing out.", async token =>
        {
            await _accountSessionService.ClearAsync(token);
            _minecraftAccount = null;
            AccountName = "Sign in to play";
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(IsSignInVisible));
            OnPropertyChanged(nameof(LaunchButtonText));
            OnPropertyChanged(nameof(PrimaryActionText));
            LaunchCommand.NotifyCanExecuteChanged();
            PrimaryActionCommand.NotifyCanExecuteChanged();
            await UpdateDiscordIdleAsync();
            StatusText = "Signed out.";
        });
    }

    [RelayCommand]
    private async Task SearchModrinthAsync() => await BrowseModrinthAsync(reset: true);

    [RelayCommand(CanExecute = nameof(CanLoadMoreModrinthPage))]
    private async Task LoadMoreModrinthAsync() => await BrowseModrinthAsync(reset: false);

    private bool CanLoadMoreModrinthPage => CanLoadMoreModrinth && !IsBrowsingModrinth && !IsBusy;

    private Task EnsureModrinthBrowseAsync()
    {
        if (SelectedVersion is null || ModrinthResults.Count > 0 || IsBrowsingModrinth)
        {
            return Task.CompletedTask;
        }

        return BrowseModrinthAsync(reset: true);
    }

    private async Task BrowseModrinthAsync(bool reset)
    {
        if (SelectedVersion is null)
        {
            ModrinthBrowseSummary = "Choose a Minecraft version on the Play tab first.";
            return;
        }

        if (IsBrowsingModrinth)
        {
            return;
        }

        try
        {
            IsBrowsingModrinth = true;
            if (reset)
            {
                _modrinthBrowseOffset = 0;
                _modrinthTotalHits = 0;
                await Dispatcher.UIThread.InvokeAsync(() => ModrinthResults.Clear());
            }

            ModrinthBrowseSummary = reset ? "Loading mods from Modrinth..." : "Loading more mods...";
            var sort = SelectedModrinthSort?.Index ?? "downloads";
            var browse = await _modrinthService.BrowseProjectsAsync(
                ModrinthQuery,
                SelectedVersion.Id,
                "fabric",
                sort,
                _modrinthBrowseOffset,
                ModrinthPageSize,
                _lifetime.Token);

            _modrinthTotalHits = browse.TotalHits;
            _modrinthBrowseOffset = browse.Offset + browse.Projects.Count;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var result in browse.Projects)
                {
                    if (ModrinthResults.Any(existing => existing.Model.ProjectId == result.ProjectId))
                    {
                        continue;
                    }

                    ModrinthResults.Add(new ModrinthProjectViewModel(
                        result,
                        InstallModrinthProjectAsync,
                        LoadModrinthVersionsAsync));
                }
            });

            CanLoadMoreModrinth = _modrinthBrowseOffset < _modrinthTotalHits;
            var queryLabel = string.IsNullOrWhiteSpace(ModrinthQuery) ? "Fabric mods" : $"\"{ModrinthQuery}\"";
            ModrinthBrowseSummary = $"Showing {ModrinthResults.Count} of {_modrinthTotalHits:N0} {queryLabel} for {SelectedVersion.Id}.";
            StatusText = ModrinthBrowseSummary;
        }
        catch (Exception ex)
        {
            ModrinthBrowseSummary = ex.Message;
            StatusText = ex.Message;
        }
        finally
        {
            IsBrowsingModrinth = false;
            LoadMoreModrinthCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedModrinthSortChanged(ModrinthSortOption? value)
    {
        if (ActiveTab == "Mods" && value is not null)
        {
            _ = BrowseModrinthAsync(reset: true);
        }
    }

    partial void OnCanLoadMoreModrinthChanged(bool value) =>
        LoadMoreModrinthCommand.NotifyCanExecuteChanged();

    partial void OnIsBrowsingModrinthChanged(bool value) =>
        LoadMoreModrinthCommand.NotifyCanExecuteChanged();

    private Task<IReadOnlyList<ModrinthProjectVersion>> LoadModrinthVersionsAsync(
        ModrinthProject project,
        CancellationToken cancellationToken)
    {
        if (SelectedVersion is null)
        {
            return Task.FromResult<IReadOnlyList<ModrinthProjectVersion>>([]);
        }

        return _modrinthService.GetProjectVersionsAsync(
            project.Slug,
            SelectedVersion.Id,
            "fabric",
            CreateModrinthVersionFilter(),
            cancellationToken);
    }

    private async Task RefreshModrinthVersionListsAsync()
    {
        if (ModrinthResults.Count == 0)
        {
            return;
        }

        foreach (var project in ModrinthResults.ToArray())
        {
            await project.ReloadVersionsAsync();
        }
    }

    private async Task InstallModrinthProjectAsync(
        ModrinthProject project,
        string? versionId,
        CancellationToken cancellationToken)
    {
        if (SelectedVersion is null)
        {
            StatusText = "Choose a version before downloading a Modrinth mod.";
            return;
        }

        await RunBusyAsync($"Installing {project.Title}.", async token =>
        {
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            var linkedToken = linkedTokenSource.Token;
            var profile = await SaveCurrentProfileCoreAsync(linkedToken);
            await _fabricInstallerService.InstallLoaderLibrariesAsync(
                SelectedVersion.Id,
                profile.GameDirectory,
                new Progress<string>(message => StatusText = message),
                linkedToken);

            var result = await _modrinthService.InstallProjectAsync(
                project,
                SelectedVersion.Id,
                "fabric",
                profile.ModsDirectory,
                versionId,
                CreateModrinthVersionFilter(),
                new Progress<string>(message => StatusText = message),
                linkedToken);

            await RefreshModsAsync();
            StatusText = $"Installed {result.FileName}.";
        });
    }

    partial void OnSelectedProfileChanged(LauncherProfile? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyProfile(value);
        _ = _profileService.SelectProfileAsync(value.Id, _lifetime.Token);
        _ = RefreshModsAsync();
        _ = RefreshInstallStatusAsync();
        _ = UpdateDiscordIdleAsync();
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(CanDeleteProfile));
    }

    partial void OnAccountNameChanged(string value) =>
        OnPropertyChanged(nameof(AccountInitial));

    partial void OnSelectedVersionChanged(MinecraftVersion? value)
    {
        OnPropertyChanged(nameof(SelectedVersionName));
        OnPropertyChanged(nameof(SelectedJavaText));
        OnPropertyChanged(nameof(InstalledModsTitle));
        OnPropertyChanged(nameof(ModrinthBrowseHeader));
        OnPropertyChanged(nameof(IsGameFilesActionVisible));
        OnPropertyChanged(nameof(RequiredJavaFeatureVersion));
        OnPropertyChanged(nameof(RequiredJavaText));
        OnPropertyChanged(nameof(InstalledJavaText));
        UpdateModsDirectoryForCurrentSelection();
        _ = RefreshModsAsync();
        _ = RefreshInstallStatusAsync();
        if (ActiveTab == "Mods")
        {
            _ = BrowseModrinthAsync(reset: true);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        LoadMoreModrinthCommand.NotifyCanExecuteChanged();
        if (!_isGameRunning && PlayScreenState != PlayScreenState.Crashed)
        {
            PlayScreenState = value ? PlayScreenState.Installing : PlayScreenState.Ready;
        }
    }

    partial void OnPlayScreenStateChanged(PlayScreenState value)
    {
        OnPropertyChanged(nameof(PrimaryActionText));
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIncludeSnapshotsChanged(bool value)
    {
        if (!_isApplyingProfile)
        {
            _ = RefreshVersionsAsync();
        }
    }

    partial void OnDiscordPresenceEnabledChanged(bool value)
    {
        _discordPresenceService.IsEnabled = value;
        _ = RefreshDiscordStatusAsync();
        _ = value ? UpdateDiscordIdleAsync() : _discordPresenceService.ClearAsync(_lifetime.Token);
    }

    private void ApplyDiscordSettings() =>
        _discordPresenceService.SetApplicationId(DiscordSettingsStore.ResolveApplicationId());

    private async Task RefreshDiscordStatusAsync()
    {
        if (!DiscordPresenceEnabled)
        {
            DiscordStatusText = "Rich Presence is disabled in settings.";
            return;
        }

        try
        {
            await _discordPresenceService.InitializeAsync(_lifetime.Token);
            DiscordStatusText = "Connected to Discord.";
        }
        catch (Exception ex)
        {
            DiscordStatusText = ex.Message;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _modFileService.ModsChanged -= OnModsChanged;
        _modFileService.Dispose();
        _discordPresenceService.Dispose();
    }

    private async Task LoadAccountAsync()
    {
        var account = await _accountSessionService.LoadAsync(_lifetime.Token);
        if (account is not null)
        {
            await ApplyAccountAsync(account, save: false, _lifetime.Token);
            StatusText = $"Welcome back, {account.UserName}.";
        }
    }

    private async Task ApplyAccountAsync(MinecraftAccount account, bool save, CancellationToken token)
    {
        _minecraftAccount = account;
        AccountName = account.UserName;
        if (save)
        {
            await _accountSessionService.SaveAsync(account, token);
        }

        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignInVisible));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(PrimaryActionText));
        LaunchCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
        await UpdateDiscordIdleAsync();
    }

    private async Task LoadProfilesAsync()
    {
        var profiles = await _profileService.LoadProfilesAsync(_lifetime.Token);
        var selected = await _profileService.GetSelectedProfileAsync(_lifetime.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selected.Id)
                              ?? Profiles.FirstOrDefault();
            OnPropertyChanged(nameof(CanDeleteProfile));
        });
    }

    private void ApplyProfile(LauncherProfile profile)
    {
        _isApplyingProfile = true;
        try
        {
            ProfileName = profile.Name;
            IncludeSnapshots = profile.IncludeSnapshots;
            InstallPerformanceMods = profile.InstallPerformanceMods;
            MaxMemoryMegabytes = profile.MaxMemoryMegabytes;
            JavaExecutable = profile.JavaExecutable;
            ExtraJvmArgsText = profile.ExtraJvmArgs;
            UpdateModsDirectoryForCurrentSelection(profile);

            if (Versions.Count > 0)
            {
                SelectedVersion = Versions.FirstOrDefault(version => version.Id == profile.MinecraftVersionId)
                                  ?? Versions.FirstOrDefault();
            }
        }
        finally
        {
            _isApplyingProfile = false;
        }
    }

    private async Task<LauncherProfile> SaveCurrentProfileCoreAsync(CancellationToken token)
    {
        var current = SelectedProfile ?? new LauncherProfile(
            "default",
            "Survival",
            SelectedVersion?.Id,
            IncludeSnapshots,
            InstallPerformanceMods,
            MaxMemoryMegabytes,
            string.Empty,
            string.Empty,
            AppPaths.DefaultInstance);

        var updated = current with
        {
            Name = string.IsNullOrWhiteSpace(ProfileName) ? current.Name : ProfileName.Trim(),
            MinecraftVersionId = SelectedVersion?.Id,
            IncludeSnapshots = IncludeSnapshots,
            InstallPerformanceMods = InstallPerformanceMods,
            MaxMemoryMegabytes = Math.Clamp(MaxMemoryMegabytes, 1024, 32768),
            JavaExecutable = string.IsNullOrWhiteSpace(JavaExecutable) ? current.JavaExecutable : JavaExecutable.Trim(),
            ExtraJvmArgs = ExtraJvmArgsText.Trim(),
            GameDirectory = GetGameDirectoryForSelection(current)
        };

        await _profileService.SaveProfileAsync(updated, select: true, token);
        var index = Profiles.ToList().FindIndex(profile => profile.Id == updated.Id);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (index >= 0)
            {
                Profiles[index] = updated;
            }
            else
            {
                Profiles.Add(updated);
            }

            SelectedProfile = updated;
            OnPropertyChanged(nameof(SelectedProfileName));
        });
        return updated;
    }

    private void UpdateModsDirectoryForCurrentSelection(LauncherProfile? profile = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            return;
        }

        var gameDirectory = GetGameDirectoryForSelection(profile);
        _modFileService.SetModsDirectory(Path.Combine(gameDirectory, "mods"));
        OnPropertyChanged(nameof(ModsDirectory));
    }

    private async Task RefreshInstallStatusAsync()
    {
        if (SelectedVersion is null || SelectedProfile is null)
        {
            IsGameFilesInstalled = false;
            GameFilesMessage = "Choose a profile and version to check files.";
            GameFilesActionText = "Install";
            OnPropertyChanged(nameof(IsGameFilesActionVisible));
            return;
        }

        try
        {
            var status = await _minecraftInstallationService.GetInstallStatusAsync(
                SelectedVersion,
                GetGameDirectoryForSelection(SelectedProfile),
                _lifetime.Token);
            IsGameFilesInstalled = status.IsInstalled;
            GameFilesMessage = status.Message;
            GameFilesActionText = status.State == MinecraftInstallState.Missing ? "Install" : "Repair";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            IsGameFilesInstalled = false;
            GameFilesMessage = "Minecraft files need repair.";
            GameFilesActionText = "Repair";
        }

        OnPropertyChanged(nameof(IsGameFilesActionVisible));
        OnPropertyChanged(nameof(PrimaryActionText));
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private void OnModsChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () => await RefreshModsAsync());
    }

    private async Task RunBusyAsync(string initialStatus, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = initialStatus;
            await action(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation canceled.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallFabricLoaderCoreAsync(
        LauncherProfile profile,
        string minecraftVersion,
        CancellationToken token)
    {
        var loader = await _fabricInstallerService.ResolveLoaderAsync(minecraftVersion, token);
        StatusText = $"Resolved Fabric Loader {loader.LoaderVersion}.";
        await _fabricInstallerService.InstallLoaderLibrariesAsync(
            minecraftVersion,
            profile.GameDirectory,
            new Progress<string>(message => StatusText = message),
            token);

        StatusText = $"Fabric loader is ready for {minecraftVersion}.";
    }

    private async Task InstallPerformanceStackCoreAsync(
        LauncherProfile profile,
        string minecraftVersion,
        CancellationToken token)
    {
        var loader = await _fabricInstallerService.ResolveLoaderAsync(minecraftVersion, token);
        StatusText = $"Resolved Fabric Loader {loader.LoaderVersion}.";
        await _fabricInstallerService.InstallLoaderLibrariesAsync(
            minecraftVersion,
            profile.GameDirectory,
            new Progress<string>(message => StatusText = message),
            token);

        await _fabricInstallerService.InstallPerformanceModsAsync(
            minecraftVersion,
            profile.ModsDirectory,
            new Progress<string>(message => StatusText = message),
            token);

        await RefreshModsAsync();
        StatusText = $"Fabric performance stack is ready for {minecraftVersion}.";
    }

    private async Task UpdateDiscordIdleAsync()
    {
        await _discordPresenceService.SetLauncherIdleAsync(
            AccountName,
            SelectedProfileName,
            _lifetime.Token);
    }

    private bool CanLaunch() => IsSignedIn && SelectedVersion is not null && !IsBusy && !_isGameRunning;

    private void RefreshJavaStatus()
    {
        OnPropertyChanged(nameof(RequiredJavaFeatureVersion));
        OnPropertyChanged(nameof(SelectedJavaText));
        OnPropertyChanged(nameof(RequiredJavaText));
        OnPropertyChanged(nameof(InstalledJavaText));
        OnPropertyChanged(nameof(AccountInitial));
    }

    private void ClearCrashState()
    {
        CrashDetails = string.Empty;
        if (PlayScreenState == PlayScreenState.Crashed)
        {
            PlayScreenState = PlayScreenState.Ready;
        }
    }

    private void BeginGameRunning(GameProcessHandle handle)
    {
        ClearCrashState();
        _isGameRunning = true;
        PlayScreenState = PlayScreenState.Running;
        StatusText = "Minecraft is running.";
        OnPropertyChanged(nameof(PrimaryActionText));
        PrimaryActionCommand.NotifyCanExecuteChanged();
        _ = MonitorGameProcessAsync(handle);
    }

    private async Task MonitorGameProcessAsync(GameProcessHandle handle)
    {
        var exitCode = 0;
        var canceled = false;
        try
        {
            using var process = Process.GetProcessById(handle.ProcessId);
            await process.WaitForExitAsync(_lifetime.Token);
            exitCode = process.ExitCode;
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            canceled = true;
        }

        if (canceled || !_isGameRunning)
        {
            return;
        }

        _isGameRunning = false;
        OnPropertyChanged(nameof(PrimaryActionText));
        PrimaryActionCommand.NotifyCanExecuteChanged();

        try
        {
            await Task.Delay(750, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        var diagnostics = await GameCrashDiagnostics.AnalyzeAsync(
            handle.MinecraftDirectory,
            handle.StartedAt,
            exitCode,
            handle.LauncherLogPath,
            _lifetime.Token);

        if (diagnostics.Crashed)
        {
            CrashDetails = diagnostics.DisplayText;
            PlayScreenState = PlayScreenState.Crashed;
            StatusText = diagnostics.Summary;
        }
        else
        {
            CrashDetails = string.Empty;
            PlayScreenState = IsBusy ? PlayScreenState.Installing : PlayScreenState.Ready;
            StatusText = "Ready to play.";
        }

        await UpdateDiscordIdleAsync();
    }

    private static bool UsesCustomJavaExecutable(string? javaExecutable)
    {
        if (string.IsNullOrWhiteSpace(javaExecutable))
        {
            return false;
        }

        var trimmed = javaExecutable.Trim();
        return !trimmed.Equals("java.exe", StringComparison.OrdinalIgnoreCase)
               && !trimmed.Equals("java", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAccountInitial(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName) || accountName.Equals("Sign in to play", StringComparison.OrdinalIgnoreCase))
        {
            return "?";
        }

        return char.ToUpperInvariant(accountName.Trim()[0]).ToString();
    }

    private string? GetGameDirectoryForCurrentSelection() =>
        SelectedProfile is null ? null : GetGameDirectoryForSelection(SelectedProfile);

    private string GetGameDirectoryForSelection(LauncherProfile profile) =>
        AppPaths.GetProfileInstance(profile.Id, SelectedVersion?.Id ?? profile.MinecraftVersionId);

    private static string ToDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (Path.GetDirectoryName(path) is null)
        {
            return path;
        }

        var normalizedRoot = Path.GetFullPath(AppPaths.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);
        if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(AppPaths.Root, normalizedPath);
            return Path.Combine(".danclient", relative);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var normalizedUserProfile = Path.GetFullPath(userProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedPath.StartsWith(normalizedUserProfile, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine("~", Path.GetRelativePath(userProfile, normalizedPath));
            }
        }

        return path;
    }

    private static string RedactDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var redacted = text;
        redacted = ReplacePathPrefix(redacted, AppPaths.Root, ".danclient");

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            redacted = ReplacePathPrefix(redacted, userProfile, "~");
        }

        return Regex.Replace(
            redacted,
            @"(?i)[A-Z]:\\Users\\[^\\\s]+",
            "~");
    }

    private static string ReplacePathPrefix(string text, string prefix, string replacement)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return text;
        }

        var normalizedPrefix = Path.GetFullPath(prefix).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return text.Replace(normalizedPrefix, replacement, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        var result = new List<string>();
        var current = new List<char>();
        var inQuotes = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                AddCurrent();
                continue;
            }

            current.Add(c);
        }

        AddCurrent();
        return result;

        void AddCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            result.Add(new string(current.ToArray()));
            current.Clear();
        }
    }

    private static void OpenBrowser(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch
        {
            // The device code remains visible in the UI if the OS cannot open a browser.
        }
    }
}
