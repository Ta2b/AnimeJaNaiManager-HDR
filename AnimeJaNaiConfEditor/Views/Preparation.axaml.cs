using AnimeJaNaiConfEditor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.Views;
public partial class Preparation : UserControl
{
    public Preparation(){InitializeComponent();}
    async Task File(Action<PreparationViewModel,string> select,bool destination=false)
    {
        if(DataContext is not PreparationViewModel vm||!vm.NotBusy)return;
        try
        {
            var storage=TopLevel.GetTopLevel(this)!.StorageProvider;
            if(destination)
            {
                var chosen=await storage.SaveFilePickerAsync(new FilePickerSaveOptions{Title="New prepared video",DefaultExtension=vm.OutputCodec=="lossless-rgb"?"mkv":"nut",SuggestedFileName="prepared-gmfss"});
                if(chosen?.TryGetLocalPath() is { } path)select(vm,path);
            }
            else
            {
                var chosen=await storage.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Select local file",AllowMultiple=false});
                if(chosen.Count>0&&chosen[0].TryGetLocalPath() is { } path)select(vm,path);
            }
        }
        catch(Exception e){vm.PickerError("Cannot choose preparation file: "+e.Message);}
    }
    async Task Folder(Action<PreparationViewModel,string> select)
    {
        if(DataContext is not PreparationViewModel vm||!vm.NotBusy)return;
        try
        {
            var chosen=await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Select model folder",AllowMultiple=false});
            if(chosen.Count>0&&chosen[0].TryGetLocalPath() is { } path)select(vm,path);
        }
        catch(Exception e){vm.PickerError("Cannot choose preparation folder: "+e.Message);}
    }
    async void SourceClick(object? sender,RoutedEventArgs e)=>await File((vm,path)=>vm.Source=path);
    async void DestinationClick(object? sender,RoutedEventArgs e)=>await File((vm,path)=>vm.Destination=path,true);
    async void PythonClick(object? sender,RoutedEventArgs e)=>await File((vm,path)=>vm.Python=path);
    async void ModelClick(object? sender,RoutedEventArgs e)=>await Folder((vm,path)=>vm.ModelSource=path);
    async void WeightsClick(object? sender,RoutedEventArgs e)=>await Folder((vm,path)=>vm.Weights=path);
    async void InspectClick(object? sender,RoutedEventArgs e){if(DataContext is PreparationViewModel vm)await vm.InspectAsync();}
    async void PrepareClick(object? sender,RoutedEventArgs e){if(DataContext is PreparationViewModel vm)await vm.PrepareAsync();}
}
