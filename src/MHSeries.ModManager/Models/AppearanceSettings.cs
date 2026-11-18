namespace MhModManager.Models;

public sealed record AppearanceSettings
{
    public string Theme { get; init; } = "blue";
    public string Fit { get; init; } = "cover";
    public int Opacity { get; init; } = 30;
    public int Blur { get; init; }
    public int PositionX { get; init; } = 50;
    public int PositionY { get; init; } = 50;

    public AppearanceSettings Normalize() => this with
    {
        Theme = Theme is "blue" or "jade" or "copper" or "graphite" ? Theme : "blue",
        Fit = Fit is "cover" or "contain" or "stretch" ? Fit : "cover",
        Opacity = Math.Clamp(Opacity, 0, 70),
        Blur = Math.Clamp(Blur, 0, 20),
        PositionX = Math.Clamp(PositionX, 0, 100),
        PositionY = Math.Clamp(PositionY, 0, 100)
    };
}
