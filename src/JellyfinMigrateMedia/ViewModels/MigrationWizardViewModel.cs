using System.Collections.ObjectModel;
using JellyfinMigrateMedia.Infrastructure.Configuration;
// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
//#pragma warning disable CA1822
#pragma warning disable IDE0290

namespace JellyfinMigrateMedia.ViewModels;

public sealed class MigrationWizardViewModel : ViewModelBase
{
    private int _stepIndex;
    private string _name;
    private string? _targetPath;
    private string _movieFolderTemplate;
    private string _movieFileTemplate;
    private string _seriesFolderTemplate;
    private string _episodeFileTemplate;
    private bool _sanitizeFileAndFolderNames;

    public MigrationWizardViewModel(MigrationProfile? existing = null)
    {
        var p = existing ?? new MigrationProfile();

        Id = p.Id;
        _name = p.Name;
        _targetPath = p.Destination.TargetPath;

        _movieFolderTemplate = p.Naming.MovieFolderTemplate;
        _movieFileTemplate = p.Naming.MovieFileTemplate;
        _seriesFolderTemplate = p.Naming.SeriesFolderTemplate;
        _episodeFileTemplate = p.Naming.EpisodeFileTemplate;
        _sanitizeFileAndFolderNames = p.Naming.SanitizeFileAndFolderNames;

        Sources = new ObservableCollection<SourceRowViewModel>(
            (p.Sources ?? [])
            .Select(s => new SourceRowViewModel(s)));

        if (Sources.Count == 0)
            Sources.Add(new SourceRowViewModel(new SourceMediaDefinition()));

        Sources.CollectionChanged += (_, _) => RehookSources();
        RehookSources();

        AddSourceCommand = new RelayCommand(() => Sources.Add(new SourceRowViewModel(new SourceMediaDefinition())));
        RemoveSelectedSourceCommand = new RelayCommand(
            () =>
            {
                if (SelectedSource is null) return;
                Sources.Remove(SelectedSource);
                if (Sources.Count == 0)
                    Sources.Add(new SourceRowViewModel(new SourceMediaDefinition()));
            },
            () => SelectedSource is not null);

        NextCommand = new RelayCommand(
            () => StepIndex++,
            () => StepIndex < 2 && CanGoNext());
        BackCommand = new RelayCommand(() => StepIndex--, () => StepIndex > 0);
    }

    public string Id { get; }

