using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using F1Team.App.ViewModels;

namespace F1Team.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private void AssetGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel ||
                sender is not DataGrid dataGrid ||
                dataGrid.SelectedItem is not AssetPickerItemViewModel item ||
                !viewModel.SelectAssetCommand.CanExecute(item))
            {
                return;
            }

            viewModel.SelectAssetCommand.Execute(item);
        }
    }
}
