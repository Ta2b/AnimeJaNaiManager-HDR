using Avalonia.Controls;
using Avalonia.Interactivity;
namespace AnimeJaNaiConfEditor.Views;
public partial class InterpolationStages : UserControl
{
    public InterpolationStages() { InitializeComponent(); }
    private void OnAddStep(object? sender, RoutedEventArgs e) => AddProcessingStep.Flyout?.Hide();
}
