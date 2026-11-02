using System.Numerics;

namespace Stationary.Ftms.Dashboard.Scene;

internal enum SceneMesh
{
    Cube,
    Sphere,
    Cylinder,
    Quad,
}

internal readonly record struct SceneVertex(Vector3 Position, Vector3 Normal);

internal readonly record struct SceneMeshRange(uint FirstIndex, uint IndexCount, int VertexOffset);

// Unit-sized low-poly meshes packed into one vertex and index buffer: a cube from -0.5 to 0.5, a sphere of radius 0.5,
// a cylinder of radius 0.5 along the X axis from -0.5 to 0.5, and a quad from -0.5 to 0.5 in the XY plane facing +Z
// (glow halos and light streaks shade it procedurally from its local position).
internal static class SceneMeshes
{
    public static (SceneVertex[] Vertices, ushort[] Indices, SceneMeshRange[] Ranges) Build()
    {
        var vertices = new List<SceneVertex>();
        var indices = new List<ushort>();
        var ranges = new SceneMeshRange[4];
        ranges[(int)SceneMesh.Cube] = Append(vertices, indices, AddCube);
        ranges[(int)SceneMesh.Sphere] = Append(vertices, indices, AddSphere);
        ranges[(int)SceneMesh.Cylinder] = Append(vertices, indices, AddCylinder);
        ranges[(int)SceneMesh.Quad] = Append(vertices, indices, AddQuad);
        return ([.. vertices], [.. indices], ranges);
    }

    private static SceneMeshRange Append(List<SceneVertex> vertices, List<ushort> indices, Action<List<SceneVertex>, List<ushort>> build)
    {
        var meshVertices = new List<SceneVertex>();
        var meshIndices = new List<ushort>();
        build(meshVertices, meshIndices);
        var range = new SceneMeshRange((uint)indices.Count, (uint)meshIndices.Count, vertices.Count);
        vertices.AddRange(meshVertices);
        indices.AddRange(meshIndices);
        return range;
    }

    private static void AddCube(List<SceneVertex> vertices, List<ushort> indices)
    {
        ReadOnlySpan<Vector3> normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (var normal in normals)
        {
            var tangent = float.Abs(normal.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var bitangent = Vector3.Cross(normal, tangent);
            var start = (ushort)vertices.Count;
            var center = normal * 0.5f;
            vertices.Add(new(center - (tangent * 0.5f) - (bitangent * 0.5f), normal));
            vertices.Add(new(center + (tangent * 0.5f) - (bitangent * 0.5f), normal));
            vertices.Add(new(center + (tangent * 0.5f) + (bitangent * 0.5f), normal));
            vertices.Add(new(center - (tangent * 0.5f) + (bitangent * 0.5f), normal));
            indices.AddRange([start, (ushort)(start + 1), (ushort)(start + 2), start, (ushort)(start + 2), (ushort)(start + 3)]);
        }
    }

    private static void AddQuad(List<SceneVertex> vertices, List<ushort> indices)
    {
        vertices.AddRange(
        [
            new(new(-0.5f, -0.5f, 0f), Vector3.UnitZ),
            new(new(0.5f, -0.5f, 0f), Vector3.UnitZ),
            new(new(0.5f, 0.5f, 0f), Vector3.UnitZ),
            new(new(-0.5f, 0.5f, 0f), Vector3.UnitZ),
        ]);
        indices.AddRange([0, 1, 2, 0, 2, 3]);
    }

    private static void AddSphere(List<SceneVertex> vertices, List<ushort> indices)
    {
        const int Rings = 10;
        const int Segments = 16;
        for (var ring = 0; ring <= Rings; ring++)
        {
            var polar = float.Pi * ring / Rings;
            for (var segment = 0; segment <= Segments; segment++)
            {
                var azimuth = 2f * float.Pi * segment / Segments;
                var normal = new Vector3(float.Sin(polar) * float.Cos(azimuth), float.Cos(polar), float.Sin(polar) * float.Sin(azimuth));
                vertices.Add(new(normal * 0.5f, normal));
            }
        }

        for (var ring = 0; ring < Rings; ring++)
        {
            for (var segment = 0; segment < Segments; segment++)
            {
                var a = (ushort)((ring * (Segments + 1)) + segment);
                var b = (ushort)(a + Segments + 1);
                indices.AddRange([a, b, (ushort)(a + 1), (ushort)(a + 1), b, (ushort)(b + 1)]);
            }
        }
    }

    private static void AddCylinder(List<SceneVertex> vertices, List<ushort> indices)
    {
        const int Segments = 20;
        for (var segment = 0; segment <= Segments; segment++)
        {
            var angle = 2f * float.Pi * segment / Segments;
            var normal = new Vector3(0f, float.Cos(angle), float.Sin(angle));
            vertices.Add(new(new Vector3(-0.5f, 0f, 0f) + (normal * 0.5f), normal));
            vertices.Add(new(new Vector3(0.5f, 0f, 0f) + (normal * 0.5f), normal));
        }

        for (var segment = 0; segment < Segments; segment++)
        {
            var a = (ushort)(segment * 2);
            indices.AddRange([a, (ushort)(a + 1), (ushort)(a + 2), (ushort)(a + 1), (ushort)(a + 3), (ushort)(a + 2)]);
        }

        foreach (var side in (ReadOnlySpan<float>)[-0.5f, 0.5f])
        {
            var normal = new Vector3(side * 2f, 0f, 0f);
            var center = (ushort)vertices.Count;
            vertices.Add(new(new Vector3(side, 0f, 0f), normal));
            for (var segment = 0; segment <= Segments; segment++)
            {
                var angle = 2f * float.Pi * segment / Segments;
                vertices.Add(new(new Vector3(side, float.Cos(angle) * 0.5f, float.Sin(angle) * 0.5f), normal));
            }

            for (var segment = 0; segment < Segments; segment++)
            {
                indices.AddRange([center, (ushort)(center + 1 + segment), (ushort)(center + 2 + segment)]);
            }
        }
    }
}