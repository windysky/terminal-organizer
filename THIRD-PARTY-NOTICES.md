# Third-Party Notices

TerminalOrganizer reimplements parts of the layout geometry and file-format
rules of **Microsoft PowerToys (FancyZones)** in C#, so that its window
placement is exactly compatible with the layouts you draw in FancyZones.

The following portions of this codebase are derived from PowerToys,
Copyright (c) Microsoft Corporation, licensed under the MIT License (full
text below). The code comments in each file cite the exact upstream
locations; reference version: PowerToys v0.101.2362.0.

- `src/TerminalOrganizer.Core/Geometry/GridZoneCalculator.cs` — C# port of
  the grid zone calculation (`LayoutConfigurator.cpp`, CalculateGridZones).
- `src/TerminalOrganizer.Core/Geometry/CanvasZoneCalculator.cs` — C# port of
  the canvas branch of `LayoutConfigurator::Custom`, including the DPI
  conversion rules.
- `src/TerminalOrganizer.Core/Geometry/TemplateZoneCalculator.cs` — the
  predefined template table (copied verbatim) and the built-in template
  geometry.
- `src/TerminalOrganizer.Core/Layouts/FancyZonesJsonReader.cs` — the
  required-field and shape rules of the `custom-layouts.json` /
  `applied-layouts.json` file formats.
- `src/TerminalOrganizer.Core/Layouts/LayoutModel.cs` — constants mirrored
  from PowerToys FancyZones.

All other code in this repository is original work. The application uses
only the .NET Framework class library and Windows platform APIs
(UI Automation, WMI, Win32) — no third-party libraries or assets are included.

---

## MIT License (applies to the PowerToys-derived portions listed above)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
