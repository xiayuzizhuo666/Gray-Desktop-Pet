# Installer validation: v1.1.0

Final MSI SHA256: `92681c95334ba3717bbe085a7cefa8dfed3ec2909c6da458cd520456e3cd65b2`.

Executed on 2026-10-02 at 18:28 UTC on Windows build 26100. The test process used a non-elevated token. There was no previous Gray installation in the target user directories or product registration.

| Check | Result |
| --- | --- |
| `msiexec /i ... /qn /norestart` | Exit 0 |
| Windows Installer product registration | State 5 (installed), version 1.1.0, AssignmentType 0 (per-user) |
| Installed payload | All seven files matched the build inputs by SHA256 |
| Start menu shortcut | Resolved to the installed GrayPet.exe |
| Installed program `--smoke-test` | Started actual window/timer/tray and exited cleanly |
| `msiexec /fa ... /qn /norestart` | Repair exit 0 |
| `msiexec /x ... /qn /norestart` | Uninstall exit 0 |
| Cleanup | App and Start menu folders, app registry values and generated smoke log removed; no GrayPet processes |
| Windows Installer state after removal | -1 (unknown product) |

The package has no ALLUSERS property, rejects a nonempty ALLUSERS override, and has summary word-count flags 10 (compressed payload and no elevated privileges required). It contains standard MSI actions with no executable custom actions. It does not set startup entries or download prerequisites.

Full installer GUI navigation, Settings UI navigation, upgrades from an older MSI, enterprise policy combinations and Windows SDK ICE validation were not tested. The MSI is unsigned. Command-line registration/removal tests are not represented as physical UI tests.

The build uses existing Microsoft-signed system makecab and Windows Installer automation. No additional packaging software was installed or downloaded, and no execution policy or security setting was changed.

References: [Microsoft installation context](https://learn.microsoft.com/en-us/windows/win32/msi/installation-context), [packages without elevation](https://learn.microsoft.com/en-us/windows/win32/msi/authoring-packages-without-the-uac-dialog-box), [summary flags](https://learn.microsoft.com/en-us/windows/win32/msi/word-count-summary).
