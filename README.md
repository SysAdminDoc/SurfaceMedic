# SurfaceMedic

![Version](https://img.shields.io/badge/version-0.1.0-cba6f7)
![License](https://img.shields.io/badge/license-MIT-a6e3a1)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-89b4fa)
![PowerShell](https://img.shields.io/badge/PowerShell-5.1%2B-f9e2af)

Tune-up and thermal toolkit for heavily-used Surface (and other Windows) devices. Single portable PowerShell script, dark WPF GUI, fully async — nothing blocks the UI, and every action streams its output to the embedded console.

![Dashboard](screenshots/app.png)

## Why

Heavily-used Surfaces throttle. Dried thermal interface, dusty vents, worn batteries, and years of accumulated Windows cruft all show up as `Kernel-Power 125` (thermal throttle engaged) and `Kernel-Processor-Power 37` (firmware speed cap) events. SurfaceMedic finds those events, quantifies the damage (battery wear, SSD health, disk pressure), and applies the highest-ROI software fixes in one place.

## Features

- **Dashboard** — device/CPU/RAM, OS build and uptime, battery design-vs-full-charge capacity with wear verdict, storage pressure, per-disk health (wear/temperature where the firmware exposes it), and a 7-day throttle-event snapshot.
- **Thermal Events** — scans the System event log (7/30/90 days) for thermal throttle engagements, firmware speed caps, WHEA hardware errors, and thermal-keyword warnings. Color-coded grid, CSV export.
- **Power** — one-click Turbo Boost cap (`PROCTHROTTLEMAX` 99%, the single biggest win on a thermally-limited device), Windows power-mode overlay switching, full battery report generation, active plan display.
- **Software** — full winget front-end: search, install selected, check updates, upgrade selected or all. One-click installs for HWiNFO64, CrystalDiskInfo, LibreHardwareMonitor, and PowerToys.
- **Maintenance** — temp cleanup with freed-space report, DISM ScanHealth/RestoreHealth, SFC, WinSxS component cleanup, DNS flush, and quick links to Windows Update (Surface firmware channel) and the Surface app.

![Power tab](screenshots/power.png)

## Usage

```powershell
powershell -ExecutionPolicy Bypass -File SurfaceMedic.ps1
```

The script self-elevates via UAC (power tweaks and repair tools need admin). Works on Windows PowerShell 5.1 and PowerShell 7+. No modules, no dependencies — copy the one file to the target machine and run it.

### Requirements

- Windows 10/11
- winget ("App Installer" from the Microsoft Store) for the Software tab — everything else works without it

### Testing

`-Smoke` renders the UI offscreen without activation, captures screenshots to `screenshots\`, and exits — it never shows a window or steals focus:

```powershell
powershell -ExecutionPolicy Bypass -File SurfaceMedic.ps1 -Smoke
```

![Maintenance tab](screenshots/maintenance.png)

## Notes

- The Turbo cap is applied to both AC and DC of the active power plan and takes effect immediately; "Restore 100%" reverts it.
- Undervolting is intentionally absent — Surface firmware locks CPU voltage (post-Plundervolt), so ThrottleStop/XTU are dead ends on these devices.
- Surface thermal-table and fan-curve fixes ship as firmware through Windows Update — check optional updates on a machine that throttles.

## License

MIT
