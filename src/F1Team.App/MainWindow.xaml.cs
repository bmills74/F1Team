using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.ComponentModel;
using F1Team.App.ViewModels;

namespace F1Team.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainWindowViewModel();
            DataContext = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            BuildHistoryColumns(viewModel);
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is MainWindowViewModel viewModel && e.PropertyName == nameof(viewModel.HistoryRoundHeaders))
            {
                BuildHistoryColumns(viewModel);
            }
        }

        private void BuildHistoryColumns(MainWindowViewModel viewModel)
        {
            ConfigureHistoryGrid(DriverHistoryGrid, viewModel.HistoryRoundHeaders, "Driver");
            ConfigureHistoryGrid(ConstructorHistoryGrid, viewModel.HistoryRoundHeaders, "Constructor");
        }

        private static void ConfigureHistoryGrid(DataGrid grid, IReadOnlyList<string> headers, string assetType)
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = assetType,
                Binding = new Binding(nameof(PredictionHistoryRow.AssetId)),
                Width = 85
            });

            var totalPanel = new FrameworkElementFactory(typeof(StackPanel));
            totalPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            totalPanel.SetValue(StackPanel.MarginProperty, new Thickness(4, 2, 4, 2));
            var totalActual = new FrameworkElementFactory(typeof(TextBlock));
            totalActual.SetBinding(TextBlock.TextProperty, new Binding(nameof(PredictionHistoryRow.TotalActualText)));
            totalPanel.AppendChild(totalActual);
            var totalSeparator = new FrameworkElementFactory(typeof(TextBlock));
            totalSeparator.SetValue(TextBlock.TextProperty, " / ");
            totalPanel.AppendChild(totalSeparator);
            var totalPredicted = new FrameworkElementFactory(typeof(TextBlock));
            totalPredicted.SetBinding(TextBlock.TextProperty, new Binding(nameof(PredictionHistoryRow.TotalPredictedText)));
            totalPredicted.SetValue(TextBlock.ForegroundProperty, Brushes.DarkBlue);
            totalPanel.AppendChild(totalPredicted);
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Total",
                CellTemplate = new DataTemplate { VisualTree = totalPanel },
                Width = 90
            });

            for (var index = 0; index < headers.Count; index++)
            {
                var panel = new FrameworkElementFactory(typeof(StackPanel));
                panel.SetValue(StackPanel.MarginProperty, new Thickness(4, 2, 4, 2));

                var actual = new FrameworkElementFactory(typeof(TextBlock));
                actual.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}].ActualText"));
                panel.AppendChild(actual);

                var prediction = new FrameworkElementFactory(typeof(TextBlock));
                prediction.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}].PredictionText"));
                prediction.SetValue(TextBlock.ForegroundProperty, Brushes.DarkBlue);
                panel.AppendChild(prediction);

                var difference = new FrameworkElementFactory(typeof(TextBlock));
                difference.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}].DifferenceText"));
                difference.SetBinding(TextBlock.ForegroundProperty, new Binding($"Cells[{index}].DifferenceBrush"));
                panel.AppendChild(difference);

                grid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = headers[index],
                    CellTemplate = new DataTemplate { VisualTree = panel },
                    Width = 105
                });
            }
        }

        private void AssetRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel ||
                sender is not DataGridRow row ||
                row.DataContext is not AssetPickerItemViewModel item ||
                !viewModel.SelectAssetCommand.CanExecute(item))
            {
                return;
            }

            viewModel.SelectAssetCommand.Execute(item);
        }
    }
}
