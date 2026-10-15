# Studio UI refinement

The app is a .NET 10 WPF operating interface. Use collapsible docks with Themes/Widgets/Media/Photos/Layers on the left, the physical-screen canvas in the centre and contextual Properties/Sensors on the right. Keep Editor/Hardware navigation, mode/model selection, Settings, brightness and Start/Stop in the top bar. Photo Frame and Hybrid retain their own workspaces and expanded sections. The owner explicitly requested the Photoshop-inspired panel layout on 15 October 2026.

## Visual direction

Retain the MechKeyMesh navy/slate surfaces, silver text and cyan selection/action colours in App.xaml. Cyan identifies selection and primary actions; blue identifies keyboard focus. Neutral controls should recede while remaining recognizable. Use Segoe UI and the existing MDL2/vector icon vocabulary, with readable sentence-case section headings. Keep user-authored artwork, fonts, colours and colour emoji independent of the application chrome.

Use spacing and typography to group controls before adding more containers. Toolbar utilities use a quieter shared button style, the canvas loses an unnecessary outer card, and inspector sections use a chevron and heading rather than a stack of dark boxes. Dense desktop fields keep their existing heights. The native layout uses WPF device-independent units, not mobile touch-size rules.

Canvas tools wrap as whole groups when the centre column narrows. The toolbar row sizes to its content. Fit scales the preview from its available viewport while preserving aspect ratio and theme/widget geometry. Manual zoom (10% to 300%) and 100% view retain scrolling and remain stable during panel/window changes. Fit uses up to 300% magnification. Docks reserve layout space, so panels cannot cover the canvas. A 104-unit rail keeps icons and names visible. Open sections share height with native GridSplitters, individual content scrolls, and an outer scroll fallback keeps every section reachable if many are opened in a short window. Width grips support dragging, arrow keys and the UI Automation range pattern. Header actions reorder panels without dragging. Compact windows reduce effective dock widths while preserving saved preferred widths and canvas space. Widget kinds use presentation-only labels; saved enum values and theme formats stay the same. Layer visibility and locking retain distinct actions and contextual accessible names.

## Interaction and accessibility

Keep editing, selection, alignment and keyboard actions immediate. Dropdowns open without fade; the brightness popup retains its current behaviour. Toolbar tooltips have an initial delay and a short path to subsequent hints. Hover and keyboard focus use distinct border colours. Settings supports Escape and cycles tab navigation inside its modal window.

Give native controls meaningful UI Automation names independently of their current values, including X/Y/size fields, source filtering, editable fonts, colour actions, screen selection and mode selection. Preserve the existing inline numeric validation and command guards. Readable names and visible focus are implemented mechanisms, not a claim of complete screen-reader or high-contrast certification.

## How the four skills contributed

| Skill | Application to this interface |
| --- | --- |
| Impeccable | Operate-mode hierarchy, restrained framing, native Windows bridge and preservation of the incumbent layout/identity. |
| Web Design Guidelines | Framework-neutral checks for named inputs, focus, long content and feedback. HTML/ARIA/URL rules are translated only where a WPF equivalent applies. |
| UI/UX Pro Max | Executed `keyboard focus accessibility --stack wpf` and `form labels --domain ux`; applied the WPF and associated-label results. The existing palette stays authoritative. |
| Emil Design Engineering | Immediate feedback for repetitive actions, no decorative layout motion, and quicker consecutive tooltips. CSS/React examples do not introduce a new framework. |

## Framework references

- [WPF WrapPanel](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.wrappanel?view=windowsdesktop-10.0), native wrapping layout.
- [WPF AutomationProperties](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationproperties?view=windowsdesktop-10.0), accessible names independent of values.
- [WPF focus and keyboard navigation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview), native focus semantics and navigation containers.
- [WPF consecutive tooltip timing](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.tooltipservice.betweenshowdelay?view=windowsdesktop-10.0).
- [Vercel Web Interface Guidelines](https://github.com/vercel-labs/web-interface-guidelines/blob/main/command.md), retrieved 15 October 2026 and scoped to relevant desktop outcomes.

## Verification scope

The Windows workflow builds WPF, exercises the existing controller/UI suite, captures wide and compact Info Screen/Photo Frame/Hybrid views plus Hardware and Settings, then checks the published EXE and extracted portable ZIP. Added checks inspect real WPF toolbar bounds and UI Automation peers. Build results and capture review belong in the matching validation note.

Windows DPI settings at 125%, 150% and 200%, Narrator traversal and high-contrast operation still need their own interactive qualification. Source/XML checks or a web detector do not certify these. No physical display or media renderer behaviour is changed by the UI refinement.

## Docked workspace persistence

New installations and older settings default to Widgets/Layers open on the left and Properties on the right. Themes, Media and Sensors are optional sections; Photos is open in Photo Frame/Hybrid. Mode-specific sections hide without losing their open state. Live view hides both docks. No panel is automatically closed on resize.

Open states, panel order, preferred widths and relative heights are app settings, independent of themes, session artwork and Undo. Layout values are bounded and repaired independently of valid brightness and other preferences. Reset workspace restores panel defaults and Fit, preserving artwork. Collapsing keeps the same content controls and selected widget alive. Repeated panel operations are immediate, with no layout animation.

Floating windows and free docking drag remain a later phase. This implementation supplies explicit header reorder actions and resizing, not a full Photoshop docking framework.

- [WPF row resizing](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-resize-rows-with-a-gridsplitter), native keyboard/mouse splitters.
- [UI Automation range provider](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.irangevalueprovider?view=windowsdesktop-10.0), accessible panel width control.
