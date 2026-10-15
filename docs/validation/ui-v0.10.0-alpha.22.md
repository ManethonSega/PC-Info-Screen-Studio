# Docked workspace candidate validation

Source version: 0.10.0-alpha.22. This note will record the matching Windows workflow and reviewed captures when they are available.

Local checks: XAML XML parsing and repository-maintenance safeguards. The Linux host cannot execute WPF, so these checks do not establish native layout correctness.

Windows coverage added: real panel toggle/invoke/range peers, independent stacked panels, keyboard height adjustment and ordering, saved states/order/widths/heights, restart restoration, reset, compact canvas reservation, Fit bounds for both display families/orientations, Photo Frame/Hybrid/Live visibility and manual zoom preservation. The checks assert that workspace-only changes preserve artwork and dirty state. Damaged optional layout fields are repaired while retaining other valid preferences.

Native capture targets include docked, collapsed, compact stacked and Hybrid arrangements plus the existing wide/compact mode views. Interactive DPI 125/150/200%, Narrator/high-contrast traversal and user hardware remain awaiting qualification. Free dragging/floating are deferred until the docked foundation is reliable.
