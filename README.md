# Image Processing App

A .NET 8 Windows Forms workspace for exploring image-processing operations. Open `ImageProcessingApp.sln` in Visual Studio, or use the commands below on Windows with the .NET SDK and .NET 8 Desktop Runtime installed.

## Build and run

```powershell
dotnet build ImageProcessingApp.sln --artifacts-path .artifacts -c Debug
& '.\.artifacts\bin\ImageProcessingApp\debug\ImageProcessingApp.exe'
```

Build output is isolated because the original repository includes tracked `bin` and `obj` files. The existing AForge 2.2.5 packages produce NU1701 compatibility warnings when targeting .NET 8; this change retains the original camera dependency.

## Workspaces

- **Basic Filters:** load an image, apply Copy Original, Grayscale, Invert, or Sepia, compare the result, and save. Histogram opens a separate chart with an accessible table of all 256 intensity bins.
- **Background Subtraction:** load a foreground and replacement background. The original brightness-based keying replaces pixels whose average RGB intensity is within 5 of 85. Different image sizes use their top-left overlap.
- **Webcam:** select a camera, start capture, and open the effect dropdown above the previews to choose among 15 live effects by category. Leaving the tab stops capture. Camera discovery does not start capture.
- **Convolution:** open **Choose filter** above the previews to explore 18 filters in Smooth & sharpen, Motion blur, Edge detection, and Emboss. Every operation starts from the original, including repeated selections. Inputs must be at least 3 × 3 pixels; transparent regions are flattened onto white for 24-bit convolution.

Each workspace retains independent images. Clear Images releases that workspace's inputs and result. Saving supports PNG, JPEG, and BMP, with an explicit encoder for the chosen format. JPEG transparency is flattened onto white.

Use **Light / Dark** to change appearance; the preference is saved under `%LOCALAPPDATA%\ImageProcessingApp\settings.json`. Windows High Contrast overrides the palette. Small windows stack previews and provide vertical scrolling. Native file dialogs remain available. The main window and Histogram share a compact custom title bar with native dragging, edge resizing, double-click maximize, window snapping, Alt+Space, and Alt+F4. Close requests await processing and camera shutdown.

The creative workspace uses pill navigation, rounded preview cards, Bahnschrift headings (Segoe UI fallback), and Segoe UI body text. Light mode pairs off-white surfaces with violet actions; dark mode pairs charcoal surfaces with lavender accents. Save actions use teal. Spacing, 16-pixel card corners, and 10-pixel control corners scale with DPI. Focus rings, selected navigation states, disabled controls, and native menu keyboard behavior remain available. Image canvases and transparency checkerboards stay neutral and static.

