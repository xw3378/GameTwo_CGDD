// @role Micro-detail pass: grass tufts, weeds, puddles. All no-collider, one combined mesh each.
// @calls NbScene NbYard

// NbDetail.cs — 近景细节：草簇、墙根杂草、水洼
//
// 为什么单独一层：
//   换皮解决了"表面是一整片纯色"，但地面仍然是**一个完美的平面** —— 没有任何东西
//   从平面里长出来。老鼠视角（眼高 0.16 m）下，画面的下半部全是地面，
//   一个没有高度的地面看起来像一张纸。真实感在这一层靠的不是贴图分辨率，是**轮廓**。
//
// 三条约束：
//   ① 全部无碰撞、Ignore Raycast 层 —— 玩家/猫/手电的判定不能因为几根草改变；
//   ② 合并成一个网格，不是 700 个 GameObject —— 否则每帧 700 次剔除；
//   ③ 位置用固定种子，**每次进关都一样**（和 NbBuilder 的地面细节同一套约定）。

using System.Collections.Generic;
using UnityEngine;

internal static class NbDetail
{
    // ───────────────────────── 对外入口 ─────────────────────────

    internal static void Build(Transform root, NbSceneKind kind)
    {
        if (root == null) return;
        switch (kind)
        {
            case NbSceneKind.Yard:
                Tufts(root, "Yard Tufts", YardSpots(), 0.72f, 1.18f);
                Tufts(root, "Yard Weeds", WallWeeds(40.6f, 103.4f, -59.4f, -0.6f, 70), 0.7f, 1.15f);
                break;

            case NbSceneKind.Street:
                Tufts(root, "Street Weeds", StreetWeeds(), 0.6f, 1.0f);
                Puddles(root);
                break;

            case NbSceneKind.Fridge:
                Frost(root);
                break;
        }
    }

    // ───────────────────────── 位置 ─────────────────────────

    static List<Vector3> YardSpots()
    {
        var list = new List<Vector3>(900);
        int seed = 7717;
        for (int i = 0; i < 3400; i++)
        {
            float x = Mathf.Lerp(NbYard.XMin + 0.8f, NbYard.XMax - 0.8f, Rnd(ref seed));
            float z = Mathf.Lerp(NbYard.ZMin + 0.8f, NbYard.ZMax - 0.8f, Rnd(ref seed));

            // 土路和菜畦上不长草
            if (Mathf.Abs(x - NbYard.CX) < 1.5f) continue;
            if (Mathf.Abs(z - (-34f)) < 1.4f) continue;
            if (Mathf.Abs(z - (-47f)) < 1.0f) continue;

            // 掩体内部不长草（用地图上那组掩体框，和玩法用的是同一份数据）
            bool inside = false;
            Vector4[] cov = NbYard.MapCover;
            for (int c = 0; c < cov.Length; c++)
            {
                if (Mathf.Abs(x - cov[c].x) < cov[c].z * 0.5f + 0.25f &&
                    Mathf.Abs(z - cov[c].y) < cov[c].w * 0.5f + 0.25f) { inside = true; break; }
            }
            if (inside) continue;

            list.Add(new Vector3(x, 0f, z));
        }
        return list;
    }

    /// 沿四边墙脚 0.35~0.9 m 一圈杂草（墙根积水、草长得最旺）。
    static List<Vector3> WallWeeds(float x0, float x1, float z0, float z1, int per)
    {
        var list = new List<Vector3>();
        int seed = 3311;
        for (int i = 0; i < per; i++)
        {
            float t = Rnd(ref seed);
            float inset = 0.35f + 0.55f * Rnd(ref seed);
            if (i % 2 == 0) list.Add(new Vector3(Mathf.Lerp(x0, x1, t), 0f, z0 + inset));
            else list.Add(new Vector3(Mathf.Lerp(x0, x1, t), 0f, z1 - inset));
        }
        for (int i = 0; i < per / 2; i++)
        {
            float t = Rnd(ref seed);
            float inset = 0.35f + 0.55f * Rnd(ref seed);
            if (i % 2 == 0) list.Add(new Vector3(x0 + inset, 0f, Mathf.Lerp(z0, z1, t)));
            else list.Add(new Vector3(x1 - inset, 0f, Mathf.Lerp(z0, z1, t)));
        }
        return list;
    }

