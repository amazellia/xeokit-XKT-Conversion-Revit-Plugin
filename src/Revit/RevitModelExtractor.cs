using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using XKTConversionRevitPlugin.XKT;

namespace XKTConversionRevitPlugin.Revit
{
    /// <summary>
    /// Walks all 3D elements in a Revit Document and builds an XKTModel.
    ///
    /// Geometry extraction strategy:
    ///  - Collect all Elements that produce 3D geometry.
    ///  - For each element extract its solid meshes via Options(DetailLevel.Fine).
    ///  - Group identical geometry symbol instances to mark IsReused (family instances
    ///    sharing a symbol definition become a single XKTGeometry with per-instance matrices).
    ///  - Map Revit material colours to XKT mesh materials.
    ///  - Build the BIM hierarchy (Project → Category → Family → Type → Instance)
    ///    into XKTMetaObject nodes for the metadata sidecar.
    /// </summary>
    public sealed class RevitModelExtractor
    {
        private readonly Document      _doc;
        private readonly ExportProgress _progress;

        // Key = ElementId of the FamilySymbol (for instancing); value = geometry index
        private readonly Dictionary<ElementId, int> _symbolGeomIndex = new();

        public RevitModelExtractor(Document doc, ExportProgress progress)
        {
            _doc      = doc;
            _progress = progress;
        }

        public XKTModel Extract()
        {
            var model = new XKTModel
            {
                ModelId             = _doc.Title,
                ProjectId           = _doc.ProjectInformation?.Number ?? "",
                Author              = _doc.ProjectInformation?.Author ?? "",
                CreatedAt           = DateTime.UtcNow.ToString("o"),
                CreatingApplication = "XKT Conversion Revit Plugin v1.0",
                Schema              = "Revit"
            };

            // Collect elements with geometry
            var collector = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();

            var options = new Options
            {
                DetailLevel             = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false,
                ComputeReferences       = false
            };

            var elements = collector
                .Cast<Element>()
                .Where(e => e.Category != null && HasGeometry(e, options))
                .ToList();

            _progress.Begin(elements.Count + 1);

            // Root meta-object (project)
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id   = "project",
                Name = _doc.Title,
                Type = "Project"
            });

            // Track processed categories/families for hierarchy
            var categoryNodes = new Dictionary<string, string>(); // categoryName → metaId
            var familyNodes   = new Dictionary<string, string>(); // familyName → metaId

            foreach (var element in elements)
            {
                _progress.Advance(element.Name);

                try
                {
                    ExtractElement(element, options, model, categoryNodes, familyNodes);
                }
                catch
                {
                    // Skip elements that fail geometry extraction
                }
            }

