using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PCControlCenter.Core;
public sealed record FeedbackSummary(int SchemaVersion,string ApplicationVersion,DeviceIdentity ReportedDevice,string CompatibilityKey,string Trust,IReadOnlyList<string> ReportedFeatures,SystemDetails? ReportedSystem);
public static class Feedback {
 private const int MaxJson=262144,MaxZip=4194304;
 public static FeedbackSummary Inspect(string file) {
  using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(input.Length>MaxZip)throw new InvalidDataException("ARCHIVE_TOO_LARGE");
  using var zip=new ZipArchive(input,ZipArchiveMode.Read);
  if(zip.Entries.Count!=1||zip.Entries[0].FullName!="diagnostics.json")throw new InvalidDataException("UNEXPECTED_ENTRY");
  var entry=zip.Entries[0];if(entry.Length>MaxJson)throw new InvalidDataException("REPORT_TOO_LARGE");
  using var content=entry.Open();using var data=new MemoryStream();var buffer=new byte[8192];
  int count;while((count=content.Read(buffer))!=0){if(data.Length+count>MaxJson)throw new InvalidDataException("REPORT_TOO_LARGE");data.Write(buffer,0,count);}
  using var document=JsonDocument.Parse(data.ToArray(),new JsonDocumentOptions{MaxDepth=12});
  var root=document.RootElement;
  if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("INVALID_ROOT");
  UniqueKeys(root);
  int version=root.GetProperty("schemaVersion").GetInt32();
  if(version is not (1 or 2))throw new InvalidDataException("UNSUPPORTED_SCHEMA");
  var device=root.GetProperty("device");if(device.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("INVALID_DEVICE");
  string Field(JsonElement value,string name) {
   var field=value.GetProperty(name);if(field.ValueKind!=JsonValueKind.String)throw new InvalidDataException("INVALID_FIELD");
   var text=field.GetString()??"";if(text.Length>160)throw new InvalidDataException("FIELD_TOO_LONG");return PublicText.Clean(text);
  }
  var identity=new DeviceIdentity(Field(device,"manufacturer"),Field(device,"product"),Field(device,"model"),Field(device,"bios"),Field(device,"platform"));
  if(identity.Manufacturer.Length==0||identity.Product.Length==0||identity.Bios.Length==0)throw new InvalidDataException("INCOMPLETE_IDENTITY");
  var features=root.GetProperty("capabilities");if(features.ValueKind!=JsonValueKind.Array||features.GetArrayLength()>64)throw new InvalidDataException("INVALID_CAPABILITIES");
  var labels=features.EnumerateArray().Select(f=>Field(f,"Feature")+" / "+Field(f,"Level")).ToArray();
  var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{identity.Manufacturer.Trim().ToUpperInvariant(),identity.Product.Trim(),identity.Model.Trim(),identity.Bios.Trim(),identity.Platform.Trim()}))));
  // This is a grouping key for a reported hardware/firmware combination, not a
  // unique physical computer identifier and never an authorization to control it.
  SystemDetails? system=null;
  if(version==2&&root.TryGetProperty("system",out var details)&&details.ValueKind!=JsonValueKind.Null) {
   if(details.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("INVALID_SYSTEM");
   var displays=details.GetProperty("Displays");
   if(displays.ValueKind!=JsonValueKind.Array||displays.GetArrayLength()>8)throw new InvalidDataException("INVALID_DISPLAYS");
   var drivers=displays.EnumerateArray().Select(d=>new DisplayDetails(Field(d,"Name"),Field(d,"DriverVersion"),d.TryGetProperty("Source",out _)?Field(d,"Source"):"Unknown")).ToArray();
   system=new(Field(details,"OsVersion"),Field(details,"OsBuild"),Field(details,"CpuName"),Field(details,"BoardMaker"),Field(details,"BoardProduct"),drivers);
  }
  return new(version,Field(root,"applicationVersion"),identity,key,"UserReportedUnverified",labels,system);
 }
 private static void UniqueKeys(JsonElement e) {
  if(e.ValueKind==JsonValueKind.Object) {
   var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in e.EnumerateObject()){if(!names.Add(p.Name))throw new InvalidDataException("DUPLICATE_KEY");UniqueKeys(p.Value);}
  }else if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())UniqueKeys(item);
 }
}
