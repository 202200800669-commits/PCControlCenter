using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.RegularExpressions;
using PCControlCenter.Ipc;
using PCControlCenter.Providers.Windows;
if(!OperatingSystem.IsWindows())return 2;
if(args.Length!=2||!Regex.IsMatch(args[0],"^pc-control-[0-9a-f]{32}$")||!int.TryParse(args[1],out int parent)||parent<=0)return 2;
if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 3;
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
try {
 using var parentProcess=Process.GetProcessById(parent);
 var expected=Path.Combine(AppContext.BaseDirectory,"pc-control.exe");
 if(!string.Equals(parentProcess.MainModule?.FileName,expected,StringComparison.OrdinalIgnoreCase))return 4;
 using var pipe=new NamedPipeClientStream(".",args[0],PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
 await pipe.ConnectAsync(deadline.Token);
 if(!LocalPipe.ServerIs(pipe,parent))return 4;
 var request=await BrokerProtocol.ReceiveAsync<BrokerRequest>(pipe,deadline.Token);
 if(!BrokerProtocol.Valid(request))return 5;
 BrokerResponse response;
 try {
  var probe=new WindowsProbe();var actual=await WindowsProbe.IdentifyAsync(probe,deadline.Token);
  if(actual!=request.Device||!new ThinkBookProvider(probe).Matches(actual))response=new(1,request.RequestId,"IDENTITY_MISMATCH");
  else {
   var reading=await probe.QueryAsync(ProbeKind.ThinkBookFans,deadline.Token);
   response=new(1,request.RequestId,"OK",reading.GetProperty("fan1").GetInt32(),reading.GetProperty("fan2").GetInt32());
  }
 }catch(ProbeException e){response=new(1,request.RequestId,e.Code=="ACCESS_DENIED"?"ACCESS_DENIED":"PROBE_FAILED");}
 catch(Exception){response=new(1,request.RequestId,"BROKER_READ_FAILED");}
 await BrokerProtocol.SendAsync(pipe,response,deadline.Token);
 return 0;
}catch(Exception){return 6;}
