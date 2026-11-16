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
    private long receivedSamples, lastBufferTimestamp;
    private int actualSampleRate;
    public long ReceivedSamples => Interlocked.Read(ref receivedSamples);
    public long LastBufferTimestamp => Interlocked.Read(ref lastBufferTimestamp);
    public int SampleRate => Volatile.Read(ref actualSampleRate);
    public bool SessionCompleted => session.IsCompleted;

    public bool IsRecording => running;
    public CapturedAudio? Recording => Volatile.Read(ref recording);
    public string FormatDescription => Volatile.Read(ref format);
    public string? Error => Volatile.Read(ref error);

    public async Task StartAsync(int maximumDurationSeconds = CapturedAudio.DefaultMaximumSeconds,
        CancellationToken cancellationToken = default)
    {
        CapturedAudio.ValidateDuration(maximumDurationSeconds);
        await StartCoreAsync(maximumDurationSeconds, null, cancellationToken);
    }

    public Task StartContinuousAsync(Action<float[], int> publish, CancellationToken cancellationToken = default) =>
        StartCoreAsync(null, publish ?? throw new ArgumentNullException(nameof(publish)), cancellationToken);

    private async Task StartCoreAsync(int? maximumDurationSeconds, Action<float[], int>? publish, CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.IsCompleted) throw new InvalidOperationException("Ya hay una captura activa.");
            stop?.Dispose();
            stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            error = null;
            Interlocked.Exchange(ref receivedSamples, 0); Interlocked.Exchange(ref lastBufferTimestamp, 0);
            Volatile.Write(ref actualSampleRate, 0);
            if (!maximumDurationSeconds.HasValue) Volatile.Write(ref recording, null);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session = Task.Run(() => CaptureAsync(ready, stop.Token, maximumDurationSeconds, publish));
            await ready.Task;
        }
        finally { lifecycle.Release(); }
    }

    private async Task CaptureAsync(TaskCompletionSource ready, CancellationToken token, int? maximumDurationSeconds, Action<float[], int>? publish)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            WasapiRecorder recorder;
            try
            {
                recorder = new WasapiRecorderBuilder().WithSharedMode()
                    .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1)).Build();
                try { await RecordAsync(recorder, ready, token, maximumDurationSeconds, publish); }
                finally { await recorder.DisposeAsync(); }
                return;
            }
            catch when (!ready.Task.IsCompleted && !token.IsCancellationRequested)
            {
                // Segundo intento con el formato nativo, sin asumir 48 kHz ni mono.
                recorder = new WasapiRecorderBuilder().WithSharedMode().Build();
            }

            await using (recorder)
            {
                await RecordAsync(recorder, ready, token, maximumDurationSeconds, publish);
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

    private async Task RecordAsync(WasapiRecorder recorder, TaskCompletionSource ready, CancellationToken token, int? maximumDurationSeconds, Action<float[], int>? publish)
    {
        var waveFormat = recorder.WaveFormat;
        Volatile.Write(ref actualSampleRate, waveFormat.SampleRate);
        var captured = maximumDurationSeconds.HasValue ? new CapturedAudio(waveFormat.SampleRate, waveFormat.Channels, maximumDurationSeconds.Value) : null;
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
                Interlocked.Add(ref receivedSamples, read / waveFormat.Channels);
                Interlocked.Exchange(ref lastBufferTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
                if (captured is not null)
                {
                    captured.Append(values, read);
                    if (captured.GetStatus().Count >= captured.SampleCapacity) finished.TrySetResult();
                }
                else
                {
                    var mono = new float[read / waveFormat.Channels];
                    for (int i = 0; i < mono.Length; i++)
                    {
                        double sum = 0;
                        for (int c = 0; c < waveFormat.Channels; c++) sum += values[i * waveFormat.Channels + c];
                        mono[i] = (float)(sum / waveFormat.Channels);
                    }
                    publish!(mono, waveFormat.SampleRate);
                }
            }
            catch (Exception ex) { finished.TrySetException(ex); }
        };
        recorder.StartRecording();
        Volatile.Write(ref recording, captured);
        format = $"Entrada: {waveFormat.SampleRate} Hz, {waveFormat.Channels} canal(es), {waveFormat.Encoding}, {waveFormat.BitsPerSample} bits. Datos/WAV: float32 mono a {waveFormat.SampleRate} Hz.";
        running = true;
        ready.TrySetResult();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (maximumDurationSeconds.HasValue) timeout.CancelAfter(TimeSpan.FromSeconds(maximumDurationSeconds.Value));
        try
        {
            await finished.Task.WaitAsync(timeout.Token);
            captured?.Complete(CaptureStopReason.DeviceStopped);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // El límite de reloj también evita esperar indefinidamente si dejan de llegar paquetes.
            // Se conservan solo las muestras reales: no se inventan muestras para completar el tiempo.
            captured?.Complete(CaptureStopReason.DurationLimit);
        }
        catch
        {
            captured?.Complete(token.IsCancellationRequested ? CaptureStopReason.ApplicationClosing : CaptureStopReason.Error);
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
