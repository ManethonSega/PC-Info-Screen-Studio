# Docked workspace candidate validation

Source version: 0.10.0-alpha.22. Qualification of the latest branch revision, including its matching workflow and capture review, is maintained in [draft PR #32](https://github.com/ManethonSega/PC-Info-Screen-Studio/pull/32).

Local checks: XAML XML parsing and repository-maintenance safeguards. The Linux host cannot execute WPF, so these checks do not establish native layout correctness.

Windows coverage added: real panel toggle/invoke/range peers, independent stacked panels, keyboard height adjustment and ordering, saved states/order/widths/heights, restart restoration, reset, compact canvas reservation, Fit bounds for both display families/orientations, Photo Frame/Hybrid/Live visibility and manual zoom preservation. The checks assert that workspace-only changes preserve artwork and dirty state. Damaged optional layout fields are repaired while retaining other valid preferences.

Native capture targets include docked, collapsed, compact stacked and Hybrid arrangements plus the existing wide/compact mode views. Interactive DPI 125/150/200%, Narrator/high-contrast traversal and user hardware remain awaiting qualification. Free dragging/floating are deferred until the docked foundation is reliable.

## Windows baseline and capture correction

[Windows run 38737562363](https://github.com/ManethonSega/PC-Info-Screen-Studio/actions/runs/38737562363), candidate `e53e7093da364bf522b8d63caf9a3687da5b5644`, merge checkout `3aa656a53c406819d0e315b6bc959612b9a6e6ae`: zero build warnings/errors, all 30 controller/WPF/capture groups, all 12 repository-maintenance safeguards, published-EXE startup and extracted portable-ZIP startup passed. The preceding failing range-control test demonstrated the preferred-width/constraint defect and passes on this revision.

Reviewed the native wide/compact Info Screen, Photo Frame and Hybrid views, Settings and the four workspace scenarios. The first capture pass exposed stretched Add buttons and clipped narrow-panel labels. The follow-up anchors the widget grid to its natural height, reduces hardware-button padding, wraps the long sensor label and puts photo playback actions on two rows. A focused native height check protects the Add buttons. The final matching workflow and confirmation capture results are recorded in PR #32.

The workspace interaction captures use the runner's actual available window size. The separate mode captures explicitly arrange native WPF content at 1560×1040 and 1080×680; they do not establish monitor/DPI behavior beyond that runner.
