namespace Geotrack.Application.Services;

public static class ReviewPaging
{
    public const int DefaultTake = 10;
    public const int MaxTake = 50;

    public static int NormalizeSkip(int skip) => Math.Max(0, skip);

    public static int NormalizeTake(int take) => Math.Clamp(take, 1, MaxTake);
}
