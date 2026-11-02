namespace AcousticLink.App.Signals;

public enum FdmAmplitudeProfile { Original, PrioritizeChannelA }

public sealed record FdmAmplitudeSetting(FdmAmplitudeProfile Profile, string Name, double ChannelA, double ChannelB)
{
    public string Label => $"{Name} · A={ChannelA:0.###} · B={ChannelB:0.###}";
    public override string ToString() => Label;
}

public static class FdmAmplitudeProfiles
{
    public const double MaximumAmplitude = 0.15;
    public static IReadOnlyList<FdmAmplitudeSetting> All { get; } = Array.AsReadOnly(new[]
    {
        new FdmAmplitudeSetting(FdmAmplitudeProfile.Original, "Original", 0.075, 0.075),
        new FdmAmplitudeSetting(FdmAmplitudeProfile.PrioritizeChannelA, "Priorizar canal A", 0.10, 0.05)
    });

    public static FdmAmplitudeSetting Get(FdmAmplitudeProfile profile) => profile switch
    {
        FdmAmplitudeProfile.Original => All[0],
        FdmAmplitudeProfile.PrioritizeChannelA => All[1],
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    internal static (double A, double B) GetGains(FdmAmplitudeProfile profile, double amplitude)
    {
        if (!double.IsFinite(amplitude) || amplitude < 0 || amplitude > MaximumAmplitude)
            throw new ArgumentOutOfRangeException(nameof(amplitude), "La amplitud conjunta debe estar entre 0 y 0,15.");
        var setting = Get(profile);
        if (amplitude == MaximumAmplitude) return (setting.ChannelA, setting.ChannelB);
        // Se escala antes de modular; no se normaliza la mezcla. En Original el factor es 0,5 exacto.
        return (amplitude * (setting.ChannelA / MaximumAmplitude), amplitude * (setting.ChannelB / MaximumAmplitude));
    }
}
