using AnimeJaNaiConfEditor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
namespace AnimeJaNaiConfEditor.Views;
public partial class OptionalEngines : UserControl
{
    public OptionalEngines()=>InitializeComponent();
    private async void ImportFrucSdkClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm||!vm.OptionalSdks.CanImport)return;
        var top=TopLevel.GetTopLevel(this);if(top==null)return;
        var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Import NVIDIA Optical Flow SDK",AllowMultiple=false,FileTypeFilter=[new("Optical Flow SDK ZIP"){Patterns=["*.zip"]}]});
        if(files.Count==1&&files[0].TryGetLocalPath() is { } path)await vm.OptionalSdks.ImportAsync(path);
    }
}
