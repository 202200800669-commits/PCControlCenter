using System.Runtime.CompilerServices;
namespace PCControlCenter.Core;

public sealed class MonitorSession {
 private readonly IHardwareProvider provider;
 private readonly DeviceIdentity identity;
 private readonly TimeSpan interval;
 private int active;
 public MonitorSession(IHardwareProvider provider,DeviceIdentity identity,TimeSpan interval) {
  if(interval<TimeSpan.FromSeconds(1)||interval>TimeSpan.FromSeconds(30))throw new ArgumentOutOfRangeException(nameof(interval));
  this.provider=provider;this.identity=identity;this.interval=interval;
 }
 public async IAsyncEnumerable<Snapshot> WatchAsync([EnumeratorCancellation]CancellationToken ct=default) {
  if(Interlocked.CompareExchange(ref active,1,0)!=0)throw new InvalidOperationException("MONITOR_ALREADY_RUNNING");
  try {
   while(true) {
    ct.ThrowIfCancellationRequested();
    var snapshot=await provider.ReadAsync(identity,ct);
    if(snapshot.Device!=identity)throw new InvalidOperationException("MONITOR_IDENTITY_CHANGED");
    yield return snapshot;
    // Delay starts after the consumer accepts this sample. Slow hardware or UI
    // never creates an overlapping read queue or an unbounded sample backlog.
    await Task.Delay(interval,ct);
   }
  } finally {Volatile.Write(ref active,0);}
 }
}
