using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FMR.AisinAMR.ViewModels;

namespace FMR.AisinAMR.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void BrowseMapPath_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var dialog = new OpenFileDialog
            {
                Filter = "PGM files (*.pgm)|*.pgm|All files (*.*)|*.*",
                Title = "Select Map File"
            };

            if (dialog.ShowDialog() == true)
            {
                vm.MapFilePath = dialog.FileName;
            }
        }
    }
}
