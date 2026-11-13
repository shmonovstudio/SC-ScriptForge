# JetBrains Mono

Unmodified static Regular (400) and Medium (500) from the official stable release 2.304.

- Upstream: https://github.com/JetBrains/JetBrainsMono
- Pinned release: https://github.com/JetBrains/JetBrainsMono/tree/v2.304
- Files: `fonts/ttf/JetBrainsMono-Regular.ttf`, `fonts/ttf/JetBrainsMono-Medium.ttf`.
- License: SIL Open Font License 1.1, included as `OFL.txt` and published as `Licenses/JetBrainsMono-OFL.txt`.

Used for PowerShell source, diffs, logs, paths and technical parameter values. Embedded in the assembly without system installation.

Release 2.304 is intentional: the Medium binary at upstream master commit `19371302b95d218af43299bce79ddbddd0bc364d` declares `usWeightClass=436`. The bundled release declares 500, verified from the font metadata rather than inferred from the filename.