    /// 街区：环道贴墙那一圈 + 巷子拐角
    static List<Vector3> StreetWeeds()
    {
        var list = new List<Vector3>();
        int seed = 9911;
        for (int i = 0; i < 260; i++)
        {
            float t = Rnd(ref seed);
            float inset = 0.30f + 0.30f * Rnd(ref seed);
            switch (i % 4)
            {
                case 0: list.Add(new Vector3(Nb.XMin + inset, 0.04f, Mathf.Lerp(Nb.ZMin + 1f, Nb.ZMax - 1f, t))); break;
                case 1: list.Add(new Vector3(Nb.XMax - inset, 0.04f, Mathf.Lerp(Nb.ZMin + 1f, Nb.ZMax - 1f, t))); break;
                case 2: list.Add(new Vector3(Mathf.Lerp(Nb.XMin + 1f, Nb.XMax - 1f, t), 0.04f, Nb.ZMax - inset)); break;
                default: list.Add(new Vector3(Mathf.Lerp(Nb.XMin + 1f, Nb.XMax - 1f, t), 0.04f, Nb.ZMin + inset)); break;
            }
        }
        return list;
    }

    // ───────────────────────── 草簇 ─────────────────────────

    static void Tufts(Transform root, string name, List<Vector3> spots, float minScale, float maxScale)
    {
        if (spots == null || spots.Count == 0) return;

        Transform t = new GameObject(name).transform;
        t.SetParent(root, false);
        int layer = Nb.DecorLayer;
        if (layer >= 0) t.gameObject.layer = layer;

        var verts = new List<Vector3>(spots.Count * 12);
        var uvs = new List<Vector2>(spots.Count * 12);
        var tris = new List<int>(spots.Count * 18);
        int seed = 4242;

        for (int i = 0; i < spots.Count; i++)
        {
            Vector3 at = spots[i];
            float s = Mathf.Lerp(minScale, maxScale, Rnd(ref seed));
            float yaw = Rnd(ref seed) * 360f;

            for (int b = 0; b < 3; b++)
            {
                float a = (yaw + b * 60f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 side = Vector3.Cross(Vector3.up, dir) * (0.075f * s);
                float h = 0.14f * s * (0.75f + 0.5f * Rnd(ref seed));
                Vector3 lean = dir * (0.02f * s);

                int o = verts.Count;
                verts.Add(at - side);
                verts.Add(at + side);
                verts.Add(at + side + Vector3.up * h + lean);
                verts.Add(at - side + Vector3.up * h + lean);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 1);
                tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
            }
        }

        Mesh m = new Mesh();
        m.name = name;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        var up = new Vector3[verts.Count];
        for (int i = 0; i < up.Length; i++) up[i] = Vector3.up;
        m.normals = up;
        m.RecalculateBounds();

        MeshFilter mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = m;
        MeshRenderer mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GrassBladeMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
    }

    static Material _bladeMat;

    static Material GrassBladeMat()
    {
        if (_bladeMat != null) return _bladeMat;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(sh);
        m.name = "NbSkin_GrassBlade";
        m.SetTexture("_BaseMap", BladeTexture());
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.05f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Cutoff", 0.35f);
        m.SetFloat("_AlphaClip", 1f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 0f);                 // 双面：草叶没有背面
        m.renderQueue = 2450;
        _bladeMat = m;
        return m;
    }

