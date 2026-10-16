using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
namespace AnimeJaNaiConfEditor.ViewModels;
internal static class PipelineReaderFeatures
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong Features();
    static readonly Dictionary<string,(long Size,long Stamp,ulong Value)> cache = new();
    public static bool HasAreaDownscale(string directory)
    {
        if(!MainWindowViewModel.OnLinux)return false;
        string path=Path.GetFullPath(Path.Combine(directory,"libaji_pipeline.so"));
        var file=new FileInfo(path);if(!file.Exists)return false;
        lock(cache) {
            long size=file.Length,stamp=file.LastWriteTimeUtc.Ticks;
            if(cache.TryGetValue(path,out var found)&&found.Size==size&&found.Stamp==stamp)return (found.Value&1)!=0;
            ulong bits=0;IntPtr handle=IntPtr.Zero;
            try {
                handle=NativeLibrary.Load(path);
                if(NativeLibrary.TryGetExport(handle,"aji_pipeline_features_v1",out var fn))bits=Marshal.GetDelegateForFunctionPointer<Features>(fn)();
            } catch(Exception e) when(e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
            finally {if(handle!=IntPtr.Zero)NativeLibrary.Free(handle);}
            cache[path]=(size,stamp,bits);return (bits&1)!=0;
        }
    }
}
