using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland;

/// <summary>Затухающая пружина — даёт «живое» расширение острова с лёгким перелётом.</summary>
public sealed class SpringEase : EasingFunctionBase
{
    public SpringEase() => EasingMode = EasingMode.EaseIn;

    public double Damping { get; set; } = 8;
    public double Frequency { get; set; } = 1.3;

    protected override double EaseInCore(double t) =>
        1 - Math.Exp(-Damping * t) * Math.Cos(2 * Math.PI * Frequency * t);

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
