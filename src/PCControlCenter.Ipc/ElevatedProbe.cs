using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;
namespace PCControlCenter.Ipc;

[SupportedOSPlatform("windows")]
public sealed class ElevatedProbe(IReadOnlyProbe normal,DeviceIdentity device):IReadOnlyProbe {
 public Task<JsonElement> QueryAsync(ProbeKind kind,CancellationToken ct)=>kind==ProbeKind.ThinkBookFans?ReadFansAsync(ct):normal.QueryAsync(kind,ct);
 private async Task<JsonElement> ReadFansAsync(CancellationToken ct) {
  var path=Path.Combine(AppContext.BaseDirectory,"pc-control-broker.exe");
  if(!File.Exists(path))throw new ProbeException("BROKER_NOT_INSTALLED");
  var name="pc-control-"+Guid.NewGuid().ToString("N");
  using var pipe=LocalPipe.Create(name);
  var start=new ProcessStartInfo(path){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory};
  start.ArgumentList.Add(name);start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
  Process? helper;
  ct.ThrowIfCancellationRequested();
  try{helper=Process.Start(start);}catch(Win32Exception e) when(e.NativeErrorCode==1223){throw new ProbeException("ELEVATION_CANCELLED");}
  using var owned=helper??throw new ProbeException("BROKER_START_FAILED");
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(50));
  // The helper is a one-shot process with its own deadline; closing the pipe ends its work.
  var connected=pipe.WaitForConnectionAsync(timeout.Token);
  var exited=owned.WaitForExitAsync(timeout.Token);
  await Task.WhenAny(connected,exited);
  if(!connected.IsCompletedSuccessfully) {
   timeout.Cancel();
   try{await connected;}catch(OperationCanceledException){}
   try{await exited;}catch(OperationCanceledException){}
   throw new ProbeException("BROKER_CONNECT_FAILED");
  }
  await connected;
  if(!LocalPipe.ClientIs(pipe,owned.Id))throw new ProbeException("BROKER_PEER_MISMATCH");
  var request=new BrokerRequest(1,Guid.NewGuid().ToString("N"),"read-fans",device);
  await BrokerProtocol.SendAsync(pipe,request,timeout.Token);
  var response=await BrokerProtocol.ReceiveAsync<BrokerResponse>(pipe,timeout.Token);
  if(response.Version!=1||response.RequestId!=request.RequestId)throw new ProbeException("BROKER_PROTOCOL_MISMATCH");
  if(response.Code!="OK")throw new ProbeException(response.Code);
  if(response.Fan1 is null or <0 or >10000||response.Fan2 is null or <0 or >10000)throw new ProbeException("BROKER_INVALID_READING");
  return JsonSerializer.SerializeToElement(new{fan1=response.Fan1,fan2=response.Fan2});
 }
}