**Reduce motion**, beside Light / Dark, persists in the same settings file. Older settings files remain valid and fresh installs start in light mode. Windows client-area animation preferences and High Contrast take precedence: either can disable motion even when the app toggle is off. The preference uses [Microsoft's documented client-area animation setting](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa).

With motion enabled, low-opacity lavender and teal shapes drift through the header and exposed gutters on a 24-second loop. Hover feedback takes 160 ms, presses 90 ms, and navigation selection 180 ms; actions and content switches happen immediately. Each window owns one disposable scheduler, running at about 30 FPS for ambient motion and up to 60 FPS for brief interactions. It pauses when the window is hidden, inactive, or minimized, and stops when idle. Cached gradients and shadows are regenerated for size, DPI, or theme changes. Animation does not move controls, relayout workspaces, invalidate image canvases, or modify image pixels. Custom-frame handling follows [Microsoft's DWM guidance](https://learn.microsoft.com/en-us/windows/win32/dwm/customframe).

Shortcuts: **Ctrl+O** loads the active still-image workspace's main input; **Ctrl+S** saves its result; **Ctrl+Tab** / **Ctrl+Shift+Tab** switch workspaces. When navigation is focused, **Left/Right**, **Up/Down**, and **Home/End** select a workspace; the same controls work for Histogram's Chart / Values views. For either filter dropdown, use **Enter**, **Space**, or **Alt+Down** to open it, arrow keys to navigate categories and filters, **Enter** to apply, and **Escape** to dismiss without changing the selection. The current selection appears on the trigger and has a menu checkmark; its explanation appears underneath. Basic Filters keeps its existing buttons and focus explanations.

## Webcam effects

The Webcam workspace offers **None, Grayscale, Invert, Sepia, Subtract, Gaussian Blur, Sharpen, Sobel Edges, Prewitt Edges, Scharr Edges, Motion Blur Horizontal, Motion Blur Vertical, Motion Blur Diagonal, Emboss NW–SE, and Emboss NE–SW**.

| Category | Filters |
|---|---|
| Top-level option | None |
| Color & tone | Grayscale, Invert, Sepia |
| Background | Subtract |
| Smooth & sharpen | Gaussian Blur, Sharpen |
| Motion blur | Motion Blur Horizontal, Motion Blur Vertical, Motion Blur Diagonal |
| Edge detection | Sobel Edges, Prewitt Edges, Scharr Edges |
| Emboss | Emboss NW–SE, Emboss NE–SW |

Choose an effect before or during capture. Each new frame uses the selected effect on its original pixels; effects do not accumulate. None shows an unchanged copy, and Subtract keeps the existing brightness-based keying to black. Blur, sharpening, edge, and emboss effects use the same fixed 3 × 3 processing as the Convolution workspace, including its unchanged one-pixel border.

The rounded dropdown defaults to **None**. Choose a category, then a filter; its explanation appears below the selector. Menu items also provide descriptions and tooltips. Processing runs on the capture thread, and only one frame is processed or waiting for display at a time to prevent a preview backlog. Frame rate depends on the effect, capture resolution, and hardware.

## Convolution presets

| Group | Filters |
|---|---|
| Smooth & sharpen | Smooth, Gaussian Blur, Sharpen, Mean Removal |
| Motion blur | Motion Blur Horizontal, Motion Blur Vertical, Motion Blur Diagonal |
| Edge detection | Laplacian, Sobel Edges, Prewitt Edges, Scharr Edges |
| Emboss | Horizontal + Vertical, All Directions, Lossy, Horizontal Only, Vertical Only, Emboss NW–SE, Emboss NE–SW |

All presets use fixed 3 × 3 neighborhoods, preserve image dimensions, and leave the one-pixel border unchanged after transparency is flattened onto white. Motion blur averages three pixels along the named direction; Diagonal runs from top-left to bottom-right. The new emboss presets produce diagonal relief centered on gray (128): NW–SE runs from top-left to bottom-right, and NE–SW from top-right to bottom-left.

The new preset kernels are listed below; semicolons separate rows.

| Preset | Kernel | Divisor | Offset |
|---|---|---:|---:|
| Motion Blur Horizontal | `0,0,0; 1,1,1; 0,0,0` | 3 | 0 |
| Motion Blur Vertical | `0,1,0; 0,1,0; 0,1,0` | 3 | 0 |
| Motion Blur Diagonal | `1,0,0; 0,1,0; 0,0,1` | 3 | 0 |
| Emboss NW–SE | `-2,-1,0; -1,0,1; 0,1,2` | 1 | 128 |
| Emboss NE–SW | `0,-1,-2; 1,0,-1; 2,1,0` | 1 | 128 |

Sobel, Prewitt, and Scharr first use average RGB intensity `(R + G + B) / 3`. They combine signed gradients from `-a,0,a; -b,0,b; -a,0,a` and its transpose, with `(a,b)` set to `(1,2)`, `(1,1)`, and `(3,10)` respectively. The grayscale result is `sqrt(Gx² + Gy²) / (2a+b)`, truncated and clamped to 0–255 for comparable preset strength. Kernel references: [OpenCV Sobel and Scharr](https://docs.opencv.org/4.12.0/d2/d2c/tutorial_sobel_derivatives.html) and [scikit-image Prewitt](https://scikit-image.org/docs/stable/api/skimage.filters.html#skimage.filters.prewitt_v). Laplacian retains its original kernel and offset.

The dropdown is disabled until a valid image is loaded and while an operation is running. Selecting a filter immediately processes the original image, including selecting the same filter again. Its name and explanation update after successful processing. Loading or clearing images resets the selection to **Choose filter**. Native category submenus keep all presets accessible while preserving usable previews; smaller windows scroll only when their content needs more space. Custom kernels and adjustable strengths are not included.

## Validation

```powershell
dotnet test ImageProcessingApp.sln --artifacts-path .artifacts -c Debug
dotnet build ImageProcessingApp.sln --artifacts-path .artifacts -c Release
```

Tests cover document ownership, result invalidation, original-based filters, convolution stride handling, cancellation, histogram source selection, transparency, subtraction bounds, explicit export formats, and detached source-file loading. New preset tests check numerical results, directional blur, constant images, signed edge transitions, emboss offsets, border preservation, all row-padding sizes, and minimum inputs. A hidden-window UI test exercises all workspaces and selects all 18 convolution menu items twice in both themes and wide/narrow layouts. It checks category membership, defaults, explanations, checkmarks, disabled and busy states, menu dismissal, Save availability, repeated filtering, clear, layout batching, preview placement, and histogram display. It opens each category submenu to check palette colors and screen bounds; optional captures include those popup menus. Webcam tests check every effect against its processing operation, select all 15 menu items, and send synthetic frames through the capture callback and preview publication, including dropping extra frames and stopping with a queued frame. Tests never start a camera.

Studio tests additionally cover interrupted and reversed transitions, elapsed-time completion, reduced-motion policy and legacy settings, disabled input states, scheduler pause/stop/disposal, zero layout passes and preview invalidations during ambient ticks, navigation keyboard/accessibility state, eight resize edges at 96/120/144/192 DPI, native caption hit-testing, taskbar-aware maximize/restore, and awaited processing shutdown from the Close button, system command, and WM_CLOSE. Histogram captures include both views in both themes and wide/narrow layouts. Resize-edge tests at multiple DPI values do not replace visual inspection on those displays.

Caption mouse tests also cover non-client press/double-click routing, capture loss, drag-away cancellation, and releasing capture before a window command. The optional visible-window checks send mouse messages directly to the title-bar buttons in both themes and verify minimize, maximize/restore, and close. They briefly show test windows but do not move or click the desktop pointer:

```powershell
$env:IMAGEPROCESSING_CAPTION_MOUSE_TESTS = "1"
dotnet test ImageProcessingApp.sln --artifacts-path .artifacts -c Debug --filter FullyQualifiedName~CaptionMouseTests
Remove-Item Env:IMAGEPROCESSING_CAPTION_MOUSE_TESTS
```

To save rendered UI previews while running the integration test:

```powershell
$env:IMAGEPROCESSING_UI_ARTIFACTS = Join-Path (Get-Location) '.artifacts/ui'
dotnet test ImageProcessingApp.sln --artifacts-path .artifacts -c Debug --filter FullyQualifiedName~UiWorkflowTests
```

Manual checks for a target machine:

- Test 100%, 125%, 150%, and 200% display scaling, moving between monitors, drag/double-click maximize, snapping (including the Windows 11 maximize hover menu), edge/corner resizing, Alt+Space/Alt+F4, keyboard navigation (including Enter/Space/Alt+Down, arrows, Enter, and Escape), Narrator, and Windows High Contrast. Inspect open menus and submenus near each screen edge, and confirm focus returns to the dropdown after dismissal. Toggle Reduce motion, disable Windows animation effects, and verify that inactive/minimized windows stop decorative motion.
- Check camera absence, capture startup failure, repeated start/stop, effect changes, disconnection, tab switching, and closing during capture. A driver that never completes shutdown can leave the window waiting; shutdown stays off the UI thread and confirmed stop failures allow retry.
- Exercise canceled file dialogs, unreadable/corrupt images, unwritable save locations, and a large image while resizing or changing themes.

Convolution preserves the original kernel coefficients and offsets. Row addressing was corrected to use the bitmap stride for every row; this fixes shifted processing in the old implementation.
