using Newtonsoft.Json;
using System.IO;

namespace XKTConversionRevitPlugin.XKT
{
    /// <summary>
    /// Writes the xeokit metadata JSON sidecar file.
    /// This file is loaded separately by the viewer as:
    ///   viewer.loadMetaModel({ id, src: "model-metadata.json" })
    /// </summary>
    public static class MetadataWriter
    {
        public static void Write(XKTModel model, string outputPath)
        {
            var metaObjects = new object[model.MetaObjects.Count];
            for (int i = 0; i < model.MetaObjects.Count; i++)
            {
                var mo = model.MetaObjects[i];
                metaObjects[i] = new
                {
                    id         = mo.Id,
                    name       = mo.Name,
                    type       = mo.Type,
                    parent     = mo.Parent,
                    external   = mo.Properties.Count > 0
                                    ? (object)new { properties = mo.Properties }
                                    : null
                };
            }

            var root = new
            {
                id                  = model.ModelId,
                projectId           = model.ProjectId,
                author              = model.Author,
                createdAt           = model.CreatedAt,
                creatingApplication = model.CreatingApplication,
                schema              = model.Schema,
                metaObjects
            };

            File.WriteAllText(outputPath,
                JsonConvert.SerializeObject(root, Formatting.Indented));
        }
    }
}
