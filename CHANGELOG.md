# Changelog

## v0.1.0 (2026-07-10)

Initial release.

- Dashboard: device, OS, battery wear (design vs full-charge from powercfg XML report), storage pressure, disk health with wear/temperature, 7-day thermal snapshot
- Thermal Events tab: System log scan (7/30/90 days) for Kernel-Power 125, Kernel-Processor-Power 37, WHEA errors, and thermal-keyword warnings; CSV export
- Power tab: Turbo Boost cap via PROCTHROTTLEMAX (99%/100%), power-mode overlay switching (efficiency/balanced/performance), battery report generation, active plan display
- Software tab: winget search/install/update/upgrade-all plus one-click installs for HWiNFO64, CrystalDiskInfo, LibreHardwareMonitor, PowerToys
- Maintenance tab: temp cleanup with freed-space report, DISM ScanHealth/RestoreHealth, SFC, component store cleanup, DNS flush, Windows Update and Surface app links
- Catppuccin Mocha WPF theme, dark title bar, toast notifications, embedded streaming console, status bar
- Fully async: runspace pool workers + ConcurrentQueue + DispatcherTimer pump; the UI never blocks
- Self-elevating; `-Smoke` offscreen render test with screenshot capture
