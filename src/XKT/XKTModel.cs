using System.Collections.Generic;

namespace XKTConversionRevitPlugin.XKT
{
    /// <summary>
    /// In-memory representation of a model to be serialized as XKT v12.
    /// </summary>
    public sealed class XKTModel
    {
        // Model-level metadata
        public string ModelId            { get; set; } = "model";
        public string ProjectId          { get; set; } = "";
        public string Author             { get; set; } = "Revit XKT Plugin";
        public string CreatedAt          { get; set; } = "";
        public string CreatingApplication{ get; set; } = "XKT Conversion Revit Plugin v1.0";
        public string Schema             { get; set; } = "Revit";

        /// <summary>All unique geometry primitives (shared across meshes).</summary>
        public List<XKTGeometry> Geometries { get; } = new();

        /// <summary>All mesh instances (geometry + material + transform).</summary>
        public List<XKTMesh> Meshes { get; } = new();

        /// <summary>All entities (BIM objects), each owning one or more meshes.</summary>
        public List<XKTEntity> Entities { get; } = new();

        /// <summary>Meta-objects for the object hierarchy (used by metadata JSON).</summary>
        public List<XKTMetaObject> MetaObjects { get; } = new();

        /// <summary>Overall model AABB (computed during write).</summary>
        public double[] ModelAABB { get; set; } = new double[6];
    }

    /// <summary>Unique geometry (vertex data, indices). May be referenced by multiple meshes.</summary>
    public sealed class XKTGeometry
    {
        public int Index { get; set; }

        /// <summary>Raw float positions [x0,y0,z0, x1,y1,z1, …]</summary>
        public float[] Positions { get; set; } = System.Array.Empty<float>();

        /// <summary>Float normals [nx,ny,nz, …] – optional.</summary>
        public float[]? Normals { get; set; }

        /// <summary>Per-vertex RGBA colors [r,g,b,a, …] in 0-1 range – optional.</summary>
        public float[]? Colors { get; set; }

        /// <summary>Triangle indices.</summary>
        public uint[] Indices { get; set; } = System.Array.Empty<uint>();

        /// <summary>Edge indices (pairs) – optional.</summary>
        public uint[]? EdgeIndices { get; set; }

        /// <summary>0=solid-triangles, 1=surface-triangles, 2=points, 3=lines</summary>
        public byte PrimitiveType { get; set; } = 0;

        /// <summary>True if this geometry is referenced by more than one mesh (share-optimised path).</summary>
        public bool IsReused { get; set; }
    }

    /// <summary>A mesh instance – pairs a geometry with a material and a per-instance transform.</summary>
    public sealed class XKTMesh
    {
        public int Index { get; set; }

        /// <summary>Index into XKTModel.Geometries.</summary>
        public int GeometryIndex { get; set; }

        /// <summary>Column-major 4×4 world transform (only populated when geometry is reused).</summary>
        public float[]? Matrix { get; set; }

        // Material – stored as 0-255
        public byte ColorR     { get; set; } = 200;
        public byte ColorG     { get; set; } = 200;
        public byte ColorB     { get; set; } = 200;
        public byte Opacity    { get; set; } = 255;
        public byte Metallic   { get; set; } = 0;
        public byte Roughness  { get; set; } = 128;
    }

    /// <summary>A BIM entity – corresponds to a Revit Element; owns one or more meshes.</summary>
    public sealed class XKTEntity
    {
        public string Id         { get; set; } = "";
        public int MeshBaseIndex { get; set; }
        public int MeshCount     { get; set; }
    }

    /// <summary>Node in the BIM metadata hierarchy (for the metadata JSON sidecar).</summary>
    public sealed class XKTMetaObject
    {
        public string Id       { get; set; } = "";
        public string Name     { get; set; } = "";
        public string Type     { get; set; } = "";
        public string? Parent  { get; set; }

        /// <summary>Arbitrary BIM properties (Revit parameter name → value string).</summary>
        public Dictionary<string, string> Properties { get; } = new();
    }
}
