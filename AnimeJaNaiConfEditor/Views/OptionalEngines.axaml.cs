using AnimeJaNaiConfEditor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
namespace AnimeJaNaiConfEditor.Views;
public partial class OptionalEngines : UserControl
{
    public OptionalEngines()=>InitializeComponent();
    private async void PythonClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm||vm.Preparation.IsBusy)return;
        var top=TopLevel.GetTopLevel(this);if(top==null)return;
        var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Choose GMFSS Python executable",AllowMultiple=false});
        if(files.Count==1&&files[0].TryGetLocalPath() is {} path)vm.Preparation.Python=path;
    }
    private async System.Threading.Tasks.Task ChooseFolder(bool weights)
    {
        if(DataContext is not MainWindowViewModel vm||vm.Preparation.IsBusy)return;
        var top=TopLevel.GetTopLevel(this);if(top==null)return;
        var folders=await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title=weights?"Choose GMFSS train_log weights":"Choose GMFSS source folder",AllowMultiple=false});
        if(folders.Count==1&&folders[0].TryGetLocalPath() is {} path){if(weights)vm.Preparation.Weights=path;else vm.Preparation.ModelSource=path;}
    }
    private async void ModelClick(object? sender,RoutedEventArgs e)=>await ChooseFolder(false);
    private async void WeightsClick(object? sender,RoutedEventArgs e)=>await ChooseFolder(true);
    private async void ImportTrueHdrSdkClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm||!vm.OptionalSdks.CanImport)return;
        var top=TopLevel.GetTopLevel(this);if(top==null)return;
        var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Import Linux Maxine Core and TrueHDR feature",AllowMultiple=true,FileTypeFilter=[new("NVIDIA SDK archives"){Patterns=["*.zip","*.tgz","*.tar.gz","*.tar"]}]});
        foreach(var file in files)if(file.TryGetLocalPath() is {} path)await vm.OptionalSdks.ImportTrueHdrAsync(path);
    }
}