    /// 64×64 的草叶贴图：几根带弯曲的窄叶，顶上渐隐（alpha clip 之后就是叶尖）。
    static Texture2D BladeTexture()
    {
        const int S = 64;
        Color32[] px = new Color32[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

        int seed = 6161;
        for (int b = 0; b < 7; b++)
        {
            float baseX = 6f + 52f * Rnd(ref seed);
            float bend = (Rnd(ref seed) - 0.5f) * 18f;
            float w = 1.4f + 1.8f * Rnd(ref seed);
            float top = S * (0.55f + 0.45f * Rnd(ref seed));
            for (int y = 0; y < (int)top; y++)
            {
                float t = y / top;
                float cx = baseX + bend * t * t;
                float half = w * (1f - t * 0.85f);
                float shade = 0.34f + 0.52f * (1f - t);
                byte r = (byte)Mathf.Clamp(34 * shade * 2.4f, 0, 255);
                byte g = (byte)Mathf.Clamp(142 * shade, 0, 255);
                byte bb = (byte)Mathf.Clamp(58 * shade, 0, 255);
                for (int x = (int)(cx - half); x <= (int)(cx + half); x++)
                {
                    if (x < 0 || x >= S) continue;
                    px[y * S + x] = new Color32(r, g, bb, 255);
                }
            }
        }

        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return tex;
    }

    // ───────────────────────── 水洼 ─────────────────────────

    static void Puddles(Transform root)
    {
        Material wet = WetMat();
        // 半径控制在 0.5~0.9 m：0.16 m 视高下，1.5 m 的水洼会直接铺满整个画面，
        // 读成"地上有一面镜子"而不是"下过雨的坑"。
        float[,] spots =
        {
            { 71.9f, -31.0f, 0.80f },
            { 72.6f, -21.4f, 0.55f },
            { 94.1f, -34.8f, 0.70f },
            { 93.2f, -45.6f, 0.60f },
            { 86.6f, -42.2f, 0.85f },
            { 70.8f, -57.0f, 0.65f },
            { 95.2f, -60.2f, 0.55f },
            { 78.2f, -52.6f, 0.50f },
        };

        Transform g = new GameObject("Puddles").transform;
        g.SetParent(root, false);

        int seed = 5150;
        for (int i = 0; i < spots.GetLength(0); i++)
        {
            float r = spots[i, 2];
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Puddle " + i;
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3(spots[i, 0], 0.0484f, spots[i, 1]);
            go.transform.localScale = new Vector3(r * 2f, 0.0015f, r * 1.35f);
            go.transform.localRotation = Quaternion.Euler(0f, Rnd(ref seed) * 180f, 0f);

            Collider c = go.GetComponent<Collider>();
            if (c != null) Nb.Kill(c);
            int layer = Nb.DecorLayer;
            if (layer >= 0) go.layer = layer;

            Renderer rr = go.GetComponent<Renderer>();
            rr.sharedMaterial = wet;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    static Material _wetMat;

    static Material WetMat()
    {
        if (_wetMat != null) return _wetMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(sh);
        m.name = "NbSkin_Wet";
        m.SetColor("_BaseColor", new Color(0.055f, 0.060f, 0.070f));
        m.SetFloat("_Smoothness", 0.86f);
        m.SetFloat("_Metallic", 0.05f);
        _wetMat = m;
        return m;
    }

    // ───────────────────────── 冰箱：霜 ─────────────────────────

    /// 每层搁板前沿挂一层薄霜：一条很浅的白蓝色，靠 0.001 的厚度避免共面闪烁。
    static void Frost(Transform root)
    {
        Material frost = FrostMat();
        for (int n = 1; n <= NbFridge.Layers; n++)
        {
            float z = NbFridge.FrontZ(n);
            GameObject go = Nb.Box(root, "Frost L" + n,
                new Vector3(NbFridge.CX, NbFridge.Top(n) + 0.004f, z - 0.24f),
                new Vector3(NbFridge.InnerW - 0.4f, 0.004f, 0.36f), Color.white, false);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = frost;
        }
    }

    static Material _frostMat;

    static Material FrostMat()
    {
        if (_frostMat != null) return _frostMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(sh);
        m.name = "NbSkin_Frost";
        m.SetColor("_BaseColor", new Color(0.80f, 0.86f, 0.92f));
        m.SetFloat("_Smoothness", 0.65f);
        m.SetFloat("_Metallic", 0f);
        _frostMat = m;
        return m;
    }

    static float Rnd(ref int s)
    {
        s = s * 1103515245 + 12345;
        return ((s >> 8) & 0xFFFFFF) / (float)0x1000000;
    }
}
