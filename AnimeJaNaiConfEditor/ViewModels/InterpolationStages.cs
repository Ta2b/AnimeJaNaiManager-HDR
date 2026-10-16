using AnimeJaNai.Platform;
using Avalonia.Collections;
using ReactiveUI;
using Salaros.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AnimeJaNaiConfEditor.ViewModels;

// Persist the shared contract, never flatten a mixed pipeline into the old RIFE
// toggle. Compatibility keys are emitted only for a lossless legacy projection.
public partial class UpscaleChain
{
    private bool _orderedPipeline;
    private string _pipelineReadError = "";
    private Dictionary<string,string> _rawChain = new();
    internal bool PipelineEditing { get; private set; }
    [DataMember] public bool OrderedPipeline
    {
        get => _orderedPipeline;
        set
        {
            if (value == _orderedPipeline || PipelineReadError.Length > 0) return;
            if (value) EnableOrderedPipeline();
            else if (TryLegacyProjection(out var legacy))
            {
                PipelineEditing = true;
                try
                {
                    EnableRife = legacy.Rife != null;
                    if (legacy.Rife is { } rife)
                    {
                        RifeModel = MainWindowViewModel.RifeValueToLabel(rife.Model);
                        RifeFactorNumerator = checked((int)rife.Factor.Numerator);
                        RifeFactorDenominator = checked((int)rife.Factor.Denominator);
                        RifeEnsemble = Opt(rife,"ensemble","no") == "yes";
                        RifeSceneDetectThreshold = decimal.Parse(Opt(rife,"scene_detect_threshold","0.150"), CultureInfo.InvariantCulture);
                        RifeBeforeUpscale = legacy.Before;
                    }
                    Models = new AvaloniaList<UpscaleModel>(legacy.Models.Select((stage,i) => new UpscaleModel(AutoSave) {
                        Vm=Vm, AllModels=Vm?.GetAllModels() ?? [], ModelNumber=(i+1).ToString(), Name=stage.Model,
                        ResizeFactorBeforeUpscale=Opt(stage,"resize_factor_before_upscale","100"),
                        ResizeHeightBeforeUpscale=Opt(stage,"resize_height_before_upscale","0"),
                        Frames=Opt(stage,"frames","0"), TemporalSceneThreshold=Opt(stage,"temporal_scene_threshold","0.150")
                    }));
                    _orderedPipeline = false;
                }
                finally { PipelineEditing = false; }
                ChangedPipeline();
            }
        }
    }
    [DataMember] public AvaloniaList<InterpolationStage> Stages { get; set; } = [];
    public string PipelineReadError => _pipelineReadError;
    public bool PipelineEditable => PipelineReadError.Length == 0;
    public bool LegacyEditorVisible => !OrderedPipeline;
    public bool CanUseLegacyEditor => PipelineEditable && (!OrderedPipeline || TryLegacyProjection(out _));
    public string OtherInterpolationMethods => "GMFSS, DRBA/DistilDRBA, GIMM, FILM, EISAI, SG-RIFE, FrameFusion and generative workflows are separate integrations. They become selectable here when their reader/preparation provider is delivered.";
    public string PipelineOrder => "Source → " + string.Join(" → ", Stages.Where(s=>s.Enabled).Select(s=>s.OrderDescription)) + " → Display";
    public string PipelineCadence
    {
        get
        {
            if (!TryPlan(out var plan, out var error)) return error;
            var fps = new InterpolationPipeline.Rational(24,1);
            var points = new List<string>{"24"};
            foreach (var s in plan.Stages.Where(s => s.Enabled && s.Kind == "interpolate"))
            { fps=fps.Multiply(s.Factor); points.Add(fps.Denominator==1?fps.Numerator.ToString():fps.ToString()); }
            return string.Join(" → ",points)+" fps (24 fps source example)";
        }
    }
    public string PipelineNotice
    {
        get
        {
            if (!PipelineEditable) return "Original stage configuration preserved, editing disabled: " + PipelineReadError;
            if (!TryPlan(out var plan,out var error)) return error;
            if (!OrderedPipeline) return "Legacy RIFE/upscaling settings are unchanged. Enable ordered stages to compose interpolation methods.";
            if (TryLegacyProjection(out _)) return "Lossless legacy-compatible layout: the existing reader arithmetic and screen synchronization are retained.";
            var n=plan.OutputCadence(new(1,1));
            if (n.Denominator!=1 || n.Numerator>16) return "This mixed cadence is unavailable: the current Linux reader requires integer stage factors and a cumulative factor ≤16.";
            return "Explicit mixed cadence. Linux NVIDIA/TensorRT reader required; GPU/color capability negotiated at playback. FRUC can produce severe artifacts on flashes and occlusions. No TrueHDR tagging on mixed chains.";
        }
    }
    public void LoadPipeline(IReadOnlyDictionary<string,string> section,string backend,string policy)
    {
        string prefix=$"chain_{ChainNumber}_";
        if (!section.Keys.Any(k=>k.Equals(prefix+"pipeline_version",StringComparison.OrdinalIgnoreCase)||k.StartsWith(prefix+"stage_",StringComparison.OrdinalIgnoreCase))) return;
        _rawChain=section.Where(k=>k.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToDictionary(k=>k.Key[prefix.Length..],k=>k.Value);
        PipelineEditing=true;
        try
        {
            _orderedPipeline=true;
            var plan=InterpolationPipeline.Read(section,int.Parse(ChainNumber),backend,policy);
            Stages=new AvaloniaList<InterpolationStage>(plan.Stages.Select(s=>new InterpolationStage(this,s)));
        }
        catch (Exception e) when(e is InvalidDataException or OverflowException or FormatException)
        { _pipelineReadError=e.Message; }
        finally { PipelineEditing=false; }
        RefreshPipeline();
    }
    private InterpolationPipeline.Plan LegacyPlan()
    {
        var values=new Dictionary<string,string>();string p=$"chain_{ChainNumber}_";
        values[p+"rife"]=EnableRife?"yes":"no";
        values[p+"rife_model"]=MainWindowViewModel.RifeLabelToValue(RifeModel) ?? "414";
        values[p+"rife_factor_numerator"]=(RifeFactorNumerator??2).ToString(CultureInfo.InvariantCulture);
        values[p+"rife_factor_denominator"]=(RifeFactorDenominator??1).ToString(CultureInfo.InvariantCulture);
        values[p+"rife_ensemble"]=RifeEnsemble?"yes":"no";
        values[p+"rife_scene_detect_threshold"]=(RifeSceneDetectThreshold??.150m).ToString(CultureInfo.InvariantCulture);
        values[p+"rife_before_upscale"]=RifeBeforeUpscale?"yes":"no";
        for(int i=0;i<Models.Count;i++)
        {
            var m=Models[i];string k=p+$"model_{i+1}_";values[k+"name"]=m.Name;
            values[k+"resize_factor_before_upscale"]=m.ResizeFactorBeforeUpscale;
            values[k+"resize_height_before_upscale"]=m.ResizeHeightBeforeUpscale;
            values[k+"frames"]=m.Frames;values[k+"temporal_scene_threshold"]=m.TemporalSceneThreshold;
        }
        return InterpolationPipeline.Read(values,int.Parse(ChainNumber),Vm?.AnimeJaNaiConf?.SelectedBackend.ToString()??"tensorrt",Vm?.AnimeJaNaiConf?.RifeEngine??"fast");
    }
    public void EnableOrderedPipeline()
    {
        if(OrderedPipeline || !PipelineEditable) return;
        PipelineEditing=true;
        try
        {
            Stages=new AvaloniaList<InterpolationStage>(LegacyPlan().Stages.Select(s=>new InterpolationStage(this,s)));
            _orderedPipeline=true;
        }
        catch(InvalidDataException e) { Vm!.BackendNotice=e.Message; }
        finally { PipelineEditing=false; }
        ChangedPipeline();
    }
    public void UseLegacyEditor() => OrderedPipeline=false;
    public void AddInterpolationStage() => AddRifeStage();
    public void AddRifeStage() => AddStage("rife","interpolate","tensorrt","quality","426",2);
    public bool CanAddFruc => Vm?.OptionalSdks?.FrucReady == true;
    public string FrucSetupHint => Vm?.OptionalSdks?.FrucStatus ?? "Install the Optical Flow SDK from Optional engines.";
    public void ShowOptionalEngines() => Vm?.ShowOptionalEngines();
    public void AddFrucStage() => AddStage("nvidia-fruc","interpolate","cuda","fast","",2);
    public void AddUpscaleStage() => AddStage("onnx","upscale","tensorrt","quality",Vm?.GetAllModels().FirstOrDefault()??"",1);
    private void AddStage(string method,string kind,string runtime,string policy,string model,int factor)
    {
        EnableOrderedPipeline();if(!OrderedPipeline || Stages.Count>=InterpolationPipeline.MaxStages || !PipelineEditable)return;
        var options=new Dictionary<string,string>();
        if(method=="nvidia-fruc") options["kernel_fatbin"]=Path.Combine(InterpolationStage.InferenceDirectory,"aji_fruc.fatbin");
        var stage=new InterpolationStage(this,new(Guid.NewGuid().ToString("N"),kind,method,runtime,false,model,new(factor,1),policy,options));
        Stages.Add(stage);ChangedPipeline();
    }
    public void DeleteStage(InterpolationStage stage) { if(PipelineEditable&&Stages.Remove(stage))ChangedPipeline(); }
    public void MoveStage(InterpolationStage stage,int delta)
    {
        int i=Stages.IndexOf(stage),n=i+delta;if(!PipelineEditable||i<0||n<0||n>=Stages.Count)return;
        // One mutation/save, so no transient missing-stage configuration is written.
        PipelineEditing=true;try { Stages.RemoveAt(i);Stages.Insert(n,stage); } finally { PipelineEditing=false; }
        ChangedPipeline();
    }
    internal void ChangedPipeline()
    {
        RefreshPipeline();if(!PipelineEditing && AutoSave)Vm?.WriteAnimeJaNaiConf();
    }
    public void RefreshPipeline()
    {
        foreach(string name in new[]{nameof(OrderedPipeline),nameof(LegacyEditorVisible),nameof(PipelineEditable),nameof(PipelineReadError),nameof(Stages),nameof(PipelineCadence),nameof(PipelineOrder),nameof(PipelineNotice),nameof(CanUseLegacyEditor),nameof(CanAddFruc),nameof(FrucSetupHint)}) this.RaisePropertyChanged(name);
        foreach(var stage in Stages)stage.Refresh();
    }
    internal bool TryPlan(out InterpolationPipeline.Plan plan,out string error)
    {
        try { plan=OrderedPipeline?new(false,Stages.Select(s=>s.ToStage()).ToArray()):LegacyPlan();InterpolationPipeline.Validate(plan);error="";return true; }
        catch(Exception e) when(e is InvalidDataException or OverflowException or FormatException) { plan=new(false,[]);error=e.Message;return false; }
    }
    private sealed record LegacyProjection(InterpolationPipeline.Stage? Rife,bool Before,InterpolationPipeline.Stage[] Models);
    private static string Opt(InterpolationPipeline.Stage s,string key,string fallback) => s.Options.TryGetValue(key,out var v)?v:fallback;
    private bool TryLegacyProjection(out LegacyProjection projection,string? enableId=null)
    {
        projection=new(null,true,[]);if(!TryPlan(out var plan,out _))return false;
        var active=plan.Stages.Where(s=>s.Enabled||s.Id==enableId).ToArray();var r=active.Where(s=>s.Kind=="interpolate").ToArray();
        string backend=(Vm?.AnimeJaNaiConf?.SelectedBackend.ToString()??"tensorrt").ToLowerInvariant();
        if(active.Any(s=>s.Runtime!=backend)||r.Length>1||r.Any(s=>s.Method!="rife"))return false;
        var models=active.Where(s=>s.Kind=="upscale").ToArray();
        if(active.Length!=models.Length+r.Length||models.Any(s=>s.Method!="onnx"))return false;
        foreach(var stage in active)
        {
            var allowed=stage.Kind=="interpolate"?new[]{"ensemble","scene_detect_threshold"}:new[]{"resize_factor_before_upscale","resize_height_before_upscale","frames","temporal_scene_threshold"};
            if(stage.Options.Keys.Except(allowed).Any())return false;
        }
        if(r.FirstOrDefault() is { } rife)
        {
            if(!Regex.IsMatch(rife.Model,@"^[0-9]{2,4}$") || Opt(rife,"ensemble","no") is not ("yes" or "no") ||
                !decimal.TryParse(Opt(rife,"scene_detect_threshold","0.150"),NumberStyles.Float,CultureInfo.InvariantCulture,out var scd)||scd<0||scd>1)return false;
            if(rife.Policy!=QualitySemantics.CanonicalTemporal(Vm?.AnimeJaNaiConf?.RifeEngine??"fast"))return false;
            int i=Array.IndexOf(active,rife);if(i!=0&&i!=active.Length-1)return false;
            projection=new(rife,i==0,models);
        }
        else projection=new(null,true,models);
        return true;
    }
    internal bool CanProjectStage(string id) => TryLegacyProjection(out _,id);
    internal void WritePipeline(ConfigParser parser,string section)
    {
        string prefix=$"chain_{ChainNumber}_";
        if(!PipelineEditable) { foreach(var kv in _rawChain)parser.SetValue(section,prefix+kv.Key,kv.Value);return; }
        if(!TryPlan(out var plan,out var error))throw new InvalidDataException(error);
        foreach(var kv in InterpolationPipeline.Write(plan,int.Parse(ChainNumber)))parser.SetValue(section,kv.Key,kv.Value);
        parser.SetValue(section,prefix+"rife","no");
        if(!TryLegacyProjection(out var legacy))return;
        if(legacy.Rife is { } r)
        {
            parser.SetValue(section,prefix+"rife","yes");parser.SetValue(section,prefix+"rife_model",r.Model);
            parser.SetValue(section,prefix+"rife_factor_numerator",r.Factor.Numerator.ToString(CultureInfo.InvariantCulture));
            parser.SetValue(section,prefix+"rife_factor_denominator",r.Factor.Denominator.ToString(CultureInfo.InvariantCulture));
            parser.SetValue(section,prefix+"rife_before_upscale",legacy.Before?"yes":"no");
            parser.SetValue(section,prefix+"rife_ensemble",Opt(r,"ensemble","no"));
            parser.SetValue(section,prefix+"rife_scene_detect_threshold",Opt(r,"scene_detect_threshold","0.150"));
        }
        for(int i=0;i<legacy.Models.Length;i++)
        {
            string p=prefix+$"model_{i+1}_";var m=legacy.Models[i];parser.SetValue(section,p+"name",m.Model);
            foreach(var kv in m.Options)parser.SetValue(section,p+kv.Key,kv.Value);
        }
    }
}

[DataContract]
public sealed class InterpolationStage : ReactiveObject
{
    private readonly UpscaleChain _chain;
    private bool _loading;
    private string _kind,_method,_runtime,_model,_policy,_numerator,_denominator;
    private bool _enabled;
    private readonly Dictionary<string,string> _options;
    private readonly StageTechnology[] _technologies;
    public InterpolationStage(UpscaleChain chain,InterpolationPipeline.Stage s)
    {
        _chain=chain;Id=s.Id;_kind=s.Kind;_method=s.Method;_runtime=s.Runtime;_model=s.Model;_policy=s.Policy;_enabled=s.Enabled;
        _numerator=s.Factor.Numerator.ToString(CultureInfo.InvariantCulture);_denominator=s.Factor.Denominator.ToString(CultureInfo.InvariantCulture);_options=new(s.Options);
        _technologies=MethodChoices.Select(name=>new StageTechnology(name,true,"")).ToArray();
    }
    [DataMember] public string Id { get; private set; }
    [DataMember] public string Kind { get=>_kind;private set=>Set(ref _kind,value); }
    [DataMember] public string Method { get=>_method;set=>Set(ref _method,value); }
    [DataMember] public string Runtime { get=>_runtime;set=>Set(ref _runtime,value); }
    [DataMember] public string Model { get=>_model;set=>Set(ref _model,value); }
    [DataMember] public string Policy { get=>_policy;set=>Set(ref _policy,value); }
    [DataMember] public string Numerator { get=>_numerator;set=>Set(ref _numerator,value); }
    [DataMember] public string Denominator { get=>_denominator;set=>Set(ref _denominator,value); }
    [DataMember] public bool Enabled { get=>_enabled;set { if(_enabled==value)return;this.RaiseAndSetIfChanged(ref _enabled,value);Changed(); } }
    public string Category => Kind switch {"interpolate"=>"Interpolation","upscale"=>"Upscale","resize"=>"Downscale","hdr"=>"HDR",_=>Kind};
    public string[] MethodChoices => Kind switch {"interpolate"=>["RIFE","NVIDIA FRUC"],"upscale"=>["ONNX upscale"],_=>[MethodChoice]};
    public StageTechnology[] TechnologyChoices => _technologies;
    public StageTechnology SelectedTechnology {
        get=>TechnologyChoices.FirstOrDefault(t=>t.Name==MethodChoice)??new(MethodChoice,false,"Provider unavailable.");
        set {if(value!=null&&value.Available&&MethodChoices.Contains(value.Name))MethodChoice=value.Name;}
    }
    public string[] PolicyChoices => ["quality","fast"];
    public string MethodChoice
    {
        get=>Method switch {"rife"=>"RIFE","nvidia-fruc"=>"NVIDIA FRUC","onnx"=>"ONNX upscale",_=>Method};
        set
        {
            if(value==MethodChoice || !MethodChoices.Contains(value)||!TechnologyEditable)return;
            _loading=true;
            try
            {
                _options.Clear();Model="";Numerator="2";Denominator="1";Enabled=false;
                switch(value)
                {
                    case "RIFE":Kind="interpolate";Method="rife";Runtime="tensorrt";Policy="quality";Model="426";break;
                    case "NVIDIA FRUC":Kind="interpolate";Method="nvidia-fruc";Runtime="cuda";Policy="fast";KernelFatbin=Path.Combine(InferenceDirectory,"aji_fruc.fatbin");break;
                    case "ONNX upscale":Kind="upscale";Method="onnx";Runtime="tensorrt";Policy="quality";Numerator="1";Model=_chain.Vm?.GetAllModels().FirstOrDefault()??"";break;
                    default:return;
                }
            }
            finally{_loading=false;}
            Changed();
        }
    }
    public AvaloniaList<string> ModelChoices => Method=="rife"?new(MainWindowViewModel.RifeModels.Select(MainWindowViewModel.RifeLabelToValue)):_chain.Vm?.GetAllModels()??[];
    public bool IsRife => Method=="rife";
    public bool IsFruc => Method=="nvidia-fruc";
    public bool IsUpscale => Kind=="upscale";
    public bool HasAttachedResize => ResizeHeight!="0"||ResizeFactor!="100";
    public bool IsInterpolation => Kind=="interpolate";
    public string ModeNotice => Method switch {
        "rife" when Policy=="quality" => "Full RIFE prediction (reference).",
        "rife" when Policy=="fast" => "Motion shortcuts can alter contours.",
        "nvidia-fruc" => "SDR8 ×2. Artifacts can be severe on flashes, fast light changes and occlusions. Prefer RIFE for these scenes.",
        _ => ""
    };

