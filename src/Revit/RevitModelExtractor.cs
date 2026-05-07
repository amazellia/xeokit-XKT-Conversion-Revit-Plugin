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
    /// Each element's geometry is extracted in world (model) coordinates so positions
    /// can be packed directly into a single global tile with no per-mesh matrices.
    /// </summary>
    public sealed class RevitModelExtractor
    {
        private readonly Document        _doc;
        private readonly ExportProgress  _progress;

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

            // ── Build geometry Options ──────────────────────────────────────────
            // Passing a 3-D view is critical: without one Revit uses the active
            // view's visibility settings, which can silently hide categories or
            // apply section-box clipping that removes geometry.
            var options = BuildOptions();

            // ── Collect all model elements that have 3-D geometry ──────────────
            var elements = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent()
                .Cast<Element>()
                .Where(e => e.Category != null && HasGeometry(e, options))
                .ToList();

            _progress.Begin(elements.Count + 1);

            // Root node in the metadata hierarchy
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id   = "project",
                Name = _doc.Title,
                Type = "Project"
            });

            var categoryNodes = new Dictionary<string, string>();
            var familyNodes   = new Dictionary<string, string>();

            foreach (var element in elements)
            {
                _progress.Advance(element.Name);
                try
                {
                    ExtractElement(element, options, model, categoryNodes, familyNodes);
                }
                catch
                {
                    // Skip any element whose geometry fails – don't abort the export
                }
            }

            _progress.Advance("Finalizing");
            return model;
        }

        // ── Options ────────────────────────────────────────────────────────────

        private Options BuildOptions()
        {
            // Prefer an existing 3-D view so that Revit honours its visibility
            // settings and returns geometry in model (world) coordinates.
            var view3D = new FilteredElementCollector(_doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            var options = new Options
            {
                ComputeReferences        = false,
                IncludeNonVisibleObjects = false
            };

            if (view3D != null)
                options.View = view3D;               // detail level comes from the view
            else
                options.DetailLevel = ViewDetailLevel.Fine; // fallback when no 3D view exists

            return options;
        }

        // ── Per-element extraction ─────────────────────────────────────────────

        private void ExtractElement(
            Element element,
            Options options,
            XKTModel model,
            Dictionary<string, string> categoryNodes,
            Dictionary<string, string> familyNodes)
        {
            var geomElem = element.get_Geometry(options);
            if (geomElem == null) return;

            // Collect all triangulated solid meshes in WORLD coordinates
            var solidMeshes = CollectSolidMeshes(geomElem);
            if (solidMeshes.Count == 0) return;

            // BIM hierarchy nodes (created lazily, shared across elements)
            string categoryId = EnsureCategoryNode(element, model, categoryNodes);
            string familyId   = EnsureFamilyNode(element, model, familyNodes, categoryId);

            int meshBaseIndex = model.Meshes.Count;
            int meshCount     = 0;

            foreach (var (positions, normals, indices, color) in solidMeshes)
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
                ApplyColor(color, mesh);
                ApplyElementMaterial(element, mesh);
                model.Meshes.Add(mesh);
                meshCount++;
            }

            if (meshCount == 0) return;

            model.Entities.Add(new XKTEntity
            {
                Id            = element.UniqueId,
                MeshBaseIndex = meshBaseIndex,
                MeshCount     = meshCount
            });

            model.MetaObjects.Add(new XKTMetaObject
            {
                Id     = element.UniqueId,
                Name   = string.IsNullOrWhiteSpace(element.Name) ? element.Id.ToString() : element.Name,
                Type   = element.Category?.Name ?? "Element",
                Parent = familyId
            });
        }

        // ── Geometry collection ────────────────────────────────────────────────

        /// <summary>
        /// Recursively collects triangulated solid surfaces from a GeometryElement.
        /// All returned positions are in Revit MODEL (world) coordinates.
        /// </summary>
        private static List<(float[] positions, float[] normals, uint[] indices, Color? color)>
            CollectSolidMeshes(GeometryElement geomElem)
        {
            var result = new List<(float[], float[], uint[], Color?)>();

            foreach (GeometryObject obj in geomElem)
            {
                switch (obj)
                {
                    case Solid solid when solid.Faces.Size > 0 && solid.Volume > 0:
                        ExtractSolid(solid, result);
                        break;

                    case GeometryInstance inst:
                        // GetInstanceGeometry() with NO arguments returns geometry
                        // transformed into MODEL coordinates using the instance's own
                        // transform. Passing Transform.Identity would OVERRIDE that
                        // transform and return local symbol-space geometry instead.
                        var instGeom = inst.GetInstanceGeometry();
                        if (instGeom != null)
                            result.AddRange(CollectSolidMeshes(instGeom));
                        break;

                    case GeometryElement nested:
                        result.AddRange(CollectSolidMeshes(nested));
                        break;
                }
            }

            return result;
        }

        private static void ExtractSolid(
            Solid solid,
            List<(float[], float[], uint[], Color?)> result)
        {
            // Group faces by material so each material produces one mesh
            var groups = new Dictionary<ElementId,
                (List<float> pos, List<float> nrm, List<uint> idx)>();

            foreach (Face face in solid.Faces)
            {
                var mesh = face.Triangulate(0.5);
                if (mesh == null || mesh.NumTriangles == 0) continue;

                var matId = face.MaterialElementId ?? ElementId.InvalidElementId;
                if (!groups.TryGetValue(matId, out var g))
                {
                    g = (new List<float>(), new List<float>(), new List<uint>());
                    groups[matId] = g;
                }

                int baseVertex = g.pos.Count / 3;

                // Positions and per-vertex normals
                for (int vi = 0; vi < mesh.Vertices.Count; vi++)
                {
                    var pt = mesh.Vertices[vi];
                    g.pos.Add((float)pt.X);
                    g.pos.Add((float)pt.Y);
                    g.pos.Add((float)pt.Z);

                    // Compute the face normal at this vertex's UV, falling back to
                    // the face centre normal if the UV evaluation fails
                    XYZ n = ComputeNormalAt(face, mesh, vi);
                    g.nrm.Add((float)n.X);
                    g.nrm.Add((float)n.Y);
                    g.nrm.Add((float)n.Z);
                }

                // Triangle indices (relative to this face's base vertex offset)
                for (int ti = 0; ti < mesh.NumTriangles; ti++)
                {
                    var tri = mesh.get_Triangle(ti);
                    g.idx.Add((uint)(baseVertex + tri.get_Index(0)));
                    g.idx.Add((uint)(baseVertex + tri.get_Index(1)));
                    g.idx.Add((uint)(baseVertex + tri.get_Index(2)));
                }
            }

            foreach (var (matId, g) in groups)
            {
                if (g.pos.Count == 0 || g.idx.Count == 0) continue;
                result.Add((g.pos.ToArray(), g.nrm.ToArray(), g.idx.ToArray(), null));
            }
        }

        // Compute the surface normal at a specific mesh vertex, falling back to
        // the face-centre normal if the UV method throws or returns zero.
        private static XYZ ComputeNormalAt(Face face, Autodesk.Revit.DB.Mesh mesh, int vertexIndex)
        {
            try
            {
                // Use UV coordinates of the vertex when available
                if (mesh.HasUVCoords())
                {
                    var uv = mesh.GetUV(vertexIndex);
                    var n  = face.ComputeNormal(uv);
                    if (n != null && n.GetLength() > 1e-6) return n.Normalize();
                }
            }
            catch { /* fall through */ }

            try
            {
                var n = face.ComputeNormal(new UV(0.5, 0.5));
                if (n != null && n.GetLength() > 1e-6) return n.Normalize();
            }
            catch { /* fall through */ }

            return XYZ.BasisZ; // last resort
        }

        // ── Material helpers ───────────────────────────────────────────────────

        private void ApplyElementMaterial(Element element, XKTMesh mesh)
        {
            // Try the element's own material ids first
            ICollection<ElementId>? matIds = null;
            try { matIds = element.GetMaterialIds(false); } catch { }

            if (matIds != null && matIds.Count > 0)
            {
                if (_doc.GetElement(matIds.First()) is Material mat)
                {
                    ApplyColor(mat.Color, mesh);
                    mesh.Opacity = (byte)Math.Round(
                        (1.0 - mat.Transparency / 100.0) * 255.0);
                    return;
                }
            }

            // Fall back to the category's material
            if (element.Category?.Material is Material catMat)
                ApplyColor(catMat.Color, mesh);
        }

        private static void ApplyColor(Color? color, XKTMesh mesh)
        {
            if (color == null || !color.IsValid) return;
            mesh.ColorR = color.Red;
            mesh.ColorG = color.Green;
            mesh.ColorB = color.Blue;
        }

        // ── BIM hierarchy helpers ──────────────────────────────────────────────

        private static string EnsureCategoryNode(
            Element element,
            XKTModel model,
            Dictionary<string, string> nodes)
        {
            var name = element.Category?.Name ?? "Uncategorized";
            if (nodes.TryGetValue(name, out var id)) return id;

            id = "cat_" + name.Replace(" ", "_");
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id = id, Name = name, Type = "Category", Parent = "project"
            });
            nodes[name] = id;
            return id;
        }

        private static string EnsureFamilyNode(
            Element element,
            XKTModel model,
            Dictionary<string, string> nodes,
            string parentId)
        {
            var name = element is FamilyInstance fi && fi.Symbol?.Family != null
                ? fi.Symbol.Family.Name
                : element.Category?.Name ?? "Generic";

            var key = parentId + "|" + name;
            if (nodes.TryGetValue(key, out var id)) return id;

            id = "fam_" + Guid.NewGuid().ToString("N");
            model.MetaObjects.Add(new XKTMetaObject
            {
                Id = id, Name = name, Type = "Family", Parent = parentId
            });
            nodes[key] = id;
            return id;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool HasGeometry(Element element, Options options)
        {
            try
            {
                var g = element.get_Geometry(options);
                if (g == null) return false;
                foreach (var _ in g) return true;
            }
            catch { }
            return false;
        }
    }
}
