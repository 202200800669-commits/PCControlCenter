namespace PCControlCenter.Core;

public sealed class Controller(IHardwareProvider provider, DeviceIdentity device)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<ControlResult> ApplyAsync(ControlRequest request, CancellationToken ct = default)
    {
        if (!double.IsFinite(request.Value))
            return new(ResultCode.InvalidRequest, "Non-finite value");
        var entered = false;
        var applying = false;
        try
        {
            await gate.WaitAsync(ct);
            entered = true;
            var snapshot = await provider.ReadAsync(device, ct);
            if (snapshot.Device != device || snapshot.Provider != provider.Id)
                return new(ResultCode.Unavailable, "Device identity changed");
            var capability = snapshot.Capabilities.SingleOrDefault(c => c.Feature == request.Feature);
            if (capability is null || !capability.CanWrite || capability.Level != SupportLevel.Verified)
                return new(ResultCode.Unsupported, "Write capability not verified");
            // Numeric writes require explicit bounds. Modes will use separate typed requests.
            if (capability.Min is null || capability.Max is null || !double.IsFinite(capability.Min.Value) || !double.IsFinite(capability.Max.Value) || capability.Min > capability.Max ||
               request.Value < capability.Min || request.Value > capability.Max)
                return new(ResultCode.InvalidRequest, "Outside validated range");
            ct.ThrowIfCancellationRequested();
            applying = true;
            return await provider.ApplyAsync(device, request, ct);
        }
        catch (OperationCanceledException)
        {
            return new(applying ? ResultCode.UnknownOutcome : ResultCode.Cancelled,
             applying ? "Operation interrupted; read back before retry" : "Cancelled before write");
        }
        catch (Exception)
        {
            return new(applying ? ResultCode.UnknownOutcome : ResultCode.Unavailable,
             applying ? "Write outcome unknown" : "Provider unavailable");
        }
        finally { if (entered) gate.Release(); }
    }
}
