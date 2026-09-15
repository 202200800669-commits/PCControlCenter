using PCControlCenter.Core;
namespace PCControlCenter.Providers.Windows;

public class GenericProvider(IReadOnlyProbe probe) : IHardwareProvider {
 protected readonly IReadOnlyProbe Probe=probe;
 public virtual string Id=>"windows.generic";
 public virtual bool Matches(DeviceIdentity device)=>true;
 public virtual async Task<Snapshot> ReadAsync(DeviceIdentity device,CancellationToken ct) {
  var readings=new List<Reading>();var codes=new List<string>();
  try {
   var data=await Probe.QueryAsync(ProbeKind.Generic,ct);
   foreach(var (key,feature) in new[]{("memory",Feature.Memory),("battery",Feature.Battery),("brightness",Feature.Brightness)}) {
    double? value=null;
    if(data.TryGetProperty(key,out var item)&&item.ValueKind==System.Text.Json.JsonValueKind.Number&&item.TryGetDouble(out var v)&&double.IsFinite(v)&&v>=0&&v<=100)value=v;
    readings.Add(new(feature,key,value,"%",DateTimeOffset.UtcNow,value is null?"unavailable":"ok"));
   }
  } catch(OperationCanceledException){throw;} catch(Exception){codes.Add("GENERIC_PROBE_FAILED");}
  var caps=new List<Capability>{new(Feature.DeviceInfo,SupportLevel.ReadOnly,false,"")};
  caps.AddRange(readings.Where(r=>r.Value is not null).Select(r=>new Capability(r.Feature,SupportLevel.ReadOnly,false,r.Unit)));
  foreach(var f in Enum.GetValues<Feature>().Where(f=>caps.All(c=>c.Feature!=f)))caps.Add(new(f,SupportLevel.Unsupported,false,"",Reason:"No verified adapter"));
  return new(Id,device,caps,readings,codes);
 }
 public Task<ControlResult> ApplyAsync(DeviceIdentity device,ControlRequest request,CancellationToken ct)=>
  Task.FromResult(new ControlResult(ResultCode.Unsupported,"Hardware writes are not shipped in this milestone"));
}

public sealed class ThinkBookProvider(IReadOnlyProbe probe) : GenericProvider(probe) {
 public override string Id=>"lenovo.thinkbook.21r0";
 public override bool Matches(DeviceIdentity d)=>
  d.Platform=="Windows" && d.Manufacturer.Equals("LENOVO",StringComparison.OrdinalIgnoreCase) &&
  d.Product=="21R0" && d.Model=="ThinkBook 16p G6 IAX" && d.Bios=="R2CN57WW";
 public override async Task<Snapshot> ReadAsync(DeviceIdentity device,CancellationToken ct) {
  if(!Matches(device))throw new InvalidOperationException("IDENTITY_MISMATCH");
  var snapshot=await base.ReadAsync(device,ct);
  try {
   var data=await Probe.QueryAsync(ProbeKind.ThinkBookFans,ct);
   var readings=snapshot.Readings.ToList();
   foreach(var key in new[]{"fan1","fan2"}) {
    var v=data.GetProperty(key).GetInt32();if(v<0||v>10000)throw new IOException("INVALID_READING");
    readings.Add(new(Feature.FanRpm,key,v,"RPM",DateTimeOffset.UtcNow,"ok"));
   }
   return snapshot with {Readings=readings,Capabilities=snapshot.Capabilities.Select(c=>c.Feature==Feature.FanRpm?new Capability(Feature.FanRpm,SupportLevel.ReadOnly,false,"RPM"):c).ToArray()};
  } catch(OperationCanceledException){throw;}catch(ProbeException e) when(e.Code=="ACCESS_DENIED"){return snapshot with {IssueCodes=[..snapshot.IssueCodes,"THINKBOOK_FAN_ACCESS_DENIED"]};}catch(Exception){return snapshot with {IssueCodes=[..snapshot.IssueCodes,"THINKBOOK_FAN_READ_UNAVAILABLE"]};}
 }
}

// Brand discovery is not a claim of hardware control compatibility.
public sealed class BrandDiscoveryProvider(IReadOnlyProbe probe,string brand):GenericProvider(probe) {
 public override string Id=>brand+".discovery";
 public override bool Matches(DeviceIdentity d)=>brand switch {
  "asus"=>d.Manufacturer.Equals("ASUSTeK COMPUTER INC.",StringComparison.OrdinalIgnoreCase)||d.Manufacturer.Equals("ASUS",StringComparison.OrdinalIgnoreCase),
  "mechrevo"=>d.Manufacturer.Equals("MECHREVO",StringComparison.OrdinalIgnoreCase),
  _=>false
 };
}
public static class Providers {
 public static ProviderRegistry Create(IReadOnlyProbe probe)=>new([
  new ThinkBookProvider(probe),new BrandDiscoveryProvider(probe,"asus"),new BrandDiscoveryProvider(probe,"mechrevo")],new GenericProvider(probe));
}
