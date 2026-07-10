# Changelog

## v0.2.0 (2026-07-10)

- Added a self-contained .NET 9 WPF/MVVM application while retaining the portable PowerShell edition.
- Rebuilt the product shell around a compact navigation rail, page command header, health-summary band, responsive diagnostic cards, persistent activity dock, status bar, and native custom window chrome.
- Added complete Overview, Thermal, Power, Software, Maintenance, Settings, and About views with clearer hierarchy, microcopy, empty/loading/disabled/success/error states, and keyboard-visible focus treatment.
- Added matching dark and light semantic themes with a constrained 0/4/6/8/10/12 px radius system, one blue interaction accent, and reserved health colors.
- Added a typed asynchronous Windows adapter for CIM inventory, battery reporting, storage and disk health, event-log scanning, powercfg, winget, DISM, SFC, component cleanup, DNS reset, and report exports.
- Added dependency-free tests for winget fixed-width parsing and battery, storage, disk, thermal, and power health thresholds, plus optional read-only live adapter checks.
- Added persistent theme/activity preferences, session logs, crash reports, toast feedback, self-elevation, and `--uia-background` / `--smoke` non-activating launch modes.
- Added multi-view dark/light background captures, a branded application icon, a solution-level build, and self-contained single-file publishing.
- Suppressed PowerShell CLIXML leakage in read-only adapters and treat unavailable Windows power-mode overlays as a supported fallback instead of an application error.

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
