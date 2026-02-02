using System.Collections.ObjectModel;
using System.IO;
using JellyfinMigrateMedia.Infrastructure.Configuration;
// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
#pragma warning disable IDE0290

namespace JellyfinMigrateMedia.ViewModels;

public sealed class MigrationManagerViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private JellyfinMigrateSettings _settings = new();
    private MigrationProfileListItemViewModel? _selectedProfile;
    private bool _isScanning;
    private string _scanSummary = "Vyber profil pro zobrazení stavu.";
    private int _foundItemsCount;
    private CancellationTokenSource? _scanCts;

    public MigrationManagerViewModel(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        NewProfileCommand = new RelayCommand(() => NewRequested?.Invoke(this, EventArgs.Empty));
        EditProfileCommand = new RelayCommand(
            () => EditRequested?.Invoke(this, EventArgs.Empty),
            () => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(
            () => _ = DeleteSelectedSafeAsync(),
            () => SelectedProfile is not null);

        RefreshStatusCommand = new RelayCommand(
            () => _ = RefreshSelectedProfileStatusAsync(),
            () => SelectedProfile is not null && !IsScanning);

        RunProfileCommand = new RelayCommand(
            () => RunRequested?.Invoke(this, EventArgs.Empty),
            () => SelectedProfile is not null && !IsScanning);
    }

    public event EventHandler? NewRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? RunRequested;

    public string SettingsPath => _settingsStore.SettingsPath;

    public ObservableCollection<MigrationProfileListItemViewModel> Profiles { get; } = [];

    public MigrationProfileListItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value)) return;
            EditProfileCommand.RaiseCanExecuteChanged();
            DeleteProfileCommand.RaiseCanExecuteChanged();
            RefreshStatusCommand.RaiseCanExecuteChanged();
            RunProfileCommand.RaiseCanExecuteChanged();

            _settings.LastProfileId = value?.Id;
            _ = SaveAsync();
            _ = RefreshSelectedProfileStatusAsync();
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            RefreshStatusCommand.RaiseCanExecuteChanged();
            RunProfileCommand.RaiseCanExecuteChanged();
        }
    }

    public int FoundItemsCount
    {
        get => _foundItemsCount;
        private set => SetProperty(ref _foundItemsCount, value);
    }

    public string ScanSummary
    {
        get => _scanSummary;
        set => SetProperty(ref _scanSummary, value);
    }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public RelayCommand NewProfileCommand { get; }
    public RelayCommand EditProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand RefreshStatusCommand { get; }
    public RelayCommand RunProfileCommand { get; }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        Profiles.Clear();

        foreach (var p in _settings.MigrationProfiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(new MigrationProfileListItemViewModel(p));

        if (!string.IsNullOrWhiteSpace(_settings.LastProfileId))
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.LastProfileId);

        SelectedProfile ??= Profiles.FirstOrDefault();

        ScanSummary = Profiles.Count == 0
            ? "Nemáš žádné profily. Klikni na 'Vytvořit migraci'."
            : "Vyber profil pro zobrazení stavu.";
    }

    private async Task SaveAsync()
    {
        await _settingsStore.SaveAsync(_settings).ConfigureAwait(false);
    }

    public void AddOrUpdateProfile(MigrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var idx = _settings.MigrationProfiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
            _settings.MigrationProfiles[idx] = profile;
        else
            _settings.MigrationProfiles.Add(profile);

        var existing = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (existing is not null)
            existing.Update(profile);
        else
            Profiles.Add(new MigrationProfileListItemViewModel(profile));

        SelectedProfile = Profiles.First(p => p.Id == profile.Id);
        _ = SaveAsync();
    }

    public MigrationProfile? GetSelectedModel() => SelectedProfile?.Model;

    private async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is null) return;

        var id = SelectedProfile.Id;
        var toRemove = SelectedProfile;

        Profiles.Remove(toRemove);
        _settings.MigrationProfiles.RemoveAll(p => p.Id == id);

        SelectedProfile = Profiles.FirstOrDefault();
        await SaveAsync();

        ScanSummary = Profiles.Count == 0
            ? "Nemáš žádné profily. Klikni na 'Vytvořit migraci'."
            : ScanSummary;
    }

    private async Task DeleteSelectedSafeAsync()
    {
        try
        {
            await DeleteSelectedAsync();
        }
        catch (Exception ex)
        {
            ScanSummary = $"Chyba při mazání profilu: {ex.Message}";
        }
    }

    private async Task RefreshSelectedProfileStatusAsync()
    {
        if (_scanCts is not null)
            await _scanCts.CancelAsync();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        if (SelectedProfile?.Model is null)
        {
            FoundItemsCount = 0;
            ScanSummary = "Vyber profil pro zobrazení stavu.";
            return;
        }

        IsScanning = true;
        try
        {
            ScanSummary = "Skenuji zdrojové složky...";
            var result = await Task.Run(() => ScanProfile(SelectedProfile.Model, ct), ct);
            FoundItemsCount = result.TotalCount;
            ScanSummary = result.Summary;
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            FoundItemsCount = 0;
            ScanSummary = $"Chyba při skenu: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static ScanResult ScanProfile(MigrationProfile profile, CancellationToken ct)
    {
        var lines = new List<string>();
        var total = 0;

        foreach (var src in profile.Sources ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var path = src.SourcePath;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (!Directory.Exists(path))
            {
                var lib = string.IsNullOrWhiteSpace(src.LibraryName) ? "Library" : src.LibraryName;
                var ct1 = string.IsNullOrWhiteSpace(src.ContentType) ? "unknown" : src.ContentType;
                lines.Add($"{lib} ({ct1}): {path} (nenalezeno)");
                continue;
            }

            var exts = GetExtensionsForContentType(src.ContentType);
            var count = 0;

            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            foreach (var file in Directory.EnumerateFiles(path, "*.*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (exts is not null)
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrWhiteSpace(ext) || !exts.Contains(ext))
                        continue;
                }

                count++;
            }

            total += count;
            var lib2 = string.IsNullOrWhiteSpace(src.LibraryName) ? "Library" : src.LibraryName;
            var ct2 = string.IsNullOrWhiteSpace(src.ContentType) ? "unknown" : src.ContentType;
            lines.Add($"{lib2} ({ct2}): {count} položek");
        }

        if (lines.Count == 0)
            return new ScanResult(0, "Profil nemá žádné zdroje.");

        var summary = $"Nalezeno celkem: {total} položek\n" + string.Join("\n", lines);
        return new ScanResult(total, summary);
    }

    private static HashSet<string>? GetExtensionsForContentType(string? contentType)
    {
        // If you want "count everything", return null.
        var ct = (contentType ?? "").Trim().ToLowerInvariant();
        return ct switch
        {
            "movie" or "episode" or "video" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mkv",".mp4",".avi",".mov",".m4v",".ts",".webm"
            },
            "audio" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3",".flac",".aac",".m4a",".ogg",".wav"
            },
            "photo" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",".jpeg",".png",".gif",".webp",".tiff",".bmp",".heic"
            },
            _ => null
        };
    }

    private readonly record struct ScanResult(int TotalCount, string Summary);
}

public sealed class MigrationProfileListItemViewModel : ViewModelBase
{
    private string _name;

    public MigrationProfileListItemViewModel(MigrationProfile model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _name = model.Name;
    }

    public string Id => Model.Id;

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public MigrationProfile Model { get; private set; }

    public void Update(MigrationProfile model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Name = Model.Name;
    }
}

