using PCControlCenter.Ipc;
using System.Buffers.Binary;
using System.IO.Pipes;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;
using System.Text.Json;
using System.IO.Compression;

var device=new DeviceIdentity("LENOVO","21R0","ThinkBook 16p G6 IAX","R2CN57WW","Windows");
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
var probe=new FakeProbe();var registry=Providers.Create(probe);
Check(registry.Resolve(device).Id=="lenovo.thinkbook.21r0","exact reference identity");
foreach(var changed in new[]{device with {Bios="other"},device with {Manufacturer="ASUS"},device with {Product="21R1"},device with {Model="ThinkBook 16p"},device with {Platform="Linux"}})
 Check(registry.Resolve(changed).Id!="lenovo.thinkbook.21r0","changed identity excludes vendor probe");
Check(registry.Resolve(device with {Manufacturer="ASUSTeK COMPUTER INC."}).Id=="asus.discovery","ASUS discovery only");
Check(registry.Resolve(device with {Manufacturer="MECHREVO"}).Id=="mechrevo.discovery","MECHREVO discovery only");
Check(registry.Resolve(device with {Manufacturer="Unknown"}).Id=="windows.generic","unknown fallback");
var ambiguous=new ProviderRegistry([new ThinkBookProvider(probe),new ThinkBookProvider(probe)],new GenericProvider(probe));
Check(ambiguous.Resolve(device).Id=="windows.generic","ambiguous matching fails closed");
var realProvider=registry.Resolve(device);var snap=await realProvider.ReadAsync(device,default);
Check(snap.Readings.Count(r=>r.Feature==Feature.FanRpm)==2,"fan readings mapped");
Check(snap.Capabilities.All(c=>!c.CanWrite),"milestone advertises no hardware writes");
Check((await new Controller(realProvider,device).ApplyAsync(new(Feature.FanControl,3000))).Code==ResultCode.Unsupported,"real provider rejects writes");
probe.FailFans=true;var degraded=await realProvider.ReadAsync(device,default);
Check(degraded.IssueCodes.Contains("THINKBOOK_FAN_READ_UNAVAILABLE")&&degraded.Readings.All(r=>r.Feature!=Feature.FanRpm),"failed fan read has no fabricated values");
probe.FailFans=false;probe.BadFans=true;degraded=await realProvider.ReadAsync(device,default);
Check(degraded.Readings.All(r=>r.Feature!=Feature.FanRpm),"invalid fan reading discarded");
probe.BadFans=false;probe.Denied=true;
degraded=await realProvider.ReadAsync(device,default);
Check(degraded.IssueCodes.Contains("THINKBOOK_FAN_ACCESS_DENIED"),"access denial has dedicated diagnostic code");
probe.Denied=false;probe.BadGeneric=true;
degraded=await new GenericProvider(probe).ReadAsync(device,default);
Check(degraded.Readings.All(r=>r.Value is null),"invalid generic percentages are unavailable");
var fake=new FakeWriter(device);var controller=new Controller(fake,device);
Check((await controller.ApplyAsync(new(Feature.FanControl,double.NaN))).Code==ResultCode.InvalidRequest&&fake.Writes==0,"NaN rejected before write");
Check((await controller.ApplyAsync(new(Feature.FanControl,1499))).Code==ResultCode.InvalidRequest&&fake.Writes==0,"out of range rejected");
fake.Max=double.NaN;
Check((await controller.ApplyAsync(new(Feature.FanControl,3000))).Code==ResultCode.InvalidRequest&&fake.Writes==0,"invalid capability bounds rejected");
fake.Max=5500;
fake.Level=SupportLevel.Experimental;
Check((await controller.ApplyAsync(new(Feature.FanControl,3000))).Code==ResultCode.Unsupported&&fake.Writes==0,"experimental writes locked");
fake.Level=SupportLevel.Verified;fake.ChangeDevice=true;
Check((await controller.ApplyAsync(new(Feature.FanControl,3000))).Code==ResultCode.Unavailable&&fake.Writes==0,"identity change rejected");
fake.ChangeDevice=false;
await Task.WhenAll(Enumerable.Range(0,6).Select(_=>controller.ApplyAsync(new(Feature.FanControl,3000))));
Check(fake.Writes==6&&fake.MaxConcurrent==1,"concurrent commands serialized");
using(var cts=new CancellationTokenSource()) {cts.Cancel();Check((await controller.ApplyAsync(new(Feature.FanControl,3000),cts.Token)).Code==ResultCode.Cancelled&&fake.Writes==6,"cancelled queue never writes");}
fake.CancelDuringWrite=true;
Check((await controller.ApplyAsync(new(Feature.FanControl,3000))).Code==ResultCode.UnknownOutcome,"interrupted write is not falsely reported cancelled safely");
var privateSnapshot=snap with {IssueCodes=["OK_CODE",@"C:\Users\PrivateName\secret"]};
var json=Diagnostics.ToJson(privateSnapshot);
Check(!json.Contains("PrivateName")&&json.Contains("OK_CODE"),"diagnostics excludes raw error paths");
var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".zip");
try {
 Diagnostics.Export(privateSnapshot,file);
 using(var zip=ZipFile.OpenRead(file)){Check(zip.Entries.Count==1&&zip.Entries[0].FullName=="diagnostics.json","minimal diagnostic archive");}
 var old=File.ReadAllBytes(file);bool rejected=false;try{Diagnostics.Export(privateSnapshot,file);}catch(IOException){rejected=true;}
 Check(rejected&&old.SequenceEqual(File.ReadAllBytes(file)),"existing diagnostics not overwritten");
} finally{File.Delete(file);}
var configDir=Path.Combine(Path.GetTempPath(),"pc-control-test-"+Guid.NewGuid());Directory.CreateDirectory(configDir);
try {
 var source=Path.Combine(configDir,"old.json");var target=Path.Combine(configDir,"new.json");
 var original="""{"Tray":false,"Interval":5,"Profiles":[{"Rpm1":5500,"Mode":3}],"Accent":"private"}""";
 File.WriteAllText(source,original);PreferenceStore.ImportLegacy(source,target);
 var pref=PreferenceStore.Load(target);
 Check(pref==new Preferences(1,false,5),"legacy non-hardware preferences imported");
 Check(!File.ReadAllText(target).Contains("Rpm")&&!File.ReadAllText(target).Contains("Accent")&&File.ReadAllText(source)==original,"migration preserves source and excludes hardware and design");
 var oldSettings=File.ReadAllText(target);bool rejected=false;try{PreferenceStore.ImportLegacy(source,target);}catch(IOException){rejected=true;}
 Check(rejected&&File.ReadAllText(target)==oldSettings,"migration does not overwrite existing configuration");
 rejected=false;try{PreferenceStore.Create(Path.Combine(configDir,"invalid.json"),new Preferences(2));}catch(InvalidDataException){rejected=true;}
 Check(rejected&&!File.Exists(Path.Combine(configDir,"invalid.json")),"unknown schema rejected before saving");
 rejected=false;try{PreferenceStore.Create(Path.Combine(configDir,"range.json"),new Preferences(1,true,0));}catch(InvalidDataException){rejected=true;}
 Check(rejected,"invalid polling interval rejected");
 Check(!Directory.EnumerateFiles(configDir,"*.tmp").Any(),"failed migration leaves no temporary file");
} finally{foreach(var f in Directory.GetFiles(configDir))File.Delete(f);Directory.Delete(configDir);}
var validRequest=new BrokerRequest(1,Guid.NewGuid().ToString("N"),"read-fans",device);
Check(BrokerProtocol.Valid(validRequest),"broker accepts read-only versioned request");
Check(!BrokerProtocol.Valid(validRequest with {Operation="fan-manual"}),"broker protocol rejects write operations");
Check(!BrokerProtocol.Valid(validRequest with {Version=2})&&!BrokerProtocol.Valid(validRequest with {RequestId="bad"}),"broker rejects version and request identifier mismatch");
using(var frame=new MemoryStream()) {
 await BrokerProtocol.SendAsync(frame,validRequest,default);frame.Position=0;
 Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(frame,default)==validRequest,"framed protocol round trip");
}
foreach(var length in new[]{-1,0,32769}) {
 var bytes=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(bytes,length);bool rejected=false;
 try{await BrokerProtocol.ReceiveAsync<BrokerRequest>(new MemoryStream(bytes),default);}catch(InvalidDataException){rejected=true;}
 Check(rejected,"invalid frame length rejected before allocation");
}
using(var frame=new MemoryStream()) {
 var bytes=System.Text.Encoding.UTF8.GetBytes("""{"Version":1,"RequestId":"id","Operation":"read-fans","Device":null,"Script":"bad"}""");
 var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);frame.Write(header);frame.Write(bytes);frame.Position=0;
 bool rejected=false;try{await BrokerProtocol.ReceiveAsync<BrokerRequest>(frame,default);}catch(JsonException){rejected=true;}
 Check(rejected,"arbitrary extra request fields rejected");
}
using(var frame=new MemoryStream(new byte[]{12,0,0,0,1})) {
 bool rejected=false;try{await BrokerProtocol.ReceiveAsync<BrokerRequest>(frame,default);}catch(EndOfStreamException){rejected=true;}
 Check(rejected,"truncated frame fails closed");
}
if(OperatingSystem.IsWindows()) {
 var name="pc-control-"+Guid.NewGuid().ToString("N");using var server=LocalPipe.Create(name);
 using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
 using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
 await Task.WhenAll(server.WaitForConnectionAsync(deadline.Token),client.ConnectAsync(deadline.Token));
 Check(LocalPipe.ClientIs(server,Environment.ProcessId)&&LocalPipe.ServerIs(client,Environment.ProcessId),"OS peer process identifiers verified");
 Check(!LocalPipe.ClientIs(server,Environment.ProcessId+1)&&!LocalPipe.ServerIs(client,Environment.ProcessId+1),"wrong peer process identifiers rejected");
 await BrokerProtocol.SendAsync(server,validRequest,deadline.Token);
 Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(client,deadline.Token)==validRequest,"real local pipe request round trip");
 using var cts=new CancellationTokenSource(20);bool cancelled=false;
 try{await BrokerProtocol.ReceiveAsync<BrokerResponse>(client,cts.Token);}catch(OperationCanceledException){cancelled=true;}
 Check(cancelled,"stalled pipe read respects cancellation");
}
Console.WriteLine($"{passed} tests passed");

