# xeokit XKT Conversion Plugin for Revit

Export any Revit model to the **xeokit XKT format** with one click — no coding, no extra software, no conversion steps.

The plugin adds an **xeokit** tab to your Revit ribbon. Click **Export to XKT**, pick a folder, and you're done. The result is two files you can drop straight into any xeokit-based web viewer.

---

## What you get after exporting

| File | What it is |
|------|------------|
| `YourModel.xkt` | The 3D model — geometry, colours, and materials |
| `YourModel-metadata.json` | The BIM data — element names, categories, families |

---

## Before you start — check your Revit version

Open Revit, click the **?** (Help) icon in the top-right corner, then **About Autodesk Revit**.  
The version number will say something like **Revit 2025**, **Revit 2026**, or **Revit 2027**.

> ⚠️ This plugin supports **Revit 2025, 2026, and 2027**.  
> If you have an older version (2024 or earlier) see the [Older Revit versions](#older-revit-versions) section at the bottom.

---

## Installation — step by step

### Step 1 — Download the plugin files

1. Go to the **[Releases page](../../releases/latest)** of this repository on GitHub.
2. Under **Assets**, find the zip that matches your Revit version and click it to download:

   | Your Revit version | File to download |
   |--------------------|-----------------|
   | Revit 2025 | `XKTPlugin-Revit2025.zip` |
   | Revit 2026 | `XKTPlugin-Revit2026.zip` |
   | Revit 2027 | `XKTPlugin-Revit2027.zip` |

3. Once downloaded, **right-click** the zip file and choose **Extract All…**
4. Extract it somewhere easy to find, like your Desktop.

You should now have a folder containing these files:
```
XKTConversionRevitPlugin.dll
XKTConversionRevitPlugin.addin
Newtonsoft.Json.dll
ICSharpCode.SharpZipLib.dll
📁 viewer/
   └── index.html
```

---

### Step 2 — Find the Revit add-ins folder

This is the folder where Revit looks for plugins. You need to copy the files there.

1. Press **Windows key + R** on your keyboard to open the Run dialog.
2. Copy and paste **exactly** this path into the box, then press **OK**:

   ```
   %APPDATA%\Autodesk\Revit\Addins\2027
   ```

   > If your Revit year is different, change `2027` to match — e.g. `2025` or `2026`.

3. A File Explorer window will open showing the Addins folder.  
   *(If the folder doesn't exist yet, that's fine — create it by right-clicking → New → Folder and naming it `2027`.)*

---

### Step 3 — Copy the plugin files

1. Go back to the extracted folder from Step 1.
2. Select **all four files** (Ctrl + A).
3. Copy them (Ctrl + C).
4. Switch to the Addins folder window from Step 2.
5. Paste them (Ctrl + V).

That's it! Your Addins folder should now look like this:

```
📁 2027
   ├── XKTConversionRevitPlugin.dll
   ├── XKTConversionRevitPlugin.addin
   ├── Newtonsoft.Json.dll
   └── ICSharpCode.SharpZipLib.dll
```

---

### Step 4 — Launch Revit

1. **Close Revit completely** if it was already open, then reopen it.
2. Open any Revit project (`.rvt` file).
3. Look at the ribbon along the top — you should see a new tab called **xeokit**.

> If you don't see the tab, see [Troubleshooting](#troubleshooting) below.

---

## Using the plugin

1. Open your Revit project.
2. Click the **xeokit** tab in the ribbon.
3. Click **Export to XKT**.
4. A folder picker will appear — choose (or create) a folder where you want the files saved.
5. Wait for the export to finish. A summary box will appear showing how many elements were exported.
6. Navigate to your chosen folder — you'll find the two exported files there.

---

## Previewing your model in a browser

A simple web viewer is included in the `viewer/` folder of this project.

1. Download the project's `viewer/index.html` file (or find it in the extracted zip).
2. Open it by double-clicking it — it will open in your web browser.
3. Click **Open XKT File…** and select your `.xkt` file.
4. When prompted, also select the `-metadata.json` file.

Your 3D model will appear in the browser. Use:
- **Left mouse drag** — rotate
- **Right mouse drag** — pan
- **Scroll wheel** — zoom
- **F key** — fit the whole model into view

---

## Troubleshooting

### The xeokit tab doesn't appear in Revit

- Make sure you fully closed and reopened Revit after copying the files.
- Double-check the files are in the correct folder for your Revit year (Step 2).
- Make sure all four files were copied — if `XKTConversionRevitPlugin.addin` is missing, Revit won't know the plugin exists.
- On first load, Revit may show a security popup asking whether to load the plugin. Click **Always Load** or **Load Once**.

### "Could not load file or assembly" error in Revit

This usually means .NET 8 is not installed on your computer.

1. Visit **[https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)**
2. Under **.NET 8.0**, click **Download** next to **Windows x64 Runtime**.
3. Run the installer and restart your computer.
4. Open Revit again.

### The export finishes but the XKT file is empty or very small

- Make sure your Revit view has 3D elements visible. Open a 3D view first (the plugin exports all model elements regardless of active view, but the model must have 3D geometry).
- Very simple or family-only files with no placed instances may produce minimal output.

### The viewer shows a blank screen

- Make sure you loaded the `.xkt` file (not the `.json` file) as the main file.
- Try a different browser. Chrome or Edge work best. Firefox also works.
- If you see an error in the viewer, make sure the `.xkt` file is not 0 KB in size.

---

## Older Revit versions

Revit 2024 and earlier use a different version of .NET (.NET Framework 4.8 instead of .NET 8).  
A separate build is required. On the [Releases page](../../releases/latest), look for a zip labelled **`XKTPlugin-Revit2024.zip`** and follow the same steps above using `2024` as the folder name in Step 2.

---

## For developers — building from source

If you want to modify the plugin or build it yourself:

**Prerequisites:**
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (free Community edition works) with the **.NET desktop development** workload installed
- Revit 2027 installed at `C:\Program Files\Autodesk\Revit 2027\`

**Steps:**

```powershell
git clone https://github.com/YOUR_USERNAME/YOUR_REPO_NAME.git
cd "xeokit XKT Conversion Revit Plugin"
dotnet restore
dotnet build -c Release
```

The built files will appear in `bin\Release\net8.0-windows\`. Copy them to the Addins folder as described in the install steps above.

> If your Revit is installed in a different location, open `XKTConversionRevitPlugin.csproj` in a text editor and update the two `<HintPath>` lines to match your install path.

---

## Project structure (for developers)

```
xeokit XKT Conversion Revit Plugin/
├── src/
│   ├── Application.cs              Adds the xeokit ribbon tab
│   ├── ExportXKTCommand.cs         Runs when you click Export to XKT
│   ├── ExportProgress.cs           Progress tracking
│   ├── XKT/
│   │   ├── XKTModel.cs             In-memory data structures
│   │   ├── XKTWriter.cs            Writes the .xkt binary file
│   │   └── MetadataWriter.cs       Writes the -metadata.json file
│   └── Revit/
│       └── RevitModelExtractor.cs  Reads geometry and metadata from Revit
├── viewer/
│   └── index.html                  Browser-based preview tool
├── XKTConversionRevitPlugin.csproj
└── XKTConversionRevitPlugin.addin
```

---

## Known limitations

- Textures and UV maps are not exported (colours and materials are, just not image textures).
- Linked Revit models (`.rvt` links) are not included in the export.
- Very large models (500,000+ elements) may take several minutes and use significant memory.
