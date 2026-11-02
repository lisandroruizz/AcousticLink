using NAudio.Wave;

namespace AcousticLink.App.Audio;

public sealed class AudioCaptureService : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? stop;
    private Task session = Task.CompletedTask;
    private CapturedAudio? recording;
    private string format = "Sin captura";
    private string? error;
    private volatile bool running;
    private bool disposed;

    public bool IsRecording => running;
    public CapturedAudio? Recording => Volatile.Read(ref recording);
    public string FormatDescription => Volatile.Read(ref format);
    public string? Error => Volatile.Read(ref error);

    public async Task StartAsync(int maximumDurationSeconds = CapturedAudio.DefaultMaximumSeconds)
    {
        CapturedAudio.ValidateDuration(maximumDurationSeconds);
        await lifecycle.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!session.IsCompleted) throw new InvalidOperationException("Ya hay una captura activa.");
            stop?.Dispose();
            stop = new CancellationTokenSource();
            error = null;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session = Task.Run(() => CaptureAsync(ready, stop.Token, maximumDurationSeconds));
            await ready.Task;
        }
        finally { lifecycle.Release(); }
    }

    private async Task CaptureAsync(TaskCompletionSource ready, CancellationToken token, int maximumDurationSeconds)
    {
        try
        {
            WasapiRecorder recorder;
            try
            {
                recorder = new WasapiRecorderBuilder().WithSharedMode()
                    .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1)).Build();
                try { await RecordAsync(recorder, ready, token, maximumDurationSeconds); }
                finally { await recorder.DisposeAsync(); }
                return;
            }
            catch when (!ready.Task.IsCompleted)
            {
                // Segundo intento con el formato nativo, sin asumir 48 kHz ni mono.
                recorder = new WasapiRecorderBuilder().WithSharedMode().Build();
            }

            await using (recorder)
            {
                await RecordAsync(recorder, ready, token, maximumDurationSeconds);
            }
        }
        catch (OperationCanceledException) { ready.TrySetCanceled(); }
        catch (Exception ex)
        {
            error = ex.Message;
            Recording?.Complete(CaptureStopReason.Error);
            ready.TrySetException(ex);
        }
        finally { running = false; }
    }

    private async Task RecordAsync(WasapiRecorder recorder, TaskCompletionSource ready, CancellationToken token, int maximumDurationSeconds)
    {
        var waveFormat = recorder.WaveFormat;
        var captured = new CapturedAudio(waveFormat.SampleRate, waveFormat.Channels, maximumDurationSeconds);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) finished.TrySetException(e.Exception);
            else finished.TrySetResult();
        };
        recorder.DataAvailable += (buffer, flags, devicePosition, qpcPosition) =>
        {
            try
            {
                byte[] bytes = buffer.ToArray();
                if ((flags & NAudio.CoreAudioApi.AudioClientBufferFlags.Silent) != 0) Array.Clear(bytes);
                using var stream = new RawSourceWaveStream(bytes, 0, bytes.Length, waveFormat);
                var source = stream.ToSampleProvider();
                var values = new float[(bytes.Length / waveFormat.BlockAlign) * waveFormat.Channels];
                int read = source.Read(values.AsSpan());
                captured.Append(values, read);
                if (captured.GetStatus().Count >= captured.SampleCapacity) finished.TrySetResult();
            }
            catch (Exception ex) { finished.TrySetException(ex); }
        };
        recorder.StartRecording();
        Volatile.Write(ref recording, captured);
        format = $"Entrada: {waveFormat.SampleRate} Hz, {waveFormat.Channels} canal(es), {waveFormat.Encoding}, {waveFormat.BitsPerSample} bits. Datos/WAV: float32 mono a {waveFormat.SampleRate} Hz.";
        running = true;
        ready.TrySetResult();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(maximumDurationSeconds));
        try
        {
            await finished.Task.WaitAsync(timeout.Token);
            captured.Complete(CaptureStopReason.DeviceStopped);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // El límite de reloj también evita esperar indefinidamente si dejan de llegar paquetes.
            // Se conservan solo las muestras reales: no se inventan muestras para completar el tiempo.
            captured.Complete(CaptureStopReason.DurationLimit);
        }
        catch
        {
            captured.Complete(token.IsCancellationRequested ? CaptureStopReason.ApplicationClosing : CaptureStopReason.Error);
            throw;
        }
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (!session.IsCompleted) Recording?.Complete(CaptureStopReason.Manual);
            stop?.Cancel();
            await session;
        }
        finally { lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            if (!session.IsCompleted) Recording?.Complete(CaptureStopReason.ApplicationClosing);
            stop?.Cancel();
            await session;
            stop?.Dispose();
            stop = null;
        }
        finally { lifecycle.Release(); }
    }
}
