namespace NML.Core.Skins;

/// <summary>
/// A single rectangular face of a skin cube, defined by 4 3D corners and the UV rectangle
/// on the 64×64 skin texture that paints it. The preview renderer projects each face to 2D
/// and paints it from the skin texture.
/// </summary>
public readonly struct SkinFace
{
    public SkinFace((double X, double Y, double Z) p0, (double X, double Y, double Z) p1,
                    (double X, double Y, double Z) p2, (double X, double Y, double Z) p3,
                    double u0, double v0, double u1, double v1)
    {
        P0 = p0; P1 = p1; P2 = p2; P3 = p3;
        U0 = u0; V0 = v0; U1 = u1; V1 = v1;
    }

    public (double X, double Y, double Z) P0 { get; }
    public (double X, double Y, double Z) P1 { get; }
    public (double X, double Y, double Z) P2 { get; }
    public (double X, double Y, double Z) P3 { get; }
    public double U0 { get; }
    public double V0 { get; }
    public double U1 { get; }
    public double V1 { get; }
}

/// <summary>
/// The Minecraft player skin as a set of textured cuboids (head, body, arms, legs). Provides
/// the face list (6 per cuboid) with the canonical skin-texture UV coordinates, centered at
/// the origin in a right-handed coordinate system. The preview rotates the whole model and
/// projects each visible face.
/// </summary>
public static class SkinModel
{
    // Minecraft skin is 64×64; modern skins (1.8+) use the full resolution for outer layers.
    private const double TexW = 64.0;
    private const double TexH = 64.0;

    /// <summary>
    /// Build the full set of faces for a player skin. Coordinates are in "pixels" centered so
    /// the model fits roughly in a ±8 × ±32 box (head ~8px wide, body 8×12, etc.).
    /// </summary>
    public static IReadOnlyList<SkinFace> BuildFaces()
    {
        var faces = new List<SkinFace>();

        // HEAD — 8×8×8 cuboid at top of body. Skin UV: head starts around (8,8).
        AddCuboidFaces(faces,
            minX: -4, maxX: 4, minY: 24, maxY: 32, minZ: -4, maxZ: 4,
            // Standard Minecraft head UV layout on the 64×64 texture.
            topU: (8, 0, 16, 8), bottomU: (16, 0, 24, 8),
            frontU: (8, 8, 16, 16), backU: (24, 8, 32, 16),
            leftU: (0, 8, 8, 16), rightU: (16, 8, 24, 16));

        // BODY — 8×12×4 cuboid below the head.
        AddCuboidFaces(faces,
            minX: -4, maxX: 4, minY: 12, maxY: 24, minZ: -2, maxZ: 2,
            topU: (20, 16, 28, 20), bottomU: (28, 16, 36, 20),
            frontU: (20, 20, 28, 32), backU: (32, 20, 40, 32),
            leftU: (16, 20, 20, 32), rightU: (28, 20, 32, 32));

        // RIGHT ARM (player's right, viewer's left) — 4×12×4 cuboid.
        AddCuboidFaces(faces,
            minX: -8, maxX: -4, minY: 12, maxY: 24, minZ: -2, maxZ: 2,
            topU: (44, 16, 48, 20), bottomU: (48, 16, 52, 20),
            frontU: (44, 20, 48, 32), backU: (52, 20, 56, 32),
            leftU: (40, 20, 44, 32), rightU: (48, 20, 52, 32));

        // LEFT ARM (player's left, viewer's right) — 4×12×4 cuboid.
        AddCuboidFaces(faces,
            minX: 4, maxX: 8, minY: 12, maxY: 24, minZ: -2, maxZ: 2,
            topU: (36, 48, 40, 52), bottomU: (40, 48, 44, 52),
            frontU: (36, 52, 40, 64), backU: (44, 52, 48, 64),
            leftU: (32, 52, 36, 64), rightU: (40, 52, 44, 64));

        // RIGHT LEG — 4×12×4 cuboid at the bottom.
        AddCuboidFaces(faces,
            minX: -4, maxX: 0, minY: 0, maxY: 12, minZ: -2, maxZ: 2,
            topU: (4, 16, 8, 20), bottomU: (8, 16, 12, 20),
            frontU: (4, 20, 8, 32), backU: (12, 20, 16, 32),
            leftU: (0, 20, 4, 32), rightU: (8, 20, 12, 32));

        // LEFT LEG — 4×12×4 cuboid.
        AddCuboidFaces(faces,
            minX: 0, maxX: 4, minY: 0, maxY: 12, minZ: -2, maxZ: 2,
            topU: (20, 48, 24, 52), bottomU: (24, 48, 28, 52),
            frontU: (20, 52, 24, 64), backU: (28, 52, 32, 64),
            leftU: (16, 52, 20, 64), rightU: (24, 52, 28, 64));

        return faces;
    }

    /// <summary>
    /// Append the 6 faces of an axis-aligned cuboid to <paramref name="faces"/>, each carrying
    /// its UV rectangle (in texture pixels) from <paramref name="*U"/> tuples of (u0,v0,u1,v1).
    /// </summary>
    private static void AddCuboidFaces(
        List<SkinFace> faces,
        double minX, double maxX, double minY, double maxY, double minZ, double maxZ,
        (double, double, double, double) topU, (double, double, double, double) bottomU,
        (double, double, double, double) frontU, (double, double, double, double) backU,
        (double, double, double, double) leftU, (double, double, double, double) rightU)
    {
        // Top (+Y), Bottom (-Y), Front (+Z), Back (-Z), Left (-X), Right (+X).
        AddQuad(faces, // top
            (minX, maxY, minZ), (maxX, maxY, minZ), (maxX, maxY, maxZ), (minX, maxY, maxZ), topU);
        AddQuad(faces, // bottom
            (minX, minY, maxZ), (maxX, minY, maxZ), (maxX, minY, minZ), (minX, minY, minZ), bottomU);
        AddQuad(faces, // front (+Z)
            (minX, minY, maxZ), (maxX, minY, maxZ), (maxX, maxY, maxZ), (minX, maxY, maxZ), frontU);
        AddQuad(faces, // back (-Z)
            (maxX, minY, minZ), (minX, minY, minZ), (minX, maxY, minZ), (maxX, maxY, minZ), backU);
        AddQuad(faces, // left (-X)
            (minX, minY, minZ), (minX, minY, maxZ), (minX, maxY, maxZ), (minX, maxY, minZ), leftU);
        AddQuad(faces, // right (+X)
            (maxX, minY, maxZ), (maxX, minY, minZ), (maxX, maxY, minZ), (maxX, maxY, maxZ), rightU);
    }

    private static void AddQuad(List<SkinFace> faces,
        (double, double, double) p0, (double, double, double) p1,
        (double, double, double) p2, (double, double, double) p3,
        (double u0, double v0, double u1, double v1) uv)
    {
        // Normalize UV from texture pixels to 0..1.
        faces.Add(new SkinFace(p0, p1, p2, p3,
            uv.u0 / TexW, uv.v0 / TexH, uv.u1 / TexW, uv.v1 / TexH));
    }
}