    public string OrderDescription => IsUpscale
        ? ((ResizeHeight!="0" ? $"Resize to {ResizeHeight}p → " : ResizeFactor!="100" ? $"Resize to {ResizeFactor}% → " : "")+"ONNX upscale")
        : IsInterpolation ? $"{MethodChoice} ×{Numerator}"+(Denominator!="1"?$"/{Denominator}":"") : MethodChoice;
    public bool CanMoveUp => _chain.Stages.IndexOf(this)>0;
    public bool CanMoveDown => _chain.Stages.IndexOf(this)<_chain.Stages.Count-1;
    public string Header => $"{_chain.Stages.IndexOf(this)+1}. {Category}";
    public static string InferenceDirectory => Environment.GetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR")??Path.Combine(MainWindowViewModel.DataDir,"inference");
    private string Get(string key,string fallback="")=>_options.TryGetValue(key,out var v)?v:fallback;
    private void Option(string key,string value){if(Get(key)==value)return;_options[key]=value;Changed();}
    public bool Ensemble {get=>Get("ensemble","no")=="yes";set=>Option("ensemble",value?"yes":"no");}
    public string SceneThreshold {get=>Get("scene_detect_threshold","0.150");set=>Option("scene_detect_threshold",value);}
    public string SdkLibrary {get=>ResolveSdkPath("sdk_library",_chain.Vm?.OptionalSdks?.InstalledFruc?.Library);set=>Option("sdk_library",value);}
    public string SdkRuntime {get=>ResolveSdkPath("sdk_runtime",_chain.Vm?.OptionalSdks?.InstalledFruc?.Runtime);set=>Option("sdk_runtime",value);}
    private string ResolveSdkPath(string key,string? installed) {
        string saved=Get(key);return saved.Length==0||_chain.Vm?.OptionalSdks?.IsManagedPath(saved)==true?installed??"":saved;
    }
    public bool CanUseInstalledSdk => IsFruc&&_chain.Vm?.OptionalSdks?.InstalledFruc!=null&&new[]{"sdk_library","sdk_runtime"}.Any(k=>Get(k).Length>0&&_chain.Vm.OptionalSdks.IsManagedPath(Get(k))!=true);
    public void UseInstalledSdk() {
        if(!CanUseInstalledSdk)return;_options.Remove("sdk_library");_options.Remove("sdk_runtime");Changed();
    }
    public string KernelFatbin {get=>Get("kernel_fatbin");set=>Option("kernel_fatbin",value);}
    public string ResizeHeight {get=>Get("resize_height_before_upscale","0");set=>Option("resize_height_before_upscale",value);}
    public string ResizeFactor {get=>Get("resize_factor_before_upscale","100");set=>Option("resize_factor_before_upscale",value);}
    public string Frames {get=>Get("frames","0");set=>Option("frames",value);}
    public string TemporalThreshold {get=>Get("temporal_scene_threshold","0.150");set=>Option("temporal_scene_threshold",value);}
    public bool TechnologyEditable => _options.Keys.All(k=>(IsRife?new[]{"ensemble","scene_detect_threshold"}:IsFruc?new[]{"sdk_library","sdk_runtime","kernel_fatbin"}:IsUpscale?new[]{"resize_factor_before_upscale","resize_height_before_upscale","frames","temporal_scene_threshold"}:Array.Empty<string>()).Contains(k));
    public string OptionsText => string.Join("; ",_options.OrderBy(k=>k.Key).Select(k=>$"{k.Key}={k.Value}"));
    public string Availability
    {
        get
        {
            if(!_chain.TryPlan(out var plan,out var error))return error;
            var stage=ToStage();
            var known=Method=="rife"?new[]{"ensemble","scene_detect_threshold"}:Method=="nvidia-fruc"?new[]{"sdk_library","sdk_runtime","kernel_fatbin"}:new[]{"resize_factor_before_upscale","resize_height_before_upscale","frames","temporal_scene_threshold"};
            if(_options.Keys.Except(known).Any())return "Option not implemented by this reader: "+string.Join(", ",_options.Keys.Except(known));
            if(Kind!=(Method=="onnx"?"upscale":"interpolate"))return "Method and stage kind do not match.";
            if(Method=="rife" && (Get("ensemble","no") is not ("yes" or "no") || !decimal.TryParse(SceneThreshold,NumberStyles.Float,CultureInfo.InvariantCulture,out var scd)||scd<0||scd>1))return "RIFE requires ensemble=yes/no and a scene threshold0..1.";
            if(!_chain.CanProjectStage(Id))
            {
                try
                {
                    var cadence=plan.Stages.Where(s=>s.Enabled||s.Id==Id).Where(s=>s.Kind=="interpolate").Aggregate(new InterpolationPipeline.Rational(1,1),(f,s)=>f.Multiply(s.Factor));
                    if(cadence.Denominator!=1||cadence.Numerator>16)return "Unavailable mixed cadence: integer stage factors, cumulative factor ≤16 required.";
                }
                catch(InvalidDataException e) { return e.Message; }
            }
            if(!MainWindowViewModel.OnLinux)return "Ordered mixed execution is currently Linux CUDA only; this stage is preserved for its target machine.";
            if(_chain.Vm?.AnimeJaNaiConf?.TensorRtSelected!=true)return "Mixed stages require the TensorRT backend. Legacy-equivalent settings may use the historical platform backend.";
            foreach(var name in new[]{"libaji_pipeline.so","aji_pipeline.fatbin","libaji_trt.so"})if(!File.Exists(Path.Combine(InferenceDirectory,name)))return "Missing reader module: "+name;
            if(Method=="nvidia-fruc"&&Runtime=="cuda")
            {
                if(Policy!="fast"||stage.Factor.Numerator!=2||stage.Factor.Denominator!=1)return "FRUC requires explicit Fast ×2; synthesis precision is SDR8bit.";
                foreach(var path in new[]{SdkLibrary,SdkRuntime,KernelFatbin})if(!Path.IsPathFullyQualified(path)||!File.Exists(path))return "FRUC SDK is missing. Import the Optical Flow SDK ZIP from Optional engines.";
                if(!File.Exists(Path.Combine(InferenceDirectory,"libaji_fruc.so")))return "Missing provider libaji_fruc.so.";
            }
            else if(Method=="rife"&&Runtime=="tensorrt")
            {
                if(!Regex.IsMatch(Model,@"^[0-9]{2,4}$"))return "RIFE model must be an installed numeric variant ID (for example426).";
                string model=MainWindowViewModel.RifeValueToLabel(Model);
                if(!MainWindowViewModel.RifeModels.Contains(model))return "RIFE model missing: "+Model;
                if(Ensemble&&!Directory.EnumerateFiles(Path.Combine(MainWindowViewModel.DataDir,"rife"),"*_ensemble.onnx").Any(f=>MainWindowViewModel.RifeModels.Contains(model)&&RifeFileValue(f)==Model))return "RIFE ensemble weights missing for model "+Model;
                if(!_chain.CanProjectStage(Id) && (stage.Factor.Denominator!=1||stage.Factor.Numerator<2||stage.Factor.Numerator>8))return "Mixed RIFE requires integer factor2..8; fractional legacy layouts retain the original reader.";
            }
            else if(Method=="onnx"&&Runtime=="tensorrt"&&Kind=="upscale")
            {
                if(!File.Exists(Path.Combine(_chain.Vm!.OnnxPath,Model+".onnx")))return "ONNX weights missing: "+Model;
                var temporal=new UpscaleModel(false){Vm=_chain.Vm,Name=Model,Frames=Frames};if(temporal.IsTemporal)return "Temporal SR is unavailable inside this mixed reader; preserve the legacy path for this model.";
            }
            else return "No reader provider delivered for "+Method+" / "+Runtime+". Preserved, not selectable as a working method.";
            return "Files present. GPU, driver, source format/color and engine plans are negotiated by the reader; this is not a hardware validation.";
        }
    }
    private static string RifeFileValue(string file)
    {
        var m=Regex.Match(Path.GetFileName(file),@"rife_v(\d+)\.(\d+)(_lite)?(_ensemble)?\.onnx$");return m.Success?m.Groups[1].Value+m.Groups[2].Value+(m.Groups[3].Success?"1":""):"";
    }
    public string AvailabilityLabel => Availability.StartsWith("Files present",StringComparison.Ordinal)?"Ready":Availability;
    // Disabling an imported stage must remain possible even on another machine.
    public bool CanEnable => Enabled||Availability.StartsWith("Files present",StringComparison.Ordinal);
    public void MoveUp()=>_chain.MoveStage(this,-1);
    public void MoveDown()=>_chain.MoveStage(this,1);
    public void Remove()=>_chain.DeleteStage(this);
    private void Set(ref string field,string value,[CallerMemberName] string? name=null) {value??="";if(field==value)return;this.RaiseAndSetIfChanged(ref field,value,name);Changed();}
    private void Changed(){if(!_loading)_chain.ChangedPipeline();}
    internal void Refresh()
    {
        foreach(var technology in _technologies)technology.Update(technology.Name!="NVIDIA FRUC"||MainWindowViewModel.OnLinux&&(_chain.Vm?.OptionalSdks?.FrucReady==true||IsFruc&&Availability.StartsWith("Files present",StringComparison.Ordinal)),technology.Name=="NVIDIA FRUC"?_chain.FrucSetupHint:"");
        foreach(var name in new[]{nameof(Header),nameof(Category),nameof(MethodChoice),nameof(TechnologyChoices),nameof(SelectedTechnology),nameof(ModelChoices),nameof(IsRife),nameof(IsFruc),nameof(IsUpscale),nameof(HasAttachedResize),nameof(IsInterpolation),nameof(ModeNotice),nameof(OrderDescription),nameof(CanMoveUp),nameof(CanMoveDown),nameof(Availability),nameof(AvailabilityLabel),nameof(CanEnable),nameof(Ensemble),nameof(SceneThreshold),nameof(SdkLibrary),nameof(SdkRuntime),nameof(CanUseInstalledSdk),nameof(KernelFatbin),nameof(ResizeHeight),nameof(ResizeFactor),nameof(Frames),nameof(TemporalThreshold),nameof(OptionsText),nameof(TechnologyEditable)})this.RaisePropertyChanged(name);
    }
    internal InterpolationPipeline.Stage ToStage()
    {
        if(!long.TryParse(Numerator,NumberStyles.None,CultureInfo.InvariantCulture,out long n)||!long.TryParse(Denominator,NumberStyles.None,CultureInfo.InvariantCulture,out long d))throw new InvalidDataException("Stage factors require positive integers.");
        var options=new Dictionary<string,string>(_options);
        if(IsFruc) {options["sdk_library"]=SdkLibrary;options["sdk_runtime"]=SdkRuntime;options["kernel_fatbin"]=KernelFatbin;}
        return new(Id,Kind,Method,Runtime,Enabled,Model,new(n,d),Policy,options);
    }
}

public sealed class StageTechnology : ReactiveObject
{
    public string Name { get; }
    private bool available;
    private string reason;
    public bool Available {get=>available;private set=>this.RaiseAndSetIfChanged(ref available,value);}
    public string Reason {get=>reason;private set=>this.RaiseAndSetIfChanged(ref reason,value);}
    public StageTechnology(string name,bool enabled,string why){Name=name;available=enabled;reason=why;}
    public void Update(bool enabled,string why){Available=enabled;Reason=why;}
}
