using NAudio.Wave;

namespace AcousticLink.App.Audio;

public sealed class AudioPlaybackService
{
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
            try
            {
                player.Init(stream);
                cancellationToken.ThrowIfCancellationRequested();
                player.Play();
                await completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                player.PlaybackStopped -= stopped;
                // DisposeAsync detiene y libera el dispositivo incluso al cancelar.
            }
        }, cancellationToken);
    }
}
