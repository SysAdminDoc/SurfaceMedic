namespace SurfaceMedic.Core.Models;

public static class HealthAssessor
{
    public static HealthAssessment Unavailable(string headline, string? detail = null) =>
        new(HealthState.Unavailable, headline, detail ?? "Windows did not expose enough data to assess this item.");

    public static HealthAssessment AssessBattery(
        bool isPresent,
        double? designCapacityMwh,
        double? fullChargeCapacityMwh)
    {
        if (!isPresent)
        {
            return Unavailable("No battery detected", "This device may be running without a battery or its firmware may not publish battery data.");
        }

        if (designCapacityMwh is null or <= 0 || fullChargeCapacityMwh is null or < 0)
        {
            return Unavailable("Battery capacity unavailable");
        }

        var wear = Math.Round(Math.Max(0, (1 - (fullChargeCapacityMwh.Value / designCapacityMwh.Value)) * 100), 1);
        return wear switch
        {
            >= 35 => new(HealthState.Critical, "Battery replacement recommended", $"Capacity has declined by {wear:F1}% from its design rating."),
            >= 20 => new(HealthState.Warning, "Battery is noticeably worn", $"Capacity has declined by {wear:F1}%; plan for replacement if runtime is limiting."),
            >= 10 => new(HealthState.Advisory, "Battery wear is moderate", $"Capacity has declined by {wear:F1}%, which is still serviceable."),
            _ => new(HealthState.Healthy, "Battery health is good", $"Measured wear is {wear:F1}%.")
        };
    }

    public static HealthAssessment AssessStorage(double? totalGb, double? freeGb)
    {
        if (totalGb is null or <= 0 || freeGb is null or < 0)
        {
            return Unavailable("Storage capacity unavailable");
        }

        var usedPercent = Math.Clamp((1 - (freeGb.Value / totalGb.Value)) * 100, 0, 100);
        return usedPercent switch
        {
            >= 95 => new(HealthState.Critical, "System drive is critically full", $"Only {freeGb:F1} GB is free; Windows needs working space for updates and maintenance."),
            >= 90 => new(HealthState.Warning, "System drive is nearly full", $"Only {freeGb:F1} GB is free; reclaim space to reduce update and performance problems."),
            >= 80 => new(HealthState.Advisory, "System drive space is getting low", $"{freeGb:F1} GB remains available."),
            _ => new(HealthState.Healthy, "Storage capacity is healthy", $"{freeGb:F1} GB remains available.")
        };
    }

    public static HealthAssessment AssessDisk(
        string? reportedHealth,
        double? wearPercent,
        double? temperatureCelsius)
    {
        if (!string.IsNullOrWhiteSpace(reportedHealth) &&
            !reportedHealth.Equals("Healthy", StringComparison.OrdinalIgnoreCase) &&
            !reportedHealth.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return new(HealthState.Critical, "Drive reports a health problem", $"Windows reports this drive as {reportedHealth}.");
        }

        if (temperatureCelsius >= 70 || wearPercent >= 90)
        {
            return new(HealthState.Critical, "Drive needs immediate attention", DescribeDiskMetrics(wearPercent, temperatureCelsius));
        }

        if (temperatureCelsius >= 60 || wearPercent >= 75)
        {
            return new(HealthState.Warning, "Drive health margin is low", DescribeDiskMetrics(wearPercent, temperatureCelsius));
        }

        if (temperatureCelsius >= 50 || wearPercent >= 50)
        {
            return new(HealthState.Advisory, "Monitor drive conditions", DescribeDiskMetrics(wearPercent, temperatureCelsius));
        }

        if (string.IsNullOrWhiteSpace(reportedHealth) && wearPercent is null && temperatureCelsius is null)
        {
            return Unavailable("Drive telemetry unavailable");
        }

        return new(HealthState.Healthy, "Drive health is good", DescribeDiskMetrics(wearPercent, temperatureCelsius));
    }

    public static HealthAssessment AssessThermal(
        int throttleEngagements,
        int firmwareSpeedCaps,
        int hardwareErrors)
    {
        if (hardwareErrors > 0)
        {
            return new(HealthState.Critical, "Hardware errors were recorded", $"Windows logged {hardwareErrors} hardware error(s) in the assessment window.");
        }

        if (throttleEngagements > 0)
        {
            return new(HealthState.Warning, "Thermal throttling was detected", $"Windows logged {throttleEngagements} thermal throttle engagement(s).");
        }

        if (firmwareSpeedCaps > 0)
        {
            return new(HealthState.Advisory, "Firmware limited processor speed", $"Windows logged {firmwareSpeedCaps} firmware speed cap event(s).");
        }

        return new(HealthState.Healthy, "No recent throttling detected", "Windows reported no thermal throttle, firmware cap, or hardware error events.");
    }

    public static HealthAssessment AssessPower(int? acMaximumPercent, int? dcMaximumPercent)
    {
        if (acMaximumPercent is null || dcMaximumPercent is null)
        {
            return Unavailable("Processor power limits unavailable");
        }

        if (acMaximumPercent <= 99 && dcMaximumPercent <= 99)
        {
            return new(HealthState.Healthy, "Cool and quiet mode is active", "Turbo Boost is capped on both AC and battery power.");
        }

        if (acMaximumPercent <= 99 || dcMaximumPercent <= 99)
        {
            return new(HealthState.Advisory, "Power limits differ", "Turbo Boost is capped on only one power source.");
        }

        return new(HealthState.Advisory, "Maximum performance mode is active", "Turbo Boost is available; expect higher peak temperature and fan activity.");
    }

    private static string DescribeDiskMetrics(double? wearPercent, double? temperatureCelsius)
    {
        var metrics = new List<string>();
        if (wearPercent is not null)
        {
            metrics.Add($"reported wear {wearPercent:F0}%");
        }

        if (temperatureCelsius is not null)
        {
            metrics.Add($"temperature {temperatureCelsius:F1} C");
        }

        return metrics.Count == 0 ? "Windows reports the drive as healthy." : string.Join(", ", metrics) + ".";
    }
}
