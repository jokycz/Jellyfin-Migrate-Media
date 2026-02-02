using System.Windows;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.ViewModels;
using WinForms = System.Windows.Forms;

namespace JellyfinMigrateMedia;

public partial class MigrationWizardWindow : Window
{
    private readonly MigrationWizardViewModel _vm;

    public MigrationWizardWindow(MigrationWizardViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = _vm;
        InitializeComponent();
    }

    public MigrationProfile? ResultProfile { get; private set; }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Vyber cílovou složku (kam migrace zapisuje)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var result = dlg.ShowDialog();
        if (result == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            _vm.TargetPath = dlg.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Enforce: one profile => one Jellyfin library (but can have multiple folders/paths).
        var distinctLibs = _vm.Sources
            .Select(s => $"{(s.LibraryName ?? "").Trim()}|{(s.ContentType ?? "").Trim()}")
            .Where(x => x != "|")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctLibs.Length > 1)
        {
            System.Windows.MessageBox.Show(
                this,
                "Jeden profil může obsahovat pouze jednu Jellyfin knihovnu.\n" +
                "Sjednoť prosím 'Knihovna' a 'Typ' ve všech řádcích (nebo nech jen jednu knihovnu).",
                "Validace profilu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ResultProfile = _vm.BuildProfile();
        DialogResult = true;
        Close();
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSource is null)
        {
            System.Windows.MessageBox.Show(this, "Nejdřív vyber řádek zdroje.", "Zdroj", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Vyber zdrojovou složku (kde jsou media soubory)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var result = dlg.ShowDialog();
        if (result == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            _vm.SelectedSource.SourcePath = dlg.SelectedPath;
    }
}

