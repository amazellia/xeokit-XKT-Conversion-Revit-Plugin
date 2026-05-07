using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace XKTConversionRevitPlugin
{
    public class Application : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                var tab = "xeokit";
                app.CreateRibbonTab(tab);

                var panel = app.CreateRibbonPanel(tab, "Export");

                var assemblyPath = Assembly.GetExecutingAssembly().Location;

                var button = new PushButtonData(
                    "ExportXKT",
                    "Export\nto XKT",
                    assemblyPath,
                    typeof(ExportXKTCommand).FullName)
                {
                    ToolTip = "Export the current Revit model to xeokit XKT format (.xkt + metadata.json)",
                    LongDescription = "Exports all 3D elements with geometry, materials, and BIM metadata to the xeokit XKT v12 binary format for web-based 3D viewing."
                };

                TrySetIcon(button, "xeokit_icon_32.png");

                panel.AddItem(button);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("XKT Plugin", $"Failed to load ribbon: {ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        private static void TrySetIcon(PushButtonData button, string resourceName)
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                var path = Path.Combine(dir, "Resources", resourceName);
                if (File.Exists(path))
                    button.LargeImage = new BitmapImage(new Uri(path));
            }
            catch { /* icon is optional */ }
        }
    }
}