            _progress.Advance("Finalizing");
            return model;
        }

        // -----------------------------------------------------------------------
        private void ExtractElement(
            Element element,
            Options options,
            XKTModel model,
            Dictionary<string, string> categoryNodes,
            Dictionary<string, string> familyNodes)
        {
            var geomElem = element.get_Geometry(options);
            if (geomElem == null) return;

            // Resolve BIM hierarchy IDs
            string categoryId = EnsureCategoryNode(element, model, categoryNodes);
            string familyId   = EnsureFamilyNode  (element, model, familyNodes, categoryId);

            var entityId = element.UniqueId;

            // Check if this is a FamilyInstance whose symbol geometry we've already seen
            bool isFamilyInstance = element is FamilyInstance;
            ElementId? symbolId   = isFamilyInstance
                ? (element as FamilyInstance)!.Symbol?.Id
                : null;

            int meshBaseIndex = model.Meshes.Count;
            int meshCount     = 0;

            if (symbolId != null && _symbolGeomIndex.TryGetValue(symbolId, out int sharedGeomIdx))
            {
                // Reuse existing geometry with per-instance transform matrix
                var transform = ((FamilyInstance)element).GetTotalTransform();
                var matrix    = TransformToColumnMajor(transform);

                var mesh = new XKTMesh
                {
                    Index         = model.Meshes.Count,
                    GeometryIndex = sharedGeomIdx,
                    Matrix        = matrix
                };
                ApplyElementMaterial(element, mesh);
                model.Meshes.Add(mesh);
                meshCount = 1;
            }
            else
            {
                // Extract geometry fresh
                var solidMeshes = CollectSolidMeshes(geomElem, Transform.Identity);
                if (solidMeshes.Count == 0) return;

                foreach (var (positions, normals, indices, matColor) in solidMeshes)
                {
                    if (positions.Length == 0 || indices.Length == 0) continue;

                    var geom = new XKTGeometry
                    {
                        Index         = model.Geometries.Count,
                        Positions     = positions,
                        Normals       = normals,
                        Indices       = indices,
                        PrimitiveType = 0   // solid triangles
                    };
                    model.Geometries.Add(geom);

                    var mesh = new XKTMesh
                    {
                        Index         = model.Meshes.Count,
                        GeometryIndex = geom.Index
                    };
                    ApplyMaterialColor(matColor, mesh);
                    model.Meshes.Add(mesh);
                    meshCount++;
                }

                // If this is a family instance, register the first geometry as the shared symbol geometry
                if (symbolId != null && meshCount > 0)
                    _symbolGeomIndex[symbolId] = model.Geometries.Count - meshCount;
            }

            if (meshCount == 0) return;

            model.Entities.Add(new XKTEntity
            {
                Id           = entityId,
                MeshBaseIndex = meshBaseIndex,
                MeshCount    = meshCount
            });

            // Meta-object for this element (leaf node under its family)
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id     = entityId,
                Name   = element.Name,
                Type   = element.Category?.Name ?? "Element",
                Parent = familyId
            });
        }

        // -----------------------------------------------------------------------
        // Geometry collection
        // -----------------------------------------------------------------------
        private static List<(float[] positions, float[]? normals, uint[] indices, Color? matColor)>
            CollectSolidMeshes(GeometryElement geomElem, Transform parentTransform)
        {
            var result = new List<(float[], float[]?, uint[], Color?)>();

            foreach (GeometryObject obj in geomElem)
            {
                switch (obj)
                {
                    case Solid solid when solid.Faces.Size > 0:
                        ExtractSolid(solid, parentTransform, result);
                        break;

                    case GeometryInstance inst:
                        var instGeom = inst.GetInstanceGeometry(parentTransform);
                        if (instGeom != null)
                            result.AddRange(CollectSolidMeshes(instGeom, Transform.Identity));
                        break;

                    case GeometryElement nested:
                        result.AddRange(CollectSolidMeshes(nested, parentTransform));
                        break;
                }
            }
            return result;
        }

        private static void ExtractSolid(
            Solid solid,
            Transform xform,
            List<(float[], float[]?, uint[], Color?)> result)
        {
            // Tessellate each face separately and merge per material
            var groupedByMat = new Dictionary<ElementId, (
                List<float> pos, List<float> nrm, List<uint> idx, Color? clr)>();

            int faceCount = 0;
            foreach (Face face in solid.Faces)
            {
                faceCount++;
                var mesh = face.Triangulate(0.5);
                if (mesh == null || mesh.NumTriangles == 0) continue;

                var matId = face.MaterialElementId ?? ElementId.InvalidElementId;
                if (!groupedByMat.TryGetValue(matId, out var bucket))
                {
                    bucket = (new List<float>(), new List<float>(), new List<uint>(), null);
                    groupedByMat[matId] = bucket;
                }

                int baseIdx = bucket.pos.Count / 3;

                for (int vi = 0; vi < mesh.Vertices.Count; vi++)
                {
                    var pt  = xform.IsIdentity ? mesh.Vertices[vi] : xform.OfPoint(mesh.Vertices[vi]);
                    var nrm = face.ComputeNormal(new UV(0.5, 0.5));
                    if (!xform.IsIdentity) nrm = xform.OfVector(nrm).Normalize();

                    bucket.pos.Add((float)pt.X);
                    bucket.pos.Add((float)pt.Y);
                    bucket.pos.Add((float)pt.Z);
                    bucket.nrm.Add((float)nrm.X);
                    bucket.nrm.Add((float)nrm.Y);
                    bucket.nrm.Add((float)nrm.Z);
                }

                for (int ti = 0; ti < mesh.NumTriangles; ti++)
                {
                    var tri = mesh.get_Triangle(ti);
                    bucket.idx.Add((uint)(baseIdx + tri.get_Index(0)));
                    bucket.idx.Add((uint)(baseIdx + tri.get_Index(1)));
                    bucket.idx.Add((uint)(baseIdx + tri.get_Index(2)));
                }

                // Grab material color for first entry
                if (bucket.clr == null && face.MaterialElementId != null
                    && face.MaterialElementId != ElementId.InvalidElementId)
                {
                    // Color will be resolved in ApplyMaterialColor
                }
            }

            foreach (var (matId, bucket) in groupedByMat)
            {
                if (bucket.pos.Count == 0) continue;
                result.Add((
                    bucket.pos.ToArray(),
                    bucket.nrm.ToArray(),
                    bucket.idx.ToArray(),
                    null));
            }
        }

        // -----------------------------------------------------------------------
        // Material helpers
        // -----------------------------------------------------------------------
        private void ApplyElementMaterial(Element element, XKTMesh mesh)
        {
            var matIds = element.GetMaterialIds(false);
            if (matIds.Count > 0)
            {
                var mat = _doc.GetElement(matIds.First()) as Material;
                if (mat != null)
                {
                    ApplyMaterialColor(mat.Color, mesh);
                    mesh.Opacity = (byte)(255 - (int)(mat.Transparency / 100.0 * 255));
                    return;
                }
            }

            // Fallback: use category colour
            if (element.Category?.Material is Material catMat)
                ApplyMaterialColor(catMat.Color, mesh);
        }

        private static void ApplyMaterialColor(Color? color, XKTMesh mesh)
        {
            if (color == null || !color.IsValid) return;
            mesh.ColorR = color.Red;
            mesh.ColorG = color.Green;
            mesh.ColorB = color.Blue;
        }

        // -----------------------------------------------------------------------
        // BIM hierarchy helpers
        // -----------------------------------------------------------------------
        private static string EnsureCategoryNode(
            Element element,
            XKTModel model,
            Dictionary<string, string> categoryNodes)
        {
            var catName = element.Category?.Name ?? "Uncategorized";
            if (categoryNodes.TryGetValue(catName, out var existing)) return existing;

            var id = $"category_{catName.Replace(" ", "_")}";
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id     = id,
                Name   = catName,
                Type   = "Category",
                Parent = "project"
            });
            categoryNodes[catName] = id;
            return id;
        }

        private static string EnsureFamilyNode(
            Element element,
            XKTModel model,
            Dictionary<string, string> familyNodes,
            string parentId)
        {
            string familyName;
            if (element is FamilyInstance fi && fi.Symbol?.Family != null)
                familyName = fi.Symbol.Family.Name;
            else
                familyName = element.Category?.Name ?? "Generic";

            var key = $"{parentId}_{familyName}";
            if (familyNodes.TryGetValue(key, out var existing)) return existing;

            var id = $"family_{Guid.NewGuid():N}";
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id     = id,
                Name   = familyName,
                Type   = "Family",
                Parent = parentId
            });
            familyNodes[key] = id;
            return id;
        }

        // -----------------------------------------------------------------------
        // Revit Transform → column-major float[16]
        // -----------------------------------------------------------------------
        private static float[] TransformToColumnMajor(Transform t)
        {
            // Revit BasisX/Y/Z are column vectors of the rotation part
            return new float[]
            {
                (float)t.BasisX.X, (float)t.BasisX.Y, (float)t.BasisX.Z, 0f,
                (float)t.BasisY.X, (float)t.BasisY.Y, (float)t.BasisY.Z, 0f,
                (float)t.BasisZ.X, (float)t.BasisZ.Y, (float)t.BasisZ.Z, 0f,
                (float)t.Origin.X, (float)t.Origin.Y, (float)t.Origin.Z, 1f
            };
        }

        // -----------------------------------------------------------------------
        private static bool HasGeometry(Element element, Options options)
        {
            try
            {
                var g = element.get_Geometry(options);
                if (g == null) return false;
                foreach (var _ in g) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
