using NAudio.Wave;

namespace AcousticLink.App.Audio;

public sealed record PlaybackProgress(float[] Samples, int SampleRate, int SampleCount, bool IsActive, string PositionSource);

public sealed class AudioPlaybackService
{
    private PlaybackProgress? progress;
    public PlaybackProgress? Progress => Volatile.Read(ref progress);
    public Task PlayAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        // Inicialización y espera fuera del hilo WPF; cada llamada posee sus recursos.
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new byte[checked(samples.Length * sizeof(float))];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            using var stream = new RawSourceWaveStream(bytes, 0, bytes.Length,
                WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));
            await using var player = new WasapiPlayerBuilder().WithSharedMode().Build();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<StoppedEventArgs> stopped = (_, e) =>
            {
                if (e.Exception is not null)
                    completion.TrySetException(e.Exception);
                else
                    completion.TrySetResult();
            };
            player.PlaybackStopped += stopped;
            int published = 0;
            string positionSource = "reloj de salida WASAPI";
            void Publish(bool active)
            {
                try
                {
                    long bytesPlayed = player.GetPosition();
                    int frames = (int)Math.Clamp(bytesPlayed * (double)sampleRate / player.OutputWaveFormat.AverageBytesPerSecond, 0, samples.Length);
                    published = Math.Max(published, frames);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Solo observabilidad: el fallo del reloj no debe interrumpir el audio.
                    // Alternativa explícita: muestras leídas por la ruta, con posible adelanto de buffering.
                    positionSource = "muestras entregadas a WASAPI (puede adelantar por buffering)";
                    published = Math.Max(published, (int)Math.Min(samples.Length, stream.Position / sizeof(float)));
                }
                Volatile.Write(ref progress, new(samples, sampleRate, published, active, positionSource));
            }
            try
            {
                player.Init(stream);
                cancellationToken.ThrowIfCancellationRequested();
                player.Play();
                Publish(true);
                while (!completion.Task.IsCompleted)
                {
                    await Task.WhenAny(completion.Task, Task.Delay(40, cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested(); Publish(true);
                }
                await completion.Task;
            }
            finally
            {
                Publish(false);
                player.PlaybackStopped -= stopped;
                // DisposeAsync detiene y libera el dispositivo incluso al cancelar.
            }
        }, cancellationToken);
    }
}
