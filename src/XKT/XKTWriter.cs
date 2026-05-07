using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XKTConversionRevitPlugin.XKT
{
    /// <summary>
    /// Writes a model to the xeokit XKT v12 binary format.
    ///
    /// Format layout (all little-endian):
    ///   [0-3]  Uint32  magic = (1 << 31) | 12   (compressed flag | version)
    ///   [4-7]  Uint32  numArrays = 29
    ///   [8 … 8+(29*4)-1]  Uint32[29]  deflated byte-length of each array
    ///   [then…]  deflated payloads concatenated in the order listed below
    ///
    /// Array order (matches xeokit-convert src/XKTModel/writeXKTModelToArrayBuffer.js):
    ///   0  metadata                   Uint8  (UTF-8 JSON)
    ///   1  textureData                Uint8
    ///   2  eachTextureDataPortion     Uint32
    ///   3  eachTextureAttributes      Uint16
    ///   4  positions                  Uint16  (quantised)
    ///   5  normals                    Int8    (oct-encoded)
    ///   6  colors                     Uint8
    ///   7  uvs                        Float32
    ///   8  indices                    Uint32
    ///   9  edgeIndices                Uint32
    ///  10  eachTextureSetTextures     Int32
    ///  11  matrices                   Float32  (reused-geometry matrices)
    ///  12  reusedGeometriesDecodeMatrix Float32 (16 floats)
    ///  13  eachGeometryPrimitiveType  Uint8
    ///  14  eachGeometryAxisLabel      Uint8   (UTF-8 JSON – empty array "[]")
    ///  15  eachGeometryPositionsPortion  Uint32
    ///  16  eachGeometryNormalsPortion    Uint32
    ///  17  eachGeometryColorsPortion     Uint32
    ///  18  eachGeometryUVsPortion        Uint32
    ///  19  eachGeometryIndicesPortion    Uint32
    ///  20  eachGeometryEdgeIndicesPortion Uint32
    ///  21  eachMeshGeometriesPortion   Uint32
    ///  22  eachMeshMatricesPortion     Uint32
    ///  23  eachMeshTextureSet          Int32
    ///  24  eachMeshMaterialAttributes  Uint8   (6 bytes per mesh)
    ///  25  eachEntityId               Uint8   (UTF-8 JSON array of strings)
    ///  26  eachEntityMeshesPortion    Uint32
    ///  27  eachTileAABB               Float64  (6 doubles per tile)
    ///  28  eachTileEntitiesPortion    Uint32
    /// </summary>
    public static class XKTWriter
    {
        private const int XKT_VERSION = 12;
        private const int NUM_ARRAYS  = 29;

        public static void Write(XKTModel model, string outputPath)
        {
            var arrays = BuildArrays(model);

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

            // Header
            bw.Write((uint)((1u << 31) | XKT_VERSION));
            bw.Write((uint)NUM_ARRAYS);

            // Deflate all arrays up-front so we know their compressed sizes
            var deflated = new byte[NUM_ARRAYS][];
            for (int i = 0; i < NUM_ARRAYS; i++)
                deflated[i] = Deflate(arrays[i]);

            // Size table
            for (int i = 0; i < NUM_ARRAYS; i++)
                bw.Write((uint)deflated[i].Length);

            // Payloads
            for (int i = 0; i < NUM_ARRAYS; i++)
                bw.Write(deflated[i]);
        }

        // -------------------------------------------------------------------------
        // Build the 29 raw (pre-compression) byte arrays from the model.
        // -------------------------------------------------------------------------
        private static byte[][] BuildArrays(XKTModel model)
        {
            var geoms    = model.Geometries;
            var meshes   = model.Meshes;
            var entities = model.Entities;

            // ------------------------------------------------------------------
            // Accumulate flat geometry buffers
            // ------------------------------------------------------------------
            var allPositions   = new List<float>();
            var allNormals     = new List<float>();
            var allColors      = new List<float>();
            var allIndices     = new List<uint>();
            var allEdgeIndices = new List<uint>();
            var allMatrices    = new List<float>();  // for reused geometries

            var eachGeomPosPortion  = new List<uint>();
            var eachGeomNrmPortion  = new List<uint>();
            var eachGeomClrPortion  = new List<uint>();
            var eachGeomUVPortion   = new List<uint>();
            var eachGeomIdxPortion  = new List<uint>();
            var eachGeomEdgePortion = new List<uint>();
            var eachGeomPrimType    = new List<byte>();

            // Per-geometry AABB for decode matrices of reused geometries
            var reusedDecodeMatrices = new List<float>(); // 16 per reused geometry

            // Combined global AABB (for the single tile)
            double xmin = double.MaxValue, ymin = double.MaxValue, zmin = double.MaxValue;
            double xmax = double.MinValue, ymax = double.MinValue, zmax = double.MinValue;

            foreach (var geom in geoms)
            {
                // Record portion start indices
                eachGeomPosPortion .Add((uint)allPositions  .Count);
                eachGeomNrmPortion .Add((uint)allNormals    .Count);
                eachGeomClrPortion .Add((uint)allColors     .Count);
                eachGeomUVPortion  .Add(0u);               // no UVs
                eachGeomIdxPortion .Add((uint)allIndices    .Count);
                eachGeomEdgePortion.Add((uint)allEdgeIndices.Count);
                eachGeomPrimType   .Add(geom.PrimitiveType);

                allPositions.AddRange(geom.Positions);

                if (geom.Normals != null)
                    allNormals.AddRange(geom.Normals);

                if (geom.Colors != null)
                    allColors.AddRange(geom.Colors);

                allIndices.AddRange(geom.Indices);

                if (geom.EdgeIndices != null)
                    allEdgeIndices.AddRange(geom.EdgeIndices);

                // Update global AABB
                for (int i = 0; i < geom.Positions.Length; i += 3)
                {
                    double px = geom.Positions[i    ];
                    double py = geom.Positions[i + 1];
                    double pz = geom.Positions[i + 2];
                    if (px < xmin) xmin = px; if (px > xmax) xmax = px;
                    if (py < ymin) ymin = py; if (py > ymax) ymax = py;
                    if (pz < zmin) zmin = pz; if (pz > zmax) zmax = pz;
                }
            }

            // Clamp degenerate case
            if (xmin > xmax) { xmin = 0; xmax = 1; ymin = 0; ymax = 1; zmin = 0; zmax = 1; }

            // ------------------------------------------------------------------
            // Quantise positions (float → uint16 using global AABB)
            // ------------------------------------------------------------------
            const double maxInt = 65535.0;
            double scaleX = (xmax - xmin) > 0 ? maxInt / (xmax - xmin) : 1.0;
            double scaleY = (ymax - ymin) > 0 ? maxInt / (ymax - ymin) : 1.0;
            double scaleZ = (zmax - zmin) > 0 ? maxInt / (zmax - zmin) : 1.0;

            var quantisedPositions = new ushort[allPositions.Count];
            for (int i = 0; i < allPositions.Count; i += 3)
            {
                quantisedPositions[i    ] = (ushort)Math.Max(0, Math.Min(65535, Math.Floor((allPositions[i    ] - xmin) * scaleX)));
                quantisedPositions[i + 1] = (ushort)Math.Max(0, Math.Min(65535, Math.Floor((allPositions[i + 1] - ymin) * scaleY)));
                quantisedPositions[i + 2] = (ushort)Math.Max(0, Math.Min(65535, Math.Floor((allPositions[i + 2] - zmin) * scaleZ)));
            }

            // Decode matrix – column-major 4×4 affine (scale then translate)
            // Row 0: [sx, 0,  0,  0]   Row 1: [0, sy, 0, 0]
            // Row 2: [0,  0,  sz, 0]   Row 3: [tx, ty, tz, 1]
            var decodeMatrix = new float[16]
            {
                (float)(1.0 / scaleX), 0f, 0f, 0f,
                0f, (float)(1.0 / scaleY), 0f, 0f,
                0f, 0f, (float)(1.0 / scaleZ), 0f,
                (float)xmin, (float)ymin, (float)zmin, 1f
            };

            // ------------------------------------------------------------------
            // Oct-encode normals (float → int8)
            // ------------------------------------------------------------------
            var encodedNormals = OctEncodeNormals(allNormals);

            // ------------------------------------------------------------------
            // Colors (float RGBA → uint8)
            // ------------------------------------------------------------------
            var colorsUint8 = new byte[allColors.Count];
            for (int i = 0; i < allColors.Count; i++)
                colorsUint8[i] = (byte)Math.Round(Math.Clamp(allColors[i], 0f, 1f) * 255f);

            // ------------------------------------------------------------------
            // Per-mesh arrays
            // ------------------------------------------------------------------
            var eachMeshGeomPortion   = new List<uint>();
            var eachMeshMatrixPortion = new List<uint>();
            var eachMeshTextureSet    = new List<int>();
            var eachMeshMaterial      = new List<byte>();   // 6 bytes per mesh
            var matrixPortionIndex    = 0u;

            foreach (var mesh in meshes)
            {
                eachMeshGeomPortion  .Add((uint)mesh.GeometryIndex);
                eachMeshTextureSet   .Add(-1);

                if (mesh.Matrix != null)
                {
                    eachMeshMatrixPortion.Add(matrixPortionIndex);
                    allMatrices.AddRange(mesh.Matrix);
                    matrixPortionIndex += 16u;
                }
                else
                {
                    eachMeshMatrixPortion.Add(uint.MaxValue); // sentinel = no matrix
                }

                eachMeshMaterial.Add(mesh.ColorR);
                eachMeshMaterial.Add(mesh.ColorG);
                eachMeshMaterial.Add(mesh.ColorB);
                eachMeshMaterial.Add(mesh.Opacity);
                eachMeshMaterial.Add(mesh.Metallic);
                eachMeshMaterial.Add(mesh.Roughness);
            }

            // ------------------------------------------------------------------
            // Per-entity arrays
            // ------------------------------------------------------------------
            var entityIds            = new List<string>();
            var eachEntityMeshPortion = new List<uint>();

            foreach (var entity in entities)
            {
                entityIds            .Add(entity.Id);
                eachEntityMeshPortion.Add((uint)entity.MeshBaseIndex);
            }

            // ------------------------------------------------------------------
            // Metadata JSON (array 0)
            // ------------------------------------------------------------------
            var metadataJson = BuildMetadataJson(model);

            // ------------------------------------------------------------------
            // eachGeometryAxisLabel – empty JSON array  (array 14)
            // ------------------------------------------------------------------
            var axisLabelJson = Encoding.UTF8.GetBytes("[]");

            // ------------------------------------------------------------------
            // eachEntityId JSON  (array 25)
            // ------------------------------------------------------------------
            var entityIdJson = Encoding.UTF8.GetBytes(
                Newtonsoft.Json.JsonConvert.SerializeObject(entityIds));

            // ------------------------------------------------------------------
            // Single tile covering the whole model
            // ------------------------------------------------------------------
            var tileAABB = new double[] { xmin, ymin, zmin, xmax, ymax, zmax };
            var tileEntitiesPortion = new uint[] { 0u };

            // ------------------------------------------------------------------
            // Assemble the 29 arrays as raw byte arrays
            // ------------------------------------------------------------------
            var arrays = new byte[NUM_ARRAYS][];

            arrays[ 0] = metadataJson;
            arrays[ 1] = Array.Empty<byte>();                  // textureData
            arrays[ 2] = ToUint32Bytes(Array.Empty<uint>());   // eachTextureDataPortion
            arrays[ 3] = ToUint16Bytes(Array.Empty<ushort>()); // eachTextureAttributes
            arrays[ 4] = ToUint16Bytes(quantisedPositions);
            arrays[ 5] = encodedNormals;
            arrays[ 6] = colorsUint8;
            arrays[ 7] = ToFloat32Bytes(Array.Empty<float>()); // uvs
            arrays[ 8] = ToUint32Bytes(allIndices.ToArray());
            arrays[ 9] = ToUint32Bytes(allEdgeIndices.ToArray());
            arrays[10] = ToInt32Bytes(Array.Empty<int>());     // eachTextureSetTextures
            arrays[11] = ToFloat32Bytes(allMatrices.ToArray());
            arrays[12] = ToFloat32Bytes(decodeMatrix);
            arrays[13] = eachGeomPrimType.ToArray();
            arrays[14] = axisLabelJson;
            arrays[15] = ToUint32Bytes(eachGeomPosPortion .ToArray());
            arrays[16] = ToUint32Bytes(eachGeomNrmPortion .ToArray());
            arrays[17] = ToUint32Bytes(eachGeomClrPortion .ToArray());
            arrays[18] = ToUint32Bytes(eachGeomUVPortion  .ToArray());
            arrays[19] = ToUint32Bytes(eachGeomIdxPortion .ToArray());
            arrays[20] = ToUint32Bytes(eachGeomEdgePortion.ToArray());
            arrays[21] = ToUint32Bytes(eachMeshGeomPortion  .ToArray());
            arrays[22] = ToUint32Bytes(eachMeshMatrixPortion.ToArray());
            arrays[23] = ToInt32Bytes(eachMeshTextureSet.ToArray());
            arrays[24] = eachMeshMaterial.ToArray();
            arrays[25] = entityIdJson;
            arrays[26] = ToUint32Bytes(eachEntityMeshPortion.ToArray());
            arrays[27] = ToFloat64Bytes(tileAABB);
            arrays[28] = ToUint32Bytes(tileEntitiesPortion);

            return arrays;
        }

        // -------------------------------------------------------------------------
        // Normal encoding helpers
        // -------------------------------------------------------------------------
        private static byte[] OctEncodeNormals(List<float> normals)
        {
            if (normals.Count == 0) return Array.Empty<byte>();

            int count  = normals.Count / 3;
            var result = new sbyte[count * 2];

            for (int i = 0; i < count; i++)
            {
                float nx = normals[i * 3    ];
                float ny = normals[i * 3 + 1];
                float nz = normals[i * 3 + 2];

                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-6f) { result[i * 2] = 0; result[i * 2 + 1] = 0; continue; }
                nx /= len; ny /= len; nz /= len;

                float denom = MathF.Abs(nx) + MathF.Abs(ny) + MathF.Abs(nz);
                float ox = nx / denom;
                float oy = ny / denom;

                if (nz < 0f)
                {
                    float tmp = ox;
                    ox = (1f - MathF.Abs(oy)) * (ox >= 0f ? 1f : -1f);
                    oy = (1f - MathF.Abs(tmp)) * (oy >= 0f ? 1f : -1f);
                }

                // Best-fit quantisation: try 4 floor/ceil combos, pick smallest error
                result[i * 2    ] = BestInt8(ox);
                result[i * 2 + 1] = BestInt8(oy);
            }

            // Reinterpret sbyte[] as byte[]
            var bytes = new byte[result.Length];
            Buffer.BlockCopy(result, 0, bytes, 0, result.Length);
            return bytes;
        }

        private static sbyte BestInt8(float v)
        {
            float scaled = v * 127f;
            sbyte a = (sbyte)Math.Clamp((int)Math.Floor(scaled), -128, 127);
            sbyte b = (sbyte)Math.Clamp((int)Math.Ceiling(scaled), -128, 127);
            return Math.Abs(a / 127f - v) <= Math.Abs(b / 127f - v) ? a : b;
        }

        // -------------------------------------------------------------------------
        // Typed-array → byte[] converters (little-endian)
        // -------------------------------------------------------------------------
        private static byte[] ToUint16Bytes(ushort[] arr)
        {
            var bytes = new byte[arr.Length * 2];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToUint32Bytes(uint[] arr)
        {
            var bytes = new byte[arr.Length * 4];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToInt32Bytes(int[] arr)
        {
            var bytes = new byte[arr.Length * 4];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToFloat32Bytes(float[] arr)
        {
            var bytes = new byte[arr.Length * 4];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToFloat64Bytes(double[] arr)
        {
            var bytes = new byte[arr.Length * 8];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        // -------------------------------------------------------------------------
        // Deflate compression (zlib, RFC 1950)
        // -------------------------------------------------------------------------
        private static byte[] Deflate(byte[] data)
        {
            if (data.Length == 0) return Array.Empty<byte>();

            using var ms  = new MemoryStream();
            using var def = new DeflaterOutputStream(ms, new Deflater(Deflater.BEST_SPEED));
            def.Write(data, 0, data.Length);
            def.Finish();
            return ms.ToArray();
        }

        // -------------------------------------------------------------------------
        // Inline metadata JSON (array 0 payload)
        // -------------------------------------------------------------------------
        private static byte[] BuildMetadataJson(XKTModel model)
        {
            var meta = new
            {
                id                   = model.ModelId,
                projectId            = model.ProjectId,
                revisionId           = "",
                author               = model.Author,
                createdAt            = model.CreatedAt,
                creatingApplication  = model.CreatingApplication,
                schema               = model.Schema,
                propertySets         = Array.Empty<object>(),
                metaObjects          = BuildMetaObjectsJson(model)
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(meta);
            return Encoding.UTF8.GetBytes(json);
        }

        private static object[] BuildMetaObjectsJson(XKTModel model)
        {
            var list = new object[model.MetaObjects.Count];
            for (int i = 0; i < model.MetaObjects.Count; i++)
            {
                var mo = model.MetaObjects[i];
                list[i] = new
                {
                    id         = mo.Id,
                    name       = mo.Name,
                    type       = mo.Type,
                    parent     = mo.Parent,
                    external   = mo.Properties.Count > 0 ? (object)mo.Properties : null
                };
            }
            return list;
        }
    }
}
