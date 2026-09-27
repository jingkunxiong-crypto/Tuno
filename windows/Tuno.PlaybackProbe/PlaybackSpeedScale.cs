namespace Tuno.PlaybackProbe;

public static class PlaybackSpeedScale
{
    public const double Minimum=0.25;
    public const double Normal=1;
    public const double Maximum=3;
    public const double Midpoint=162;
    public const double MaxProgress=324;

    public static double Normalize(double speed)
        =>Math.Round(Math.Clamp(speed,Minimum,Maximum)*20,MidpointRounding.AwayFromZero)/20;

    public static double FromProgress(double progress)
    {
        progress=Math.Clamp(progress,0,MaxProgress);
        var speed=progress<=Midpoint
            ?Minimum+(Normal-Minimum)*progress/Midpoint
            :Normal+(Maximum-Normal)*(progress-Midpoint)/Midpoint;
        return Normalize(speed);
    }

    public static double ToProgress(double speed)
    {
        speed=Normalize(speed);
        return speed<=Normal
            ?(speed-Minimum)/(Normal-Minimum)*Midpoint
            :Midpoint+(speed-Normal)/(Maximum-Normal)*Midpoint;
    }
}
