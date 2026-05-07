using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Windows.Forms;
using XKTConversionRevitPlugin.Revit;
using XKTConversionRevitPlugin.XKT;

namespace XKTConversionRevitPlugin
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportXKTCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            // Pick output directory
            using var dlg = new FolderBrowserDialog
            {
                Description  = "Select output folder for XKT export",
                ShowNewFolderButton = true
            };

            if (dlg.ShowDialog() != DialogResult.OK)
                return Result.Cancelled;

            var outputDir = dlg.SelectedPath;
            var modelName = SanitizeFileName(doc.Title);

            try
            {
                using var progress = new ExportProgress();

                // 1. Extract geometry + metadata from Revit
                var extractor = new RevitModelExtractor(doc, progress);
                var xktModel  = extractor.Extract();

                // 2. Write XKT binary file
                var xktPath = Path.Combine(outputDir, modelName + ".xkt");
                XKTWriter.Write(xktModel, xktPath);

                // 3. Write metadata JSON sidecar
                var metaPath = Path.Combine(outputDir, modelName + "-metadata.json");
                MetadataWriter.Write(xktModel, metaPath);

                TaskDialog.Show("XKT Export",
                    $"Export complete!\n\n" +
                    $"XKT file:      {xktPath}\n" +
                    $"Metadata:      {metaPath}\n\n" +
                    $"Entities:      {xktModel.Entities.Count:N0}\n" +
                    $"Geometries:    {xktModel.Geometries.Count:N0}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("XKT Export Error", ex.ToString());
                return Result.Failed;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');
            return string.IsNullOrWhiteSpace(name) ? "model" : name;
        }
    }
}
