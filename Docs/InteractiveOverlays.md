# Interactive RHUD overlays (API v1)

`ClientPlugin.RichHud.InteractiveOverlayRegistry` is an additive host for pack
windows. `HudOverlayRegistry` remains the corner-text API. Rich HUD Master is
optional; packs retain their existing Pulsar settings when it is absent. Do not
vendor a second client or take a compile-time Anomaly/RHUD reference.

Resolve the registry by type name, check its constant `ApiVersion == 1`, call
`RequestWindow(id, title)`, and obtain the returned object's
`ClientPlugin.RichHud.IInteractiveOverlayWindow` interface. Repeated requests for
the same id return the same handle. Bind interface methods to CLR delegates once;
do not reflect per vertex or draw call. `Unregister(id)` removes the window and
its capture. Sessions and Master resets unmount the host but retain registrations
for remounting; the window remains closed.

## Lifecycle and controls

- `Available` indicates a mounted, registered RHUD host. `Visible` is the current
  window state, not a promise that a queued request has mounted.
- `SetVisible(bool)` queues visibility. Explicit opens close the RHUD terminal
  and wait at most ten seconds for gameplay focus. Chord opens are ignored during
  chat/text entry, loading, terminal and other GUI modals.
- `SetBinding(int key, bool ctrl, bool alt, bool shift)` sets an exact-modifier
  chord using the VRage MyKeys numeric value. MyKeys.None unbinds it.
- `SetHandlers(input, resize, select, visibility)` uses
  `Action<int,float,float,float>`, `Action<float,float>`, `Action<long>` and
  `Action<bool>`. Input kind 0 is click (x/y pixels), 1 orbit and 2 pan (x/y deltas),
  3 wheel (z in steps). Resize reports viewport width/height in HUD pixels.
- `Button(text, Action)`, `Checkbox(text, Func<bool>, Action<bool>, Func<bool>)`
  and `Slider(text, min, max, Func<float>, Action<float>)` define side-panel
  controls before first opening. `Refresh()` pulls cached getter values;
  setters are suppressed during refresh. No CustomValueGetter is attached.

Callbacks execute on the HUD thread. Enqueue commands for game-thread entity
access and GUI actions. Getters must return cached values. Mark pack config
dirty and flush through its existing deferred save worker; never write files in
callbacks.

The host uses the pinned RHUD client, HighDpiRoot and ResizableWindow. It starts
at 80% of screen dimensions, with a 300-HUD-pixel right panel and a minimum size
of 700×460. Object rows use reusable slots sized to the viewport height, so large
screens and low UI scales can show every visible row. Scroll
over the side panel scrolls controls/details/list; scroll over the viewport is
delivered to the pack. Escape/chord/Close dismiss the window. A focus-only GUI
screen blocks raw gameplay inputs alongside RHUD's temporary blacklist without
pausing the world. Its GUI lifecycle runs on Anomaly's game update thread.

## Geometry and object list

`SubmitGeometry(float[] lines, int lineCount, float[] triangles, int triangleCount,
string[] labels, float[] labelPositions)` copies pack buffers into host-owned
storage. Packs can reuse their buffers immediately after returning.

Coordinates are viewport-local HUD pixels, centered at (0,0), with Y up.
Each line is eight floats: `x1,y1,x2,y2,r,g,b,a`.
Each triangle is ten floats: `x1,y1,x2,y2,x3,y3,r,g,b,a`.
RGBA is normalized sRGB tint plus alpha; the host converts through RHUD's
premultiplied billboard color helper. Each label has two floats, `x,y`.
Counts are primitives, not floats. Caps are 4,096 lines, 1,024 triangles and 128
labels. Invalid lengths, counts or nonfinite values throw ArgumentException.

The pack performs 3D projection, near-plane clipping and depth ordering. The host
draws triangles in submitted order, followed by lines, with the RHUD viewport mask.
It submits a batch of post-processing billboard triangles, taking no render target
or shader ownership. Final Frontier clips its projected polygons before submission.

`SubmitRows(long[] ids, string[] rows, string details, string status)` copies equal-
length id/text arrays. Selection returns the id of the clicked visible row. Rows
are virtualized independently of the geometry budgets. Apply permissions and
metadata redaction in the pack before any geometry, labels, rows or details are
submitted. Opening/closing clears previous payloads so remounts do not expose an
old visibility snapshot. Submit a fresh snapshot on the visibility callback.

All public payloads are primitive CLR arrays and delegates; no closed VRage
tuple, plugin DTO, RHUD object or collection generic crosses this API boundary.
This avoids the loader-context identity issue handled by the existing RHUD
handshake patch.

## Validation

Final Frontier's `Tests/Run-MapTests.ps1` verifies both runtimes, buffer ownership,
limits, nonfinite rejection, reflection binding and input policy, including keeping
the map open when Master's InputMode reports Full because of its own capture.
The net10.0 harness also exercises the contract in an isolated load context.
Actual Master reset, GUI input capture, drawing and hardware costs need the
documented in-game acceptance pass; a successful build is not visual acceptance.
