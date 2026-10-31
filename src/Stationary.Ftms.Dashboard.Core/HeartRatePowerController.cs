namespace Stationary.Ftms.Dashboard.Core;

public static class HeartRatePowerController
{
    public const int MaximumAdjustmentWatts = 10;

    public static int GetNextTargetWatts(
        int currentWatts,
        int minimumWatts,
        int maximumWatts,
        int powerIncrementWatts,
        ushort currentHeartRate,
        ushort targetMinimumHeartRate,
        ushort targetMaximumHeartRate,
        ushort? safetyCeilingHeartRate = null)
    {
        if (minimumWatts < 0
            || maximumWatts < minimumWatts
            || powerIncrementWatts <= 0
            || targetMaximumHeartRate < targetMinimumHeartRate)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWatts));
        }

        currentWatts = int.Clamp(currentWatts, minimumWatts, maximumWatts);
        var direction = currentHeartRate > targetMaximumHeartRate
            ? -1
            : currentHeartRate < targetMinimumHeartRate
                ? 1
                : 0;
        if (safetyCeilingHeartRate is ushort ceiling && currentHeartRate >= ceiling)
        {
            direction = -1;
        }

        if (direction == 0)
        {
            return currentWatts;
        }

        var difference = direction < 0
            ? currentHeartRate - (safetyCeilingHeartRate is ushort cap && currentHeartRate >= cap ? cap : targetMaximumHeartRate)
            : targetMinimumHeartRate - currentHeartRate;
        var requestedAdjustment = int.Clamp((difference + 1) * 2, powerIncrementWatts, MaximumAdjustmentWatts);
        var roundedAdjustment = int.Max(powerIncrementWatts, requestedAdjustment / powerIncrementWatts * powerIncrementWatts);
        return int.Clamp(currentWatts + (direction * roundedAdjustment), minimumWatts, maximumWatts);
    }
}