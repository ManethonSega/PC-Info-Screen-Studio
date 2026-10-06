# Alpha validation

This document describes what the release workflow verifies and what still needs a person using a physical screen. The exact successful workflow and source SHA are recorded in the portable package's `build-info.json`.

Candidate validation passed on **5 October 2026** in [Windows run 37367471392](https://github.com/ManethonSega/PC-Info-Screen-Studio/actions/runs/37367471392), including the extracted portable ZIP. The release workflow repeats these checks for its tagged source.

UI-polish candidate checks passed on **6 October 2026** in [Windows run 37420665087](https://github.com/ManethonSega/PC-Info-Screen-Studio/actions/runs/37420665087), with zero compiler warnings or errors. The alpha.6 release workflow repeats these checks for its tagged source.

## Automated Windows checks

| Workflow | Evidence provided by the checks |
| --- | --- |
| Empty states and property panels | Actual WPF Photo Frame and Hybrid prompts appear and disappear as photos and overlays change. They are absent in Live view; blank property editors hide when no widget is selected. |
| Alignment icons | All six vector buttons retain single-widget canvas alignment and multi-selection alignment, with accessible names and descriptive tooltips. |
| Numeric editing | Actual numeric fields reject invalid dimensions, opacity, range endpoints, photo timing and caption settings before their setters clamp them. Inline feedback appears while typing, corrections clear it, finite-number checks reject malformed values, and comma decimals honour the field culture. Invalid drafts preserve the last valid model value. |
| Configure a selected widget | The actual search box filters the Source dropdown. GPU and multiword queries, empty results, Clear, advanced sensors and switching widgets preserve the current sensor until an explicit choice; Hybrid saves/reopens that choice. Search never adds widgets. |
| Add, edit and save Info Screen | Direct Add command creates a widget; the application save path writes it into the reopened `.t3theme`. |
| Save Photo Frame | The app saves global caption settings and retains the linked playlist. `.pcphoto` files exclude photos. |
| Add and save Hybrid | The app saves an added shape and its exact position into `.pchybrid`. |
| Switch modes and reopen | Independent photo settings, theme paths, dirty state, Info Screen layers, Hybrid positions and imported assets survive session save and recreation. |
| Tray workflow | An actual WPF window is shown, minimized, restored, closed to tray, restored again and exited; a new view model restores its session. |
| Reconnect | A fake display temporarily rejects a connection; automatic error handling and a subsequent successful reconnect are verified. No physical USB transfer is proved. |
| Windows startup | An isolated per-user, interactive task is registered, inspected and deleted on the Windows runner. Single-instance activation and noninteractive scheduled startup policy are checked. This does not simulate a real reboot or a user's UAC approval. |
| UI readability | Actual selected-item templates and tooltip colours are inspected, and real WPF controls are captured for review. The checks do not claim that every OS configuration has been reviewed. |
| Published package | The self-contained executable starts in smoke-test mode, the portable ZIP is extracted and the extracted executable starts again. Required files, native rendering library, fonts, licences and the verified PawnIO prerequisite are checked. |

## Physical-device and first-time user checklist

These checks remain pending for this release unless an explicit dated record is added. CI success is not a hardware-reliability result.

1. With other screen software closed, detect and connect the actual display. Check orientation, colours and live values.
2. Add a text, value and circular gauge, resize them, choose sources, save and reopen the Info Screen theme.
3. In Photo Frame, add a folder, change captions and timing, save and reopen. Confirm the original photos stay on the PC.
4. In Hybrid, add and position overlays, save and reopen. Confirm both the folder and exact positions.
5. Close to tray, restore, then unplug/replug and reconnect the screen.
6. Exit, reopen, then restart Windows. Confirm the last mode/theme restores and output resumes after sign-in with the approved startup task.
7. Run a representative album and sensor theme for several hours in the tray; note responsiveness, memory and any missing readings.
8. Ask someone unfamiliar with the app to complete connection, adding and saving without instructions. Record where they hesitate.

Report issues with app version, Windows version, device details, exact steps and whether the problem occurs on the preview, physical screen or both. Local crash/startup logs exist under `%LOCALAPPDATA%\PCInfoScreenStudio\Logs`; there is no automatic remote crash reporting.
