using System.Windows;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.ViewModels;

namespace JellyfinMigrateMedia
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly JsonSettingsStore _settingsStore = new();
        private readonly MigrationManagerViewModel _vm;

        public MainWindow()
        {
            _vm = new MigrationManagerViewModel(_settingsStore);
            _vm.NewRequested += Vm_NewRequested;
            _vm.EditRequested += Vm_EditRequested;
            _vm.RunRequested += Vm_RunRequested;

            InitializeComponent();
            DataContext = _vm;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.InitializeAsync();
            }
            catch (Exception ex)
            {
               System.Windows.MessageBox.Show(this, $"Nelze načíst konfiguraci: {ex.Message}", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Vm_NewRequested(object? sender, EventArgs e)
        {
            var wizardVm = new MigrationWizardViewModel();
            var win = new MigrationWizardWindow(wizardVm) { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile is not null)
                _vm.AddOrUpdateProfile(win.ResultProfile);
        }

        private void Vm_EditRequested(object? sender, EventArgs e)
        {
            var selected = _vm.GetSelectedModel();
            if (selected is null) return;

            var wizardVm = new MigrationWizardViewModel(selected);
            var win = new MigrationWizardWindow(wizardVm) { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile is not null)
                _vm.AddOrUpdateProfile(win.ResultProfile);
        }

        private void Vm_RunRequested(object? sender, EventArgs e)
        {
            var selected = _vm.GetSelectedModel();
            if (selected is null) return;

            System.Windows.MessageBox.Show(
                this,
                "Spuštění reálné migrace ještě není napojené na MigrationService.\n" +
                "Profil je ale uložený a stav (nalezené položky) funguje.",
                "Spustit migraci",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}