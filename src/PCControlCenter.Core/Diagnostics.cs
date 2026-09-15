using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PCControlCenter.Core;
public static class Diagnostics {
 public static JsonSerializerOptions Json {get;}=new() {
  WriteIndented=true, Converters={new JsonStringEnumConverter()}
 };
 // Only explicitly selected fields enter the report. Never serialize process output,
 // exception messages, registry dumps, serial numbers or user paths.
 public static object Report(Snapshot s)=>new {
  schemaVersion=2, applicationVersion="0.1.0-alpha.5", capturedAt=DateTimeOffset.UtcNow,
  provider=s.Provider,
  device=new {manufacturer=s.Device.Manufacturer,product=s.Device.Product,model=s.Device.Model,bios=s.Device.Bios,platform=s.Device.Platform},
  system=s.System,
  capabilities=s.Capabilities.Select(c=>new {c.Feature,c.Level,c.CanWrite,c.Unit,c.Min,c.Max}),
  readings=s.Readings.Select(r=>new {r.Feature,r.Channel,r.Value,r.Unit,r.At,r.Status}),
  issueCodes=s.IssueCodes.Where(c=>Regex.IsMatch(c,"^[A-Z][A-Z0-9_]{0,63}$")),
  privacy="No serial number, user name, machine name, full paths or network identifiers are intentionally collected. Review model/firmware strings before sharing."
 };
 public static string ToJson(Snapshot snapshot)=>JsonSerializer.Serialize(Report(snapshot),Json);
 public static void Export(Snapshot snapshot,string destination) {
  var json=ToJson(snapshot);
  using var file=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
  using var zip=new ZipArchive(file,ZipArchiveMode.Create);
  using var writer=new StreamWriter(zip.CreateEntry("diagnostics.json").Open());
  writer.Write(json);
 }
}
