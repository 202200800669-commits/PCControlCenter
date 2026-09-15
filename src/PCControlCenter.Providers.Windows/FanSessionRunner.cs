using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PCControlCenter.Core;
namespace PCControlCenter.Providers.Windows;
public static class FanSessionRunner {
 public static async Task<FanReceipt> RunAsync(FanTrial trial,CancellationToken stop) {
  if(!OperatingSystem.IsWindows()||!trial.IsValid)return new("INVALID_REQUEST","NOT_NEEDED");
  using var resource=typeof(FanSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.FanSession.ps1")??throw new IOException("RESOURCE_MISSING");
  using var reader=new StreamReader(resource);var script=await reader.ReadToEndAsync();
  // Mode is an exact enum-like allowlist and numeric fields are integers.
  var prefix=string.Create(CultureInfo.InvariantCulture,$"$mode='{trial.Mode}';$rpm1={trial.Rpm1};$rpm2={trial.Rpm2};$seconds={trial.Seconds};");
  var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");
  var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
  foreach(var arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(prefix+script))})start.ArgumentList.Add(arg);
  using var process=new Process{StartInfo=start};
  stop.ThrowIfCancellationRequested();
  if(!process.Start())return new("WORKER_START_FAILED","NOT_NEEDED");
  // EOF is the recovery signal. Do not kill the worker on cancellation: its finally
  // block owns restoring automatic control, even if the broker itself exits.
  using var registration=stop.Register(()=>{try{process.StandardInput.Close();}catch(Exception){}});
  var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
  var exited=process.WaitForExitAsync();
  if(await Task.WhenAny(exited,Task.Delay(TimeSpan.FromSeconds(80)))!=exited) {
   process.StandardInput.Close();return new("WORKER_TIMEOUT","UNCONFIRMED");
  }
  var json=await output;await errors;
  if(process.ExitCode!=0||json.Length>32768)return new("WORKER_FAILED","UNCONFIRMED");
  try{return JsonSerializer.Deserialize<FanReceipt>(json)??new("INVALID_RECEIPT","UNCONFIRMED");}
  catch(JsonException){return new("INVALID_RECEIPT","UNCONFIRMED");}
 }
}
