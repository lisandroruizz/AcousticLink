using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

// Misma trama por canal que FDM, sin relleno ni señal del canal B.
public sealed class BfskTextModem
{
    private readonly BfskToneGenerator generator = new();
    private readonly FdmTextFrameBuilder frames = new();

    public FdmTextChannelFrame Build(string text, bool useHamming = false) =>
        frames.BuildChannel(text, "BFSK", useHamming);

    public float[] Generate(string text, bool useHamming = false)
    {
        var frame = Build(text, useHamming);
        return generator.GenerateSequence(frame.FrameBits, BfskToneGenerator.Bit0Frequency,
            BfskToneGenerator.Bit1Frequency, BfskToneGenerator.DefaultBitDurationSeconds,
            BfskToneGenerator.DefaultAmplitude, FdmTextFrameBuilder.MaximumProtectedFrameBits);
    }
}

public sealed record BfskTextReceiveResult(BfskSynchronizationResult Synchronization, FdmTextChannelResult Channel)
{
    public bool IsValid => Channel.State == FdmTextReceiveState.Valid;
}

public sealed class BfskTextReceiver
{
    private readonly BfskFrameSynchronizer synchronizer = new();
    private readonly BfskSequenceDecoder decoder = new();
    private readonly FdmTextReceiver interpreter = new();

    public BfskTextReceiveResult Receive(ReadOnlySpan<float> samples, int sampleRate, CancellationToken token = default,
        bool useHamming = false, bool recoverErasures = false)
    {
        var sync = synchronizer.FindHeaders(samples, sampleRate, token);
        return ReceiveSynchronized(samples, sampleRate, sync, token, useHamming, recoverErasures);
    }

    internal BfskTextReceiveResult ReceiveSynchronized(ReadOnlySpan<float> samples, int sampleRate,
        BfskSynchronizationResult sync, CancellationToken token, bool useHamming, bool recoverErasures)
    {
        if (sync.DataStartSeconds is not double start)
        {
            var state = sync.Reason.Contains("ambigua", StringComparison.OrdinalIgnoreCase)
                ? FdmTextReceiveState.AmbiguousSynchronization
                : samples.Length < Math.Round((BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length)
                    * BfskSequenceDecoder.BitDurationSeconds * sampleRate)
                    ? FdmTextReceiveState.Truncated : FdmTextReceiveState.HeaderNotSynchronized;
            return new(sync, new(state, false, null, null, null, null, sync.Reason)
            { UsesHamming = useHamming, ErasureRecoveryEnabled = useHamming && recoverErasures });
        }
        int available = (int)Math.Floor((samples.Length - start * sampleRate + 0.000001)
            / (BfskSequenceDecoder.BitDurationSeconds * sampleRate));
        int maximum = useHamming ? FdmTextFrameBuilder.MaximumProtectedFrameBits : FdmTextFrameBuilder.MaximumFrameBits;
        maximum -= BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length;
        int count = Math.Clamp(available, 0, maximum);
        if (count == 0)
            return new(sync, new(FdmTextReceiveState.Truncated, true, null, null, null, null,
                "Cabecera validada; faltan los símbolos de longitud y contenido.")
            { UsesHamming = useHamming, ErasureRecoveryEnabled = useHamming && recoverErasures });
        var block = decoder.DecodeMessageBlock(samples, sampleRate, start, count, token);
        var interpreted = interpreter.InterpretPrefix(block, true, useHamming, recoverErasures);
        return new(sync, interpreted);
    }
}
