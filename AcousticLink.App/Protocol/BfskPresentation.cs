using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

// Adaptador de presentación para las vistas existentes; B no representa una medición.
public static class BfskPresentation
{
    public static FdmTextReceiveResult AsSingleChannel(BfskTextReceiveResult result)
    {
        var sync = result.Synchronization;
        var empty = new BfskSequenceResult(Array.Empty<BfskSymbolResult>());
        var a = sync.Header is null ? null : new FdmSynchronizedChannel(sync.Found, sync.Header, empty);
        var displaySync = new FdmSynchronizationResult(sync.Found ? FdmSynchronizationState.Complete : FdmSynchronizationState.Failed,
            sync.StartSeconds, sync.DataStartSeconds, a, null, sync.Reason)
        { IsAmbiguous = result.Channel.State == FdmTextReceiveState.AmbiguousSynchronization };
        var absent = new FdmTextChannelResult(FdmTextReceiveState.HeaderNotSynchronized, false, null, null,
            null, null, "Canal B no utilizado en BFSK.");
        return new(displaySync, result.Channel, absent);
    }
}
