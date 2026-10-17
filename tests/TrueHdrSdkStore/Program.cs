using AnimeJaNaiConfEditor.Services;
using System.Diagnostics;
using System.Text.Json;
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);string mode=args[1];string receipt=Path.Combine(output,mode+".json");if(File.Exists(receipt))throw new Exception("Conclusive receipt exists; no replay.");
if(mode.StartsWith("guards")) { Guards.Run(output,mode=="guards_resume");return; }
var store=new TrueHdrSdkStore(Path.Combine(output,"data"));var before=store.Read();var timer=Stopwatch.StartNew();var state=store.Import(Path.GetFullPath(args[2]));timer.Stop();
if(mode=="core") {if(!state.Core||state.Feature||state.Root!=null)throw new Exception("Core alone must remain incomplete.");}
else if(mode=="feature") {if(!state.Core||!state.Feature||state.Root==null)throw new Exception("Both components must form a complete SDK.");}
else throw new Exception("Unknown case");
File.WriteAllText(receipt,JsonSerializer.Serialize(new{passed=true,mode,import_ms=timer.Elapsed.TotalMilliseconds,state,scope=mode=="core"?"Actual official Core TGZ, runtime/ELF/internal aliases/licenses and incomplete-state guard":"Actual installed feature files in a controlled archive; not a claim about the unavailable original NGC feature archive"},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(File.ReadAllText(receipt));
