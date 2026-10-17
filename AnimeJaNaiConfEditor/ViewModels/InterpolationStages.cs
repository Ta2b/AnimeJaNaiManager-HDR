using AnimeJaNaiConfEditor.Services;
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
    private string _pipelineReadError = "";
    private Dictionary<string,string> _rawChain = new();
    internal bool PipelineEditing { get; private set; }
    [DataMember] public AvaloniaList<InterpolationStage> Stages { get; set; } = [];
    public string PipelineReadError => _pipelineReadError;
    public bool PipelineEditable => PipelineReadError.Length == 0;
    private InterpolationStage? _selectedStage;
    public InterpolationStage? SelectedStage
    {
        get => _selectedStage;
        set { if(ReferenceEquals(_selectedStage,value))return;this.RaiseAndSetIfChanged(ref _selectedStage,value);this.RaisePropertyChanged(nameof(HasSelectedStage)); }
    }
    public bool HasSelectedStage => SelectedStage != null;
    public bool HasStages => Stages.Count > 0;
    public bool CanAddStage => PipelineEditable && Stages.Count < InterpolationPipeline.MaxStages;
    public string AddStageHint => CanAddStage ? "Choose a category, then its technology and settings." : PipelineEditable ? "This profile reaches the reader limit of 16 steps." : PipelineReadError;
    internal string SelectedRuntime => (MainWindowViewModel.OnMac ? Vm?.AnimeJaNaiConf?.AppleEngine??"metal" : Vm?.AnimeJaNaiConf?.SelectedBackend.ToString()??"tensorrt").ToLowerInvariant();
    internal string HistoricalResizeMethod => SelectedRuntime is "tensorrt" or "directml"||MainWindowViewModel.OnWindows&&SelectedRuntime=="migraphx"?"spline36":"bilinear";
    internal string DefaultRifeModel => SelectedRuntime=="vulkan"?"46":MainWindowViewModel.RifeModels.Select(MainWindowViewModel.RifeLabelToValue).FirstOrDefault()??"426";
    public string OtherInterpolationMethods => "GMFSS, DRBA/DistilDRBA, GIMM, FILM, EISAI, SG-RIFE, FrameFusion and generative workflows are separate integrations. They become selectable here when their reader/preparation provider is delivered.";
    public string PipelineOrder => Stages.Any(s=>s.Enabled) ? "Source → " + string.Join(" → ", Stages.Where(s=>s.Enabled).Select(s=>s.OrderDescription)) + " → Display" : "Source → Display (no active processing steps)";
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
            if (TryLegacyProjection(out _)) return "Processing order is shown below. Settings are applied inside each stage.";
            var n=plan.OutputCadence(new(1,1));
            if (n.Denominator!=1 || n.Numerator>16) return "This mixed cadence is unavailable: the current Linux reader requires integer stage factors and a cumulative factor ≤16.";
            return "Each step keeps its own settings. Check the selected step for any missing engine or unsupported combination.";
        }
    }
    public void LoadPipeline(IReadOnlyDictionary<string,string> section,string backend,string policy)
    {
        string prefix=$"chain_{ChainNumber}_";
        _rawChain=section.Where(k=>k.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToDictionary(k=>k.Key[prefix.Length..],k=>k.Value);
        PipelineEditing=true;
        _pipelineReadError="";
        try
        {
            var plan=InterpolationPipeline.Read(section,int.Parse(ChainNumber),backend,policy,name=>OnnxInputShape.TemporalFrames(Path.Combine(Vm?.OnnxPath??Path.Combine(MainWindowViewModel.DataDir,"onnx"),name+".onnx")),MainWindowViewModel.OnWindows?"windows":MainWindowViewModel.OnMac?"macos":"linux");
            Stages=new AvaloniaList<InterpolationStage>(plan.Stages.Select(s=>new InterpolationStage(this,s)));
            SelectedStage=Stages.FirstOrDefault();
        }
        catch (Exception e) when(e is InvalidDataException or OverflowException or FormatException)
        { Stages=[];SelectedStage=null;_pipelineReadError=e.Message; }
        finally { PipelineEditing=false; }
        RefreshPipeline();
    }
    public void AddInterpolationStage() => AddRifeStage();
    public void AddRifeStage() => AddStage("rife","interpolate",SelectedRuntime,"quality",DefaultRifeModel,2);
    public void ShowOptionalEngines() => Vm?.ShowOptionalEngines();
    public void AddHdrStage() {if(!CanAddHdrStage)return;if(OwnerProfile is {} profile)profile.HdrStageControlled=true;AddStage("mpv","hdr","gpu-next","quality","",1);}
    public string HdrSetupHint => ProfileHdrCount>0?"This profile already has an HDR step, including inactive steps and other rules.":OwnerProfile?.Chains.Any(c=>!c.PipelineEditable)==true?"Another rule has an unsupported configuration. Resolve it before adding HDR, to keep one conversion per profile.":"Choose mpv HDR without an SDK, or NVIDIA TrueHDR with its optional SDK.";
    public void AddDownscaleStage()
    {
        bool independent=MainWindowViewModel.OnLinux&&SelectedRuntime=="tensorrt";
        AddStage(independent?"area":HistoricalResizeMethod,"resize",independent?"cuda":SelectedRuntime,"quality","",1);
    }
    public void AddUpscaleStage() => AddStage("onnx","upscale",SelectedRuntime,"quality",Vm?.GetAllModels().FirstOrDefault()??"",1);
    private UpscaleSlot? OwnerProfile => Vm?.AnimeJaNaiConf?.UpscaleSlots.FirstOrDefault(p=>p.Chains.Contains(this));
    public int ProfileHdrCount => OwnerProfile?.Chains.Sum(c=>c.Stages.Count(s=>s.Kind=="hdr"))??Stages.Count(s=>s.Kind=="hdr");
    public bool CanAddHdrStage => CanAddStage&&ProfileHdrCount==0&&OwnerProfile?.Chains.All(c=>c.PipelineEditable)!=false;
    public string ProfileHdrNotice => ProfileHdrCount>1?"Only one HDR conversion stage is allowed per profile, across all its chains.":"";
    private void AddStage(string method,string kind,string runtime,string policy,string model,int factor)
    {
        if(Stages.Count>=InterpolationPipeline.MaxStages || !PipelineEditable)return;
        var options=new Dictionary<string,string>();
        if(kind=="resize")options["target_height"]="720";
        if(kind=="hdr") {
            if(method=="nvidia-truehdr")options["sdk_root"]=Vm?.OptionalSdks?.TrueHdrRoot??"/usr/local/VideoFX";
            options["contrast"]=100.ToString(CultureInfo.InvariantCulture);
            options["saturation"]=100.ToString(CultureInfo.InvariantCulture);
            options["middle_gray"]=50.ToString(CultureInfo.InvariantCulture);
            if(method!="mpv")options["peak"]="1000";
        }
        var stage=new InterpolationStage(this,new(Guid.NewGuid().ToString("N"),kind,method,runtime,false,model,new(factor,1),policy,options));
        int hdrIndex=Stages.ToList().FindIndex(s=>s.Kind=="hdr");
        if(kind!="hdr"&&hdrIndex>=0)Stages.Insert(hdrIndex,stage);else Stages.Add(stage);SelectedStage=stage;ChangedPipeline();
    }
    public void DeleteStage(InterpolationStage stage)
    {
        int index=Stages.IndexOf(stage);
        if(!PipelineEditable||index<0)return;
        bool selected=ReferenceEquals(SelectedStage,stage);
        Stages.RemoveAt(index);
        if(selected)SelectedStage=Stages.Count==0?null:Stages[Math.Min(index,Stages.Count-1)];
        ChangedPipeline();
    }
    public void MoveStage(InterpolationStage stage,int delta)
    {
        int i=Stages.IndexOf(stage),n=i+delta;if(!PipelineEditable||i<0||n<0||n>=Stages.Count)return;
        // One mutation/save, so no transient missing-stage configuration is written.
        var selected=SelectedStage;
        PipelineEditing=true;try { Stages.RemoveAt(i);Stages.Insert(n,stage);SelectedStage=selected; } finally { PipelineEditing=false; }
        ChangedPipeline();
    }
    internal void ChangedPipeline()
    {
        if(OwnerProfile is {} profile)foreach(var chain in profile.Chains)chain.RefreshPipeline();else RefreshPipeline();
        if(!PipelineEditing && AutoSave)Vm?.WriteAnimeJaNaiConf();
    }
    public void RefreshPipeline()
    {
        foreach(string name in new[]{nameof(PipelineEditable),nameof(PipelineReadError),nameof(Stages),nameof(PipelineCadence),nameof(PipelineOrder),nameof(PipelineNotice),nameof(CanAddHdrStage),nameof(HdrSetupHint),nameof(ProfileHdrNotice)}) this.RaisePropertyChanged(name);
        if(SelectedStage==null||!Stages.Contains(SelectedStage))SelectedStage=Stages.FirstOrDefault();
        foreach(string name in new[]{nameof(HasStages),nameof(HasSelectedStage),nameof(CanAddStage),nameof(AddStageHint)})this.RaisePropertyChanged(name);
        foreach(var stage in Stages)stage.Refresh();
    }
    internal bool TryPlan(out InterpolationPipeline.Plan plan,out string error)
    {
        try { if(ProfileHdrNotice.Length>0)throw new InvalidDataException(ProfileHdrNotice);plan=new(false,Stages.Select(s=>s.ToStage()).ToArray());InterpolationPipeline.Validate(plan);error="";return true; }
        catch(Exception e) when(e is InvalidDataException or OverflowException or FormatException) { plan=new(false,[]);error=e.Message;return false; }
    }
    private sealed record LegacyProjection(InterpolationPipeline.Stage? Rife,bool Before,InterpolationPipeline.Stage[] Models);
    private static string Opt(InterpolationPipeline.Stage s,string key,string fallback) => s.Options.TryGetValue(key,out var v)?v:fallback;
    private bool TryLegacyProjection(out LegacyProjection projection,string? enableId=null)
    {
        projection=new(null,true,[]);if(!TryPlan(out var plan,out _))return false;
        InterpolationPipeline.Stage[] active;try {active=InterpolationPipeline.InferencePlan(plan with {Stages=plan.Stages.Select(s=>s.Id==enableId?s with {Enabled=true}:s).ToArray()}).Stages.ToArray();}catch(InvalidDataException){return false;}var r=active.Where(s=>s.Kind=="interpolate").ToArray();
        string backend=SelectedRuntime;
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
    private readonly List<StageTechnology> _technologies;
    public InterpolationStage(UpscaleChain chain,InterpolationPipeline.Stage s)
    {
        _chain=chain;Id=s.Id;_kind=s.Kind;_method=s.Method;_runtime=s.Runtime;_model=s.Model;_policy=s.Policy;_enabled=s.Enabled;
        _numerator=s.Factor.Numerator.ToString(CultureInfo.InvariantCulture);_denominator=s.Factor.Denominator.ToString(CultureInfo.InvariantCulture);_options=new(s.Options);
        _technologies=MethodChoices.Append(MethodChoice).Distinct().Select(name=>new StageTechnology(name,true,"")).ToList();
    }
    [DataMember] public string Id { get; private set; }
    [DataMember] public string Kind { get=>_kind;private set=>Set(ref _kind,value); }
    [DataMember] public string Method { get=>_method;set=>Set(ref _method,value); }
    [DataMember] public string Runtime { get=>_runtime;set=>Set(ref _runtime,value); }
    [DataMember] public string Model { get=>_model;set=>Set(ref _model,value); }
    // ComboBox clears selection while switching its DataContext; preserve the stored policy.
    [DataMember] public string Policy { get=>_policy;set {if(value!=null)Set(ref _policy,value);} }
    [DataMember] public string Numerator { get=>_numerator;set=>Set(ref _numerator,value); }
    [DataMember] public string Denominator { get=>_denominator;set=>Set(ref _denominator,value); }
    [DataMember] public bool Enabled { get=>_enabled;set { if(_enabled==value)return;this.RaiseAndSetIfChanged(ref _enabled,value);Changed(); } }
    public string Category => Kind switch {"interpolate"=>"Interpolation","upscale"=>"Upscale","resize"=>"Downscale","hdr"=>"HDR",_=>Kind};
    public string[] MethodChoices => Kind switch {"interpolate"=>IsFruc?["RIFE","NVIDIA FRUC (removed)"]:["RIFE"],"upscale"=>["ONNX upscale"],"resize"=>MainWindowViewModel.OnLinux&&_chain.SelectedRuntime=="tensorrt"?["Area","Spline36"]:_chain.HistoricalResizeMethod=="spline36"?["Spline36"]:["Bilinear"],"hdr"=>["mpv HDR","NVIDIA TrueHDR"],_=>[MethodChoice]};
    public StageTechnology[] TechnologyChoices => _technologies.Where(t=>MethodChoices.Contains(t.Name)||t.Name==MethodChoice).ToArray();
    public StageTechnology SelectedTechnology {
        get=>TechnologyChoices.FirstOrDefault(t=>t.Name==MethodChoice)??new(MethodChoice,false,"Provider unavailable.");
        set {if(value!=null&&value.Available&&MethodChoices.Contains(value.Name))MethodChoice=value.Name;}
    }
    public string[] PolicyChoices => ["quality","fast"];
    public string MethodChoice
    {
        get=>Method switch {"mpv"=>"mpv HDR","spline36"=>"Spline36","bilinear"=>"Bilinear","nvidia-truehdr"=>"NVIDIA TrueHDR","area"=>"Area","rife"=>"RIFE","nvidia-fruc"=>"NVIDIA FRUC (removed)","onnx"=>"ONNX upscale",_=>Method};
        set
        {
            if(value==MethodChoice || !MethodChoices.Contains(value)||!TechnologyEditable)return;
            _loading=true;
            try
            {
                _options.Clear();Model="";Numerator="2";Denominator="1";Enabled=false;
                switch(value)
                {
                    case "mpv HDR":Method="mpv";Runtime="gpu-next";Policy="quality";Numerator="1";break;
                    case "Bilinear":Method="bilinear";Runtime=_chain.SelectedRuntime;Policy="quality";Numerator="1";TargetHeight="720";break;
                    case "Spline36":Method="spline36";Runtime=_chain.SelectedRuntime;Policy="quality";Numerator="1";TargetHeight="720";break;
                    case "NVIDIA TrueHDR":Method="nvidia-truehdr";Runtime=MainWindowViewModel.OnWindows?"rtx-video":"cuda";Policy="quality";Numerator="1";break;
                    case "Area":Method="area";Runtime="cuda";Policy="quality";Numerator="1";TargetHeight="720";break;
                    case "RIFE":Method="rife";Runtime=_chain.SelectedRuntime;Policy="quality";Model=_chain.DefaultRifeModel;break;
                    case "ONNX upscale":Method="onnx";Runtime=_chain.SelectedRuntime;Policy="quality";Numerator="1";Model=_chain.Vm?.GetAllModels().FirstOrDefault()??"";break;
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
    public bool IsResize => Kind=="resize";
    public bool IsHdr => Kind=="hdr";
    public bool IsNvidiaHdr => IsHdr&&Method=="nvidia-truehdr";
    public bool IsMpvHdr => IsHdr&&Method=="mpv";
    public bool HasModel => IsRife||IsUpscale;

    public bool IsInterpolation => Kind=="interpolate";
    public bool IsLegacySizing => IsResize&&_options.ContainsKey("legacy_geometry");
    public string ModeNotice => IsLegacySizing?"Legacy sizing preserved. This imported step keeps the original reader’s geometry, including possible enlargement. Choose downscale-only sizing in Advanced settings to change that behavior.":Method switch {
        "rife" when Policy=="quality" => "Full RIFE prediction (reference).",
        "rife" when Policy=="fast" => "Motion shortcuts can alter contours.",
        "mpv" => "Converts SDR at the gpu-next render stage, without an NVIDIA SDK. HDR output must be negotiated. Native HDR is not converted again.",
        "bilinear" => "Bilinear downscale preserves the original platform calculation. It runs with the following upscaler when that layout is supported.",
        "spline36" => "Sharp Spline36 resampling. Preserves aspect ratio; smaller sources stay unchanged. The installed reader determines supported positions.",
        "nvidia-truehdr" => "Converts SDR BT.709 to HDR10 (PQ / BT.2020). SDK input is 8-bit. Place it after the current SDR engines. An HDR-only chain preserves an already-HDR source without converting it again.",
        "area" => "Preserves aspect ratio. Smaller sources stay unchanged. Downscaling reduces spatial detail.",
        "nvidia-fruc" => "FRUC has been retired. This imported step and its original settings are preserved. Disable or remove it, or explicitly choose another technology.",
        _ => ""
    };

    public string OrderDescription => IsHdr ? MethodChoice+" → HDR10" : IsResize ? IsLegacySizing?(ResizeByPercent?$"Legacy resize {TargetPercent}% ({MethodChoice})":$"Legacy resize {TargetHeight}p ({MethodChoice})"):(ResizeByPercent?$"Downscale to {TargetPercent}% ({MethodChoice})":$"Downscale ≤{TargetHeight}p ({MethodChoice})") : IsUpscale ? "ONNX upscale"
        : IsInterpolation ? $"{MethodChoice} ×{Numerator}"+(Denominator!="1"?$"/{Denominator}":"") : MethodChoice;
    public bool CanMoveUp => _chain.Stages.IndexOf(this)>0;
    public bool CanMoveDown => _chain.Stages.IndexOf(this)<_chain.Stages.Count-1;
    public string Summary => IsRife ? $"{MethodChoice} · {Policy} · ×{Numerator}"+(Denominator!="1"?$"/{Denominator}":"") : IsUpscale ? (Model.Length>0?Model:"Choose a model") : OrderDescription;
    public string StateLabel => Enabled ? Readiness.FilesPresent?"Active":"Unavailable" : "Inactive";
    public string RuntimeLabel => "Execution: "+Runtime;
    public bool HasAvailabilityAction => Readiness.MissingFiles;
    public bool CanUseSelectedBackend => Runtime!=_chain.SelectedRuntime &&
        (IsRife||IsUpscale||IsResize&&Method==_chain.HistoricalResizeMethod);
    public string BackendChangeLabel => "Use selected backend ("+_chain.SelectedRuntime+")";
    public bool NeedsResizeTechnologyChange => IsResize&&Runtime!=_chain.SelectedRuntime&&!CanUseSelectedBackend;
    public void UseSelectedBackend()
    {
        if(!CanUseSelectedBackend)return;
        _loading=true;
        try {Enabled=false;Runtime=_chain.SelectedRuntime;}
        finally {_loading=false;}
        Changed();
    }
    public void OpenEngineSetup() => _chain.ShowOptionalEngines();
    public string Header => $"{_chain.Stages.IndexOf(this)+1}. {Category}";
    public static string InferenceDirectory => Environment.GetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR")??Path.Combine(MainWindowViewModel.DataDir,"inference");
    private string Get(string key,string fallback="")=>_options.TryGetValue(key,out var v)?v:fallback;
    private void Option(string key,string value){if(Get(key)==value)return;_options[key]=value;Changed();}
    public bool Ensemble {get=>Get("ensemble","no")=="yes";set=>Option("ensemble",value?"yes":"no");}
    public string SceneThreshold {get=>Get("scene_detect_threshold","0.150");set=>Option("scene_detect_threshold",value);}
    public bool CanUseInstalledHdrSdk => IsNvidiaHdr&&MainWindowViewModel.OnLinux&&_chain.Vm?.OptionalSdks?.TrueHdrReady==true&&HdrSdkRoot!=_chain.Vm.OptionalSdks.TrueHdrRoot;
    public void UseInstalledHdrSdk() {if(CanUseInstalledHdrSdk)Option("sdk_root",_chain.Vm!.OptionalSdks.TrueHdrRoot);}
    public string HdrSdkRoot => Get("sdk_root",_chain.Vm?.OptionalSdks?.TrueHdrRoot??"/usr/local/VideoFX");
    public string HdrContrast {get=>Get("contrast","100");set=>Option("contrast",value);}
    public string HdrSaturation {get=>Get("saturation","100");set=>Option("saturation",value);}
    public string HdrMiddleGray {get=>Get("middle_gray","50");set=>Option("middle_gray",value);}
    public string HdrPeak {get=>Get("peak","1000");set=>Option("peak",value);}
    public bool MpvUnmatched => IsMpvHdr&&!MpvHdrLook;
    public bool HdrToneCurveControls => IsNvidiaHdr||MpvHdrLook;
    public bool MpvHdrLook {get=>Get("mpv_truehdr_look","yes")=="yes";set=>Option("mpv_truehdr_look",value?"yes":"no");}
    public string MpvPaperWhite {get=>Get("paper_white","203");set=>Option("paper_white",value);}
    public string MpvHighlights {get=>Get("highlights","50");set=>Option("highlights",value);}
    public string MpvVibrance {get=>Get("vibrance","15");set=>Option("vibrance",value);}
    private string HdrError {
        get {
            foreach(var value in new[]{HdrContrast,HdrSaturation})if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out int n)||n>200)return "HDR contrast and saturation must be integers from 0 to 200.";
            if(!int.TryParse(HdrMiddleGray,NumberStyles.None,CultureInfo.InvariantCulture,out int middle)||middle<10||middle>100)return "HDR middle grey must be an integer from 10 to 100.";
            if(IsNvidiaHdr&&(!int.TryParse(HdrPeak,NumberStyles.None,CultureInfo.InvariantCulture,out int peak)||peak<400||peak>2000))return "HDR target peak must be an integer from 400 to 2000 nits.";
            if(IsMpvHdr) {
                if(Get("mpv_truehdr_look","yes") is not ("yes" or "no"))return "mpv HDR tone curve must be yes or no.";
                foreach(var setting in new[]{(MpvPaperWhite,80,400),(MpvHighlights,0,100),(MpvVibrance,0,80)})
                    if(!int.TryParse(setting.Item1,NumberStyles.None,CultureInfo.InvariantCulture,out int value)||value<setting.Item2||value>setting.Item3)return "Invalid mpv HDR white/highlights/vibrance value.";
            }
            if(_chain.Stages.Skip(_chain.Stages.IndexOf(this)+1).Any(s=>s.Enabled))return "HDR must be the last active stage; the following engines currently require SDR.";
            return "";
        }
    }
    public void UseDownscaleOnlySizing()
    {
        if(!IsLegacySizing)return;
        _loading=true;
        try {Enabled=false;_options.Remove("legacy_geometry");}
        finally {_loading=false;}
        Changed();
    }
    public string TargetHeight {get=>Get("target_height","720");set=>Option("target_height",value);}
    public string TargetPercent {get=>Get("target_percent","50");set=>Option("target_percent",value);}
    public bool ResizeByPercent => _options.ContainsKey("target_percent");
    public string[] ResizeUnitChoices => ["Height (px)","Size (%)"];
    public string ResizeUnit {
        get=>ResizeByPercent?"Size (%)":"Height (px)";
        set {if(value==ResizeUnit||!ResizeUnitChoices.Contains(value))return;_options.Remove("target_height");_options.Remove("target_percent");_options[value=="Size (%)"?"target_percent":"target_height"]=value=="Size (%)"?"50":"720";Changed();}
    }
    public string ResizeValue {get=>ResizeByPercent?TargetPercent:TargetHeight;set {if(ResizeByPercent)TargetPercent=value;else TargetHeight=value;}}
    private string ResizeError => IsLegacySizing?"":ResizeByPercent
        ? (!int.TryParse(TargetPercent,NumberStyles.None,CultureInfo.InvariantCulture,out int percent)||percent<1||percent>100||_options.ContainsKey("target_height")?"Downscale percentage must be an integer from 1 to 100, without a target height.":"")
        : (!int.TryParse(TargetHeight,NumberStyles.None,CultureInfo.InvariantCulture,out int height)||height<32||height>8192?"Downscale height must be an integer from 32 to 8192.":"");
    public string Frames {get=>Get("frames","0");set=>Option("frames",value);}
    public string TemporalThreshold {get=>Get("temporal_scene_threshold","0.150");set=>Option("temporal_scene_threshold",value);}
    public bool TechnologyEditable => _options.Keys.All(k=>(IsRife?new[]{"ensemble","scene_detect_threshold"}:IsFruc?new[]{"sdk_library","sdk_runtime","kernel_fatbin","artifact_protection"}:IsUpscale?new[]{"frames","temporal_scene_threshold"}:IsResize?new[]{"target_height","target_percent","legacy_geometry"}:IsMpvHdr?new[]{"contrast","saturation","middle_gray","mpv_truehdr_look","paper_white","highlights","vibrance"}:IsNvidiaHdr?new[]{"sdk_root","contrast","saturation","middle_gray","peak"}:Array.Empty<string>()).Contains(k));
    public string OptionsText => string.Join("; ",_options.OrderBy(k=>k.Key).Select(k=>$"{k.Key}={k.Value}"));
    private sealed record StageReadiness(bool FilesPresent,string Reason,bool MissingFiles);
    private StageReadiness? _readiness;
    private StageReadiness Readiness => _readiness??=ResolveReadiness();
    private StageReadiness ResolveReadiness()
    {
        if(IsFruc)return new(false,"FRUC is no longer supported. Its imported configuration is preserved; disable or remove this step.",false);
        if(!_chain.TryPlan(out var plan,out var error))return new(false,error,false);
        if(IsHdr&&HdrError.Length>0)return new(false,HdrError,false);
        if(IsResize&&ResizeError.Length>0)return new(false,ResizeError,false);
        if(IsNvidiaHdr&&MainWindowViewModel.OnWindows&&_chain.Vm?.RtxHdrSelectable!=true)
            return new(false,"NVIDIA RTX Video is unavailable on this machine. Choose mpv HDR, or check the GPU and NVIDIA driver.",false);
        try
        {
            var candidate=plan with {Stages=plan.Stages.Select(s=>s.Id==Id?s with {Enabled=true}:s).ToArray()};
            InterpolationPipeline.Validate(candidate);
            var context=new ProfileResolver.Context(MainWindowViewModel.OnWindows?"windows":MainWindowViewModel.OnMac?"macos":"linux",
                _chain.SelectedRuntime,InferenceDirectory,_chain.Vm?.OnnxPath??Path.Combine(MainWindowViewModel.DataDir,"onnx"),
                Path.Combine(MainWindowViewModel.DataDir,"rife"),PipelineReaderFeatures.Read(InferenceDirectory),
                _chain.Vm?.AnimeJaNaiConf?.RifeEngine??"fast",_chain.Vm?.AnimeJaNaiConf?.AppleDevice??"AUTO");
            var resolved=ProfileResolver.Resolve(candidate,context);
            var stage=resolved.Stages.Single(s=>s.Id==Id);
            if(stage.Status!="files-present")return new(false,stage.Reason,stage.Requirements.Any(r=>!r.Present));
            // A global validation issue (for example cumulative cadence) cannot be
            // hidden by a locally available engine. Other stages keep their own diagnostics.
            var global=resolved.Issues.Where(issue=>!resolved.Stages.Any(s=>issue.StartsWith(s.Id+":",StringComparison.Ordinal))).ToArray();
            if(global.Length>0)return new(false,string.Join(" ",global),false);
            return new(true,stage.Reason,false);
        }
        catch(Exception e) when(e is InvalidDataException or FormatException or OverflowException)
        {return new(false,e.Message,false);}
    }
    public string Availability => Readiness.Reason;
    public string AvailabilityLabel => Readiness.FilesPresent?"Required files present":Availability;
    // Disabling an imported stage must remain possible even on another machine.
    public bool CanEnable => Enabled||Readiness.FilesPresent;
    public void MoveUp()=>_chain.MoveStage(this,-1);
    public void MoveDown()=>_chain.MoveStage(this,1);
    public void Remove()=>_chain.DeleteStage(this);
    private void Set(ref string field,string value,[CallerMemberName] string? name=null) {value??="";if(field==value)return;this.RaiseAndSetIfChanged(ref field,value,name);Changed();}
    private void Changed(){if(!_loading)_chain.ChangedPipeline();}
    internal void Refresh()
    {
        _readiness=null;
        foreach(var name in MethodChoices.Append(MethodChoice).Distinct())
            if(!_technologies.Any(t=>t.Name==name))_technologies.Add(new StageTechnology(name,true,""));
        foreach(var technology in _technologies) {
            bool available=true;string reason="";
            if(technology.Name=="NVIDIA FRUC (removed)") {available=false;reason="Imported FRUC is preserved but cannot be activated.";}
            else if(!MethodChoices.Contains(technology.Name)) {available=false;reason="Imported technology is not available in this Manager.";}
            if(technology.Name=="NVIDIA TrueHDR") {available=MainWindowViewModel.OnWindows?_chain.Vm?.RtxHdrSelectable==true:MainWindowViewModel.OnLinux&&(_chain.Vm?.OptionalSdks?.TrueHdrReady==true||IsNvidiaHdr&&Readiness.FilesPresent);reason=_chain.Vm?.OptionalSdks?.TrueHdrStatus??"Install the TrueHDR SDK.";}
            technology.Update(available,reason);
        }
        foreach(var name in new[]{nameof(Header),nameof(Summary),nameof(StateLabel),nameof(RuntimeLabel),nameof(HasAvailabilityAction),nameof(CanUseSelectedBackend),nameof(BackendChangeLabel),nameof(NeedsResizeTechnologyChange),nameof(Category),nameof(MethodChoice),nameof(TechnologyChoices),nameof(SelectedTechnology),nameof(ModelChoices),nameof(IsRife),nameof(IsFruc),nameof(IsUpscale),nameof(IsResize),nameof(IsHdr),nameof(IsNvidiaHdr),nameof(IsMpvHdr),nameof(MpvHdrLook),nameof(MpvUnmatched),nameof(HdrToneCurveControls),nameof(MpvPaperWhite),nameof(MpvHighlights),nameof(MpvVibrance),nameof(HdrContrast),nameof(HdrSaturation),nameof(HdrMiddleGray),nameof(HdrPeak),nameof(HasModel),nameof(IsLegacySizing),nameof(TargetHeight),nameof(TargetPercent),nameof(ResizeUnit),nameof(ResizeValue),nameof(IsInterpolation),nameof(ModeNotice),nameof(OrderDescription),nameof(CanMoveUp),nameof(CanMoveDown),nameof(Availability),nameof(AvailabilityLabel),nameof(CanEnable),nameof(Ensemble),nameof(SceneThreshold),nameof(CanUseInstalledHdrSdk),nameof(HdrSdkRoot),nameof(Frames),nameof(TemporalThreshold),nameof(OptionsText),nameof(TechnologyEditable)})this.RaisePropertyChanged(name);
    }
    internal InterpolationPipeline.Stage ToStage()
    {
        if(!long.TryParse(Numerator,NumberStyles.None,CultureInfo.InvariantCulture,out long n)||!long.TryParse(Denominator,NumberStyles.None,CultureInfo.InvariantCulture,out long d))throw new InvalidDataException("Stage factors require positive integers.");
        if(IsHdr&&Enabled&&HdrError.Length>0)throw new InvalidDataException(HdrError);
        if(IsResize&&Enabled&&ResizeError.Length>0)throw new InvalidDataException(ResizeError);
        var options=new Dictionary<string,string>(_options);
        if(IsNvidiaHdr&&Runtime=="cuda")options["sdk_root"]=HdrSdkRoot;
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
