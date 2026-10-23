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
- **Webcam:** select a camera, start capture, and choose an effect. Leaving the tab stops capture. Camera discovery does not start capture.
- **Convolution:** explore smoothing, sharpening, and emboss filters. Every operation starts from the original, including repeated clicks. Inputs must be at least 3 × 3 pixels; transparent regions are flattened onto white for 24-bit convolution.

Each workspace retains independent images. Clear Images releases that workspace's inputs and result. Saving supports PNG, JPEG, and BMP, with an explicit encoder for the chosen format. JPEG transparency is flattened onto white.

Use **Light / Dark** to change appearance; the preference is saved under `%LOCALAPPDATA%\ImageProcessingApp\settings.json`. Windows High Contrast overrides the palette. Small windows stack previews and provide vertical scrolling. Native file dialogs and title bars follow Windows styling.

Both themes use indigo primary actions, teal Save buttons, and softly tinted filter controls. Buttons and selection toggles have rounded corners that scale with display DPI, with distinct keyboard-focus, hover, pressed, and disabled states. Image canvases stay neutral for comparing results.

Shortcuts: **Ctrl+O** loads the active still-image workspace's main input; **Ctrl+S** saves its result; **Ctrl+Tab** / **Ctrl+Shift+Tab** switch workspaces. Keyboard focus on a filter exposes its explanation.

## Validation

```powershell
dotnet test ImageProcessingApp.sln --artifacts-path .artifacts -c Debug
dotnet build ImageProcessingApp.sln --artifacts-path .artifacts -c Release
```

Tests cover document ownership, result invalidation, original-based filters, convolution stride handling, cancellation, histogram source selection, transparency, subtraction bounds, explicit export formats, and detached source-file loading. A hidden-window UI test exercises all workspaces in both themes, wide/narrow layouts, repeated filtering, clear, and histogram display. It never starts a camera.

To save rendered UI previews while running the integration test:

```powershell
$env:IMAGEPROCESSING_UI_ARTIFACTS = Join-Path (Get-Location) '.artifacts/ui'
dotnet test ImageProcessingApp.sln --artifacts-path .artifacts -c Debug --filter FullyQualifiedName~UiWorkflowTests
```

Manual checks for a target machine:

- Test 100%, 125%, 150%, and 200% display scaling, moving between monitors, keyboard navigation, Narrator, and Windows High Contrast.
- Check camera absence, capture startup failure, repeated start/stop, effect changes, disconnection, tab switching, and closing during capture. A driver that never completes shutdown can leave the window waiting; shutdown stays off the UI thread and confirmed stop failures allow retry.
- Exercise canceled file dialogs, unreadable/corrupt images, unwritable save locations, and a large image while resizing or changing themes.

Convolution preserves the original kernel coefficients and offsets. Row addressing was corrected to use the bitmap stride for every row; this fixes shifted processing in the old implementation.
