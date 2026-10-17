using System.Collections.Generic;
namespace AnimeJaNaiConfEditor.Services;
// The launch resolver and Manager inspect the same model metadata.
public static class OnnxInputShape
{
    public static int TemporalFrames(string path)=>AnimeJaNai.Platform.OnnxModelShape.TemporalFrames(path);
    public static List<long>? InputDims(byte[] data)=>AnimeJaNai.Platform.OnnxModelShape.InputDims(data);
}