sealed class FakeProbe:IReadOnlyProbe {
 public bool FailFans, BadFans, Denied, BadGeneric;
 public Task<JsonElement> QueryAsync(ProbeKind kind,CancellationToken ct){
  ct.ThrowIfCancellationRequested();
  if(kind==ProbeKind.ThinkBookFans&&Denied)throw new ProbeException("ACCESS_DENIED");
  if(kind==ProbeKind.Generic&&BadGeneric)return Task.FromResult(JsonDocument.Parse("""{"memory":101,"battery":-1,"brightness":null}""").RootElement.Clone());
  if(kind==ProbeKind.ThinkBookFans&&FailFans)throw new IOException("secret path");
  using var d=JsonDocument.Parse(kind==ProbeKind.ThinkBookFans?(BadFans?"{\"fan1\":-1,\"fan2\":4500}":"{\"fan1\":3500,\"fan2\":4500}"):"{\"memory\":40,\"battery\":90,\"brightness\":75}");
  return Task.FromResult(d.RootElement.Clone());
 }
}
sealed class FakeWriter(DeviceIdentity identity):IHardwareProvider {
 public string Id=>"test";public bool Matches(DeviceIdentity d)=>true;
 public double Max=5500;public int Writes,MaxConcurrent;private int concurrent;
 public bool ChangeDevice,CancelDuringWrite;public SupportLevel Level=SupportLevel.Verified;
 public Task<Snapshot> ReadAsync(DeviceIdentity d,CancellationToken ct)=>Task.FromResult(new Snapshot(Id,ChangeDevice?identity with {Bios="new"}:identity,[new(Feature.FanControl,Level,true,"RPM",1500,Max)],[],[]));
 public async Task<ControlResult> ApplyAsync(DeviceIdentity d,ControlRequest r,CancellationToken ct){
  Writes++;var n=Interlocked.Increment(ref concurrent);MaxConcurrent=Math.Max(MaxConcurrent,n);
  try{await Task.Delay(10,ct);if(CancelDuringWrite)throw new OperationCanceledException();return new(ResultCode.Success,"read back confirmed");}
  finally{Interlocked.Decrement(ref concurrent);}
 }
}
