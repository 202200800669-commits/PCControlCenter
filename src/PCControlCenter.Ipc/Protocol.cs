using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCControlCenter.Core;
namespace PCControlCenter.Ipc;

public sealed record BrokerRequest(int Version,string RequestId,string Operation,DeviceIdentity Device,FanTrial? Trial=null);
public sealed record BrokerResponse(int Version,string RequestId,string Code,int? Fan1=null,int? Fan2=null,FanReceipt? Receipt=null);
public static class BrokerProtocol {
 public const int Version=2,MaxFrame=32768;
 public static JsonSerializerOptions Json {get;}=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,AllowDuplicateProperties=false,RespectRequiredConstructorParameters=true,RespectNullableAnnotations=true,MaxDepth=8};
 private static bool ValidIdentity(DeviceIdentity? d)=>d is not null&&new[]{d.Manufacturer,d.Product,d.Model,d.Bios,d.Platform}.All(s=>!string.IsNullOrWhiteSpace(s)&&s.Length<=160&&!s.Any(char.IsControl));
 public static bool Valid(BrokerRequest q)=>q.Version==Version&&Guid.TryParseExact(q.RequestId,"N",out _)&&ValidIdentity(q.Device) && (q.Operation=="read-fans"&&q.Trial is null || q.Operation=="fan-trial"&&q.Trial is {IsValid:true});
 public static async Task SendAsync<T>(Stream stream,T value,CancellationToken ct) {
  var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
  if(bytes.Length is <1 or >MaxFrame)throw new InvalidDataException("FRAME_SIZE");
  var size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,bytes.Length);
  await stream.WriteAsync(size,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
 }
 public static async Task<T> ReceiveAsync<T>(Stream stream,CancellationToken ct) {
  var size=new byte[4];await stream.ReadExactlyAsync(size,ct);var length=BinaryPrimitives.ReadInt32LittleEndian(size);
  if(length is <1 or >MaxFrame)throw new InvalidDataException("FRAME_SIZE");
  var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,ct);
  return JsonSerializer.Deserialize<T>(bytes,Json)??throw new InvalidDataException("NULL_MESSAGE");
 }
}
