using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
namespace AnimeJaNaiConfEditor.Views;
public partial class InterpolationStages : UserControl
{
    public InterpolationStages() { InitializeComponent(); }
    // Button raises Click before executing its bound command. Closing the flyout
    // here detaches its content and clears that binding before it can run.
    private void OnAddStep(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => AddProcessingStep.Flyout?.Hide());
}
