using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCControlCenter.Core;
namespace PCControlCenter.Ipc;

public sealed record BrokerRequest(int Version,string RequestId,string Operation,DeviceIdentity Device);
public sealed record BrokerResponse(int Version,string RequestId,string Code,int? Fan1=null,int? Fan2=null);
public static class BrokerProtocol {
 public const int Version=1,MaxFrame=32768;
 public static JsonSerializerOptions Json {get;}=new(){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
 public static bool Valid(BrokerRequest q)=>q.Version==Version&&Guid.TryParseExact(q.RequestId,"N",out _)&&q.Operation=="read-fans"&&q.Device is not null;
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
