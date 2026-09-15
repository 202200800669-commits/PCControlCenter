using System.Text.Json;
namespace PCControlCenter.Core;

public sealed record Preferences(int SchemaVersion=1,bool MinimizeToTray=true,int PollIntervalSeconds=3);
public static class PreferenceStore {
 public static Preferences Load(string path) {
  var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))??throw new InvalidDataException("Empty configuration");
  Validate(p);return p;
 }
 private static void Validate(Preferences p) {
  if(p.SchemaVersion!=1)throw new InvalidDataException("Unsupported configuration version");
  if(p.PollIntervalSeconds is <1 or >30)throw new InvalidDataException("Invalid poll interval");
 }
 public static void Create(string destination,Preferences preferences) {
  Validate(preferences);
  var full=Path.GetFullPath(destination);var parent=Path.GetDirectoryName(full)!;
  // Callers select the data folder; never silently overwrite existing settings.
  Directory.CreateDirectory(parent);
  var temporary=Path.Combine(parent,".preferences-"+Guid.NewGuid()+".tmp");
  try {
   using(var f=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
    JsonSerializer.Serialize(f,preferences,Diagnostics.Json);f.Flush(true);
   }
   File.Move(temporary,full,false);
  } finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
 public static Preferences ImportLegacy(string source,string destination) {
  using var json=JsonDocument.Parse(File.ReadAllText(source));
  var root=json.RootElement;
  if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid legacy configuration");
  bool tray=true;int interval=3;
  if(root.TryGetProperty("Tray",out var t)) {
   if(t.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Invalid tray preference");
   tray=t.GetBoolean();
  }
  if(root.TryGetProperty("Interval",out var i)&&!i.TryGetInt32(out interval))throw new InvalidDataException("Invalid interval");
  var p=new Preferences(1,tray,interval);
  // Explicit allowlist excludes Profiles, fan targets, power limits and visual assets.
  Create(destination,p);return p;
 }
}
