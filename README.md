# XKT Conversion Revit Plugin

A Revit add-in that exports your model directly to the **xeokit XKT v12** binary format — no intermediate conversion step required.

## What it produces

| File | Description |
|------|-------------|
| `<ModelName>.xkt` | xeokit XKT v12 binary (geometry + materials + entity data) |
| `<ModelName>-metadata.json` | BIM hierarchy sidecar (project → category → family → element) |

Load both files in any xeokit viewer:
```js
const model = xktLoader.load({
  id: "myModel",
  src: "MyModel.xkt",
  metaModelSrc: "MyModel-metadata.json"
});
```

A ready-made test viewer is included at `viewer/index.html`.

---

## Requirements

| Requirement | Version |
|-------------|---------|
| Revit | 2025–2027 (adjust `.csproj` hint paths for other years) |
| .NET | 8.0 |
| Visual Studio | 2022 or 2025 |

---

## Build

```powershell
# Restore NuGet packages and build
dotnet restore
dotnet build -c Release
```

The output `XKTConversionRevitPlugin.dll` and `XKTConversionRevitPlugin.addin` go into:
```
%APPDATA%\Autodesk\Revit\Addins\2024\
```

---

## Install

1. Build the project (Release configuration).
2. Copy these two files to `%APPDATA%\Autodesk\Revit\Addins\2027\`:
   - `XKTConversionRevitPlugin.dll`
   - `XKTConversionRevitPlugin.addin`
   - `Newtonsoft.Json.dll`  
   - `ICSharpCode.SharpZipLib.dll`
3. Launch Revit — a new **xeokit** tab appears in the ribbon.

> For other Revit years, edit the `<HintPath>` entries in
> `XKTConversionRevitPlugin.csproj` and the Addins install folder to match the year.

---

## Usage

1. Open a Revit project.
2. Click **xeokit → Export to XKT**.
3. Choose an output folder.
4. Open `viewer/index.html` in a browser and load the exported files.

---

## XKT v12 Format Notes

- **Positions** are quantised to `Uint16` using the model AABB → decoded by a 4×4 float matrix stored in the file.
- **Normals** are oct-encoded to `Int8` (2 bytes per normal).
- **All arrays** are individually zlib-deflated (RFC 1950) before packing.
- The file begins with a `Uint32` magic word: `(1 << 31) | 12`.
- 29 arrays are stored in the fixed order documented in `src/XKT/XKTWriter.cs`.

---

## Project Structure

```
XKTConversionRevitPlugin/
├── src/
│   ├── Application.cs              Revit ribbon registration
│   ├── ExportXKTCommand.cs         IExternalCommand entry point
│   ├── ExportProgress.cs           Simple progress reporter
│   ├── XKT/
│   │   ├── XKTModel.cs             In-memory data model
│   │   ├── XKTWriter.cs            XKT v12 binary writer
│   │   └── MetadataWriter.cs       metadata JSON writer
│   └── Revit/
│       └── RevitModelExtractor.cs  Geometry + metadata extraction
├── viewer/
│   └── index.html                  Browser-based XKT preview
├── XKTConversionRevitPlugin.csproj
└── XKTConversionRevitPlugin.addin
```

---

## Limitations / Roadmap

- Textures / UV maps are not yet exported (geometry + diffuse colour only).
- Very large models (>500k elements) may benefit from spatial tiling — currently the whole model is one tile.
- Linked Revit models are not traversed automatically.
