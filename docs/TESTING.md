# Validation scope

Application version: 1.1.0.0. MSI/product/release version: 1.1.0; release tag: v1.1.0.

The application passed 1,525 assertions on Windows build 26100 with .NET Framework 4.8, one connected 1920x1200 work area and 96 DPI. Native PerMonitorV2 awareness was active. See `test-results.txt` for the test run timestamp and exact coverage.

Checks exercise actual application HWNDs, layered/topmost flags, WPF rendering and timers, in-process menu/tray commands, animation timing, click decisions, native movement, pause/resume, singleton behavior and cleanup. A regression first reproduced active dragging being clamped by a layout callback; it now waits until drag release. Negative monitor coordinates and multiple monitors were checked with synthetic geometry only.

Physical mouse delivery, double-click timing/hit testing, transparent-pixel click-through, physical tray input, shortcut keyboard input and desktop visual occlusion were not verified. Mixed-DPI monitor crossing, live Windows scaling changes, monitor unplugging and Explorer restart were not tested. The supported desktop input tool was not available; no substitute input-injection helper was used.

The installer is authored as a per-user Windows Installer MSI. It embeds a CAB, installs only application files, creates a per-user Start menu shortcut, and uses standard Windows Installer registration/removal. It has no custom executable actions, services, startup entry, network download or machine-wide registry payload. Actual installation/uninstallation results are recorded separately in `INSTALLER-TESTS.md` when available. MSI GUI mouse navigation and SDK ICE validation are not implied by command-line tests.

Run application tests only after exiting an existing Gray instance. Diagnostic files are generated in the program directory. The source build needs a writable directory and the Windows .NET Framework compiler; the installer build also needs the system Windows Installer automation interface and makecab.
