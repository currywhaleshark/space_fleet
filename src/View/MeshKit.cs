using System;
using Godot;

namespace SpaceFleet.View;

/// <summary>절차적 메시와 텍스처 생성 도구.</summary>
public static class MeshKit
{
    /// <summary>
    /// 두 직사각형 단면(XY 평면, Z축 방향으로 배치)을 잇는 사다리꼴 블록.
    /// 함수·뱃머리·날개처럼 끝이 좁아지는 형태에 쓴다.
    /// </summary>
    public static void AddTaper(SurfaceTool st, Vector3 c0, Vector2 s0, Vector3 c1, Vector2 s1)
    {
        Vector3[] a = Rect(c0, s0);
        Vector3[] b = Rect(c1, s1);
        Vector3 centroid = (c0 + c1) * 0.5f;

        AddQuad(st, centroid, a[0], a[1], a[2], a[3]);
        AddQuad(st, centroid, b[0], b[1], b[2], b[3]);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            AddQuad(st, centroid, a[i], a[j], b[j], b[i]);
        }
    }

    public static ArrayMesh Taper(Vector3 c0, Vector2 s0, Vector3 c1, Vector2 s1)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddTaper(st, c0, s0, c1, s1);
        return st.Commit();
    }

    private static Vector3[] Rect(Vector3 c, Vector2 s)
    {
        float hx = s.X * 0.5f, hy = s.Y * 0.5f;
        return new[]
        {
            c + new Vector3(-hx, -hy, 0), c + new Vector3(hx, -hy, 0),
            c + new Vector3(hx, hy, 0), c + new Vector3(-hx, hy, 0),
        };
    }

    private static void AddQuad(SurfaceTool st, Vector3 centroid, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 faceCenter = (p0 + p1 + p2 + p3) * 0.25f;
        Vector3 outward = faceCenter - centroid;
        Vector3 n = (p1 - p0).Cross(p2 - p0);
        if (n.LengthSquared() < 1e-12f)
            n = (p2 - p0).Cross(p3 - p0);
        if (n.LengthSquared() < 1e-12f)
            return; // 면적 0인 면(뾰족한 끝)

        n = n.Normalized();
        // Godot은 시계 방향이 앞면. 외향 법선 기준으로 감기 순서를 맞춘다.
        bool flip = n.Dot(outward) > 0f;
        if (flip)
            n = -n;
        Vector3 normal = -n;

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            st.SetNormal(normal);
            st.AddVertex(a);
            st.SetNormal(normal);
            st.AddVertex(flip ? c : b);
            st.SetNormal(normal);
            st.AddVertex(flip ? b : c);
        }

        Tri(p0, p1, p2);
        Tri(p0, p2, p3);
    }

    /// <summary>
    /// 사각 패널 무늬 텍스처(타일링). 거대 함선 표면에 "판 단위" 스케일 단서를 준다.
    /// </summary>
    public static ImageTexture PanelTexture(int size = 512, int seed = 7)
    {
        var rng = new Random(seed);
        Image img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        img.Fill(new Color(0.55f, 0.55f, 0.55f));
        Split(img, rng, new Rect2I(0, 0, size, size), 0);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static void Split(Image img, Random rng, Rect2I r, int depth)
    {
        bool canSplitX = r.Size.X > 24;
        bool canSplitY = r.Size.Y > 24;
        bool stop = depth >= 7 || (!canSplitX && !canSplitY) || (depth > 2 && rng.NextDouble() < 0.18);
        if (stop)
        {
            float v = 0.74f + (float)rng.NextDouble() * 0.26f;
            // 1px 이음매를 남기고 판을 칠한다.
            img.FillRect(new Rect2I(r.Position + Vector2I.One, r.Size - Vector2I.One), new Color(v, v, v));
            return;
        }

        // 긴 변 쪽을 더 자주 자른다. 한쪽만 자를 수 있으면 그쪽.
        bool splitX = canSplitX && (!canSplitY || rng.NextDouble() < (r.Size.X >= r.Size.Y ? 0.7 : 0.3));
        if (splitX)
        {
            int cut = Snap(r.Size.X, rng);
            Split(img, rng, new Rect2I(r.Position, new Vector2I(cut, r.Size.Y)), depth + 1);
            Split(img, rng, new Rect2I(r.Position + new Vector2I(cut, 0), new Vector2I(r.Size.X - cut, r.Size.Y)), depth + 1);
        }
        else
        {
            int cut = Snap(r.Size.Y, rng);
            Split(img, rng, new Rect2I(r.Position, new Vector2I(r.Size.X, cut)), depth + 1);
            Split(img, rng, new Rect2I(r.Position + new Vector2I(0, cut), new Vector2I(r.Size.X, r.Size.Y - cut)), depth + 1);
        }
    }

    private static int Snap(int length, Random rng)
    {
        // 8px 격자에 맞춰 잘라 판이 정돈돼 보이게 한다.
        int cells = length / 8;
        int cut = 8 * (1 + rng.Next(Math.Max(1, cells - 1)));
        return Math.Clamp(cut, 8, length - 8);
    }
}
