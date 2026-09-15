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
  var response=await BrokerClient.ExecuteAsync(new(BrokerProtocol.Version,Guid.NewGuid().ToString("N"),"read-fans",device),ct);
  if(response.Code!="OK")throw new ProbeException(response.Code);
  if(response.Fan1 is null or <0 or >10000||response.Fan2 is null or <0 or >10000)throw new ProbeException("BROKER_INVALID_READING");
  return JsonSerializer.SerializeToElement(new{fan1=response.Fan1,fan2=response.Fan2});
 }
}
