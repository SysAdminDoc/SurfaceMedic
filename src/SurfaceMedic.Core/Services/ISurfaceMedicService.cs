using SurfaceMedic.Core.Models;

namespace SurfaceMedic.Core.Services;

public interface ISurfaceMedicService
{
    Task<DashboardSnapshot> GetDashboardAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<PowerStatus> GetPowerStatusAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThermalEventRecord>> ScanThermalEventsAsync(
        int days,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageRecord>> SearchPackagesAsync(
        string query,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageRecord>> GetPackageUpdatesAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> InstallPackagesAsync(
        IEnumerable<string> packageIds,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> UpgradePackagesAsync(
        IEnumerable<string> packageIds,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> UpgradeAllPackagesAsync(
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<PowerStatus> SetCpuMaximumAsync(
        int percent,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<PowerStatus> SetPowerOverlayAsync(
        PowerOverlay overlay,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateBatteryReportAsync(
        string? destinationPath = null,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RunMaintenanceAsync(
        MaintenanceOperation operation,
        OperationCallbacks? callbacks = null,
        CancellationToken cancellationToken = default);
}