    public int StepIndex
    {
        get => _stepIndex;
        set
        {
            if (!SetProperty(ref _stepIndex, value)) return;
            BackCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(StepNumber));
            OnPropertyChanged(nameof(IsStep0));
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(IsNotLastStep));
        }
    }

    public int StepNumber => StepIndex + 1;
    public static int TotalSteps => 3;

    public bool IsStep0 => StepIndex == 0;
    public bool IsStep1 => StepIndex == 1;
    public bool IsStep2 => StepIndex == 2;
    public bool IsLastStep => StepIndex == 2;
    public bool IsNotLastStep => !IsLastStep;

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value)) return;
            NextCommand.RaiseCanExecuteChanged();
        }
    }

    // Step 0 (Cíl + název)
    public string? TargetPath
    {
        get => _targetPath;
        set
        {
            if (!SetProperty(ref _targetPath, value)) return;
            NextCommand.RaiseCanExecuteChanged();
        }
    }

    // Step 1 (Zdroj)
    public ObservableCollection<SourceRowViewModel> Sources { get; }

    private SourceRowViewModel? _selectedSource;
    public SourceRowViewModel? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (!SetProperty(ref _selectedSource, value)) return;
            RemoveSelectedSourceCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand AddSourceCommand { get; }
    public RelayCommand RemoveSelectedSourceCommand { get; }

    // Step 2 (Maskování / struktura)
    public string MovieFolderTemplate
    {
        get => _movieFolderTemplate;
        set => SetProperty(ref _movieFolderTemplate, value);
    }

    public string MovieFileTemplate
    {
        get => _movieFileTemplate;
        set => SetProperty(ref _movieFileTemplate, value);
    }

    public string SeriesFolderTemplate
    {
        get => _seriesFolderTemplate;
        set => SetProperty(ref _seriesFolderTemplate, value);
    }

    public string EpisodeFileTemplate
    {
        get => _episodeFileTemplate;
        set => SetProperty(ref _episodeFileTemplate, value);
    }

    public bool SanitizeFileAndFolderNames
    {
        get => _sanitizeFileAndFolderNames;
        set => SetProperty(ref _sanitizeFileAndFolderNames, value);
    }

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }

    // Mask visibility helpers (until UI is fully wired to DB library selection).
    public bool IsMovieContent => Sources.FirstOrDefault()?.ContentType?.Equals("Movie", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsSeriesContent => !IsMovieContent; // default: show series mask for non-movie

    private bool CanGoNext()
    {
        // Step 0: name + destination required
        if (StepIndex == 0)
            return !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(TargetPath);

        // Step 1: at least one source path
        if (StepIndex == 1)
            return Sources.Any(s => !string.IsNullOrWhiteSpace(s.SourcePath));

        return true;
    }

    private void RehookSources()
    {
        foreach (var s in Sources)
            s.PropertyChanged -= SourceRow_PropertyChanged;
        foreach (var s in Sources)
            s.PropertyChanged += SourceRow_PropertyChanged;

        NextCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsMovieContent));
        OnPropertyChanged(nameof(IsSeriesContent));
    }

    private void SourceRow_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (StepIndex == 1 && e.PropertyName == nameof(SourceRowViewModel.SourcePath))
            NextCommand.RaiseCanExecuteChanged();

        if (e.PropertyName == nameof(SourceRowViewModel.ContentType))
        {
            OnPropertyChanged(nameof(IsMovieContent));
            OnPropertyChanged(nameof(IsSeriesContent));
        }
    }

    public MigrationProfile BuildProfile()
    {
        var profile = new MigrationProfile
        {
            Id = Id,
            Name = Name.Trim(),
            Destination = new DestinationSettings
            {
                TargetPath = string.IsNullOrWhiteSpace(TargetPath) ? null : TargetPath.Trim()
            },
            Naming = new NamingAndOrganizationSettings
            {
                MovieFolderTemplate = MovieFolderTemplate,
                MovieFileTemplate = MovieFileTemplate,
                SeriesFolderTemplate = SeriesFolderTemplate,
                EpisodeFileTemplate = EpisodeFileTemplate,
                SanitizeFileAndFolderNames = SanitizeFileAndFolderNames
            },
            Sources =
            [
                .. Sources
                    .Select(s => s.ToModel())
                    .Where(s => !string.IsNullOrWhiteSpace(s.SourcePath))
            ],
        };

        return profile;
    }
}

public sealed class SourceRowViewModel : ViewModelBase
{
    private string? _libraryName;
    private string? _contentType;
    private string? _sourcePath;

    public SourceRowViewModel(SourceMediaDefinition source)
    {
        _libraryName = source.LibraryName;
        _contentType = source.ContentType;
        _sourcePath = source.SourcePath;
    }

    public string? LibraryName
    {
        get => _libraryName;
        set => SetProperty(ref _libraryName, value);
    }

    public string? ContentType
    {
        get => _contentType;
        set => SetProperty(ref _contentType, value);
    }

    public string? SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }

    public SourceMediaDefinition ToModel() => new()
    {
        LibraryName = string.IsNullOrWhiteSpace(LibraryName) ? null : LibraryName.Trim(),
        ContentType = string.IsNullOrWhiteSpace(ContentType) ? null : ContentType.Trim(),
        SourcePath = string.IsNullOrWhiteSpace(SourcePath) ? null : SourcePath.Trim(),
        DiskLabel = null
    };
}

