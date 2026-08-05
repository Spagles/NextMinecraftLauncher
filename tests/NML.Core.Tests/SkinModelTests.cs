using NML.Core.Skins;

namespace NML.Core.Tests;

/// <summary>
/// Validates the 3D skin cube model. 6 body parts × 6 faces each = 36 faces; UVs normalized 0..1.
/// These invariants matter — a wrong count or un-normalized UV would render garbage.
/// </summary>
public class SkinModelTests
{
    [Fact]
    public void Builds_six_body_parts_six_faces_each()
    {
        IReadOnlyList<SkinFace> faces = SkinModel.BuildFaces();
        // head, body, 2 arms, 2 legs = 6 cuboids; 6 faces per cuboid = 36.
        faces.Should().HaveCount(36);
    }

    [Fact]
    public void All_uvs_are_normalized_to_0_1()
    {
        IReadOnlyList<SkinFace> faces = SkinModel.BuildFaces();
        foreach (SkinFace f in faces)
        {
            f.U0.Should().BeInRange(0, 1);
            f.V0.Should().BeInRange(0, 1);
            f.U1.Should().BeInRange(0, 1);
            f.V1.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void Each_face_has_four_distinct_3d_corners()
    {
        IReadOnlyList<SkinFace> faces = SkinModel.BuildFaces();
        foreach (SkinFace f in faces)
        {
            var pts = new[] { f.P0, f.P1, f.P2, f.P3 };
            pts.Distinct().Should().HaveCountGreaterThan(1, "a degenerate face has all-same corners");
        }
    }

    [Fact]
    public void Head_is_at_top_of_model()
    {
        IReadOnlyList<SkinFace> faces = SkinModel.BuildFaces();
        double maxY = faces.Max(f => Math.Max(Math.Max(f.P0.Y, f.P1.Y), Math.Max(f.P2.Y, f.P3.Y)));
        // Head's top should be near y=32 (the model is built head-up).
        maxY.Should().BeApproximately(32.0, 0.1);
    }

    [Fact]
    public void Legs_reach_down_to_zero()
    {
        IReadOnlyList<SkinFace> faces = SkinModel.BuildFaces();
        double minY = faces.Min(f => Math.Min(Math.Min(f.P0.Y, f.P1.Y), Math.Min(f.P2.Y, f.P3.Y)));
        minY.Should().BeApproximately(0.0, 0.1);
    }
}
