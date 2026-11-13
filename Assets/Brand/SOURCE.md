# SC brand mark

User-supplied source: `SC_logo_vector-clean.svg`; the canonical copy in this repository is `sc-scriptforge-mark.svg`.
Copied unchanged to `sc-scriptforge-mark.svg` on 2026-10-23 (replaces the earlier `sc-mark.svg` from 2026-10-21).

SHA-256: `C1E4EDC945B3E907635EAB2CEEE1F7A28074474A56C99DD5C731602C7BFFD3BF`.

The SVG is a 1254×1254 tile: a navy radial-gradient plate (`<rect>`) with the turquoise S, white C and turquoise accent paths (linear gradients). `Tools/Generate-BrandAssets.ps1` reads its viewBox, paths and gradients and generates `BrandResources.xaml`, the ICO at 16/20/24/32/40/48/64/128/256 px, PNG previews and the full wordmark. The Windows icon and wordmark draw the plate as a rounded square (radius 14/64 of the side).

In the application the mark is `Desktop/BrandMark.cs`, a native vector copy of the same paths and gradients on a rounded navy plate. It is used in the title bar, the sidebar footer and the Settings workspace card; set `Width`, `Height` and `CornerRadius` at the call site.

To update the mark, replace the canonical SVG (a path-based SVG with flat or gradient fills), rerun the generator, copy the new path data into `BrandMark.cs`, then rebuild/publish. Changing only a loose ICO beside the EXE does not update its native Explorer icon.
