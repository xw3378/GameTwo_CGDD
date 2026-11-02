// @role Procedural surface library: tileable albedo+normal textures, meter-scaled box meshes, per-scene reskin pass.
// @calls NbScene NbBuilder NbYard NbFridge

// NbSkin.cs — 给三个场景里"纯色方块"换一层有纹理的皮
//
// 为什么要有这一层：
//   三个场景的几何全是 Nb.Box/Ball/Cyl 拼的，材质只有一个 _BaseColor —— 于是
//   路面是一整片灰、围墙是一整片砖红、冰箱搁板是一整片亮灰。夜里打上月光之后，
//   画面上只剩"色块 + 边缘"，没有任何尺度感：玩家看不出自己离墙 0.3 m 还是 3 m，
//   因为 3 m 内没有任何频率信息。这一层补的就是这个频率信息。
//
// 两个关键决定（别随手改）：
//   ① **贴图在运行时生成，不落盘。** 工程里没有 PBR 素材，也不该为了"路面有颗粒"
//      往仓库里塞几十 MB 的贴图；256~512 px 的噪声图在 0.16 m 视高下完全够用。
//   ② **UV 按米算。** Unity 的 Cube 原始 UV 是每面 0..1，3.5 × 60 m 的铺装贴同一张图
//      会被拉成 1:17 的长条。所以这里按物体的实际尺寸生成一份立方体网格，
//      UV 直接写成米 —— 贴图一个 tile 永远是 1 m（各表面可调），不会被比例拉花。
//      网格还是 1×1×1，Transform 的缩放不动 ⇒ BoxCollider 的尺寸不受影响。
//
// 只换"还没贴图、名字能认出来"的物体：FBX 楼栋自带贴图，一律跳过。

using System.Collections.Generic;
using UnityEngine;

internal static class NbSkin
{
    internal enum Surf
    {
        None = 0,
        Concrete,   // 水泥铺装 / 路缘 / 压顶
        Asphalt,    // 沥青（备用）
        Brick,      // 砖墙
        Stucco,     // 抹灰外墙
        Wood,       // 木板 / 木箱 / 栅栏
        Metal,      // 刷漆钢：垃圾桶、灯杆、井盖
        Grass,      // 草地
        Soil,       // 泥土 / 菜畦
        Plastic,    // 冰箱内胆、塑料袋、白塑料台阶
        Steel,      // 冰箱搁板（不锈钢）
        Carton,     // 纸箱
        Leaves,     // 灌木 / 树冠
    }

    static readonly Dictionary<Surf, Material> Mats = new Dictionary<Surf, Material>();
    static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

    // 一张贴图覆盖多少米。数值越大，颗粒越粗、重复感越弱。
    static float MetersPerTile(Surf s)
    {
        switch (s)
        {
            case Surf.Grass:    return 2.60f;
            case Surf.Concrete: return 2.20f;
            case Surf.Asphalt:  return 2.60f;
            case Surf.Brick:    return 1.20f;
            case Surf.Stucco:   return 1.80f;
            case Surf.Wood:     return 1.00f;
            case Surf.Metal:    return 1.00f;
            case Surf.Steel:    return 0.80f;
            case Surf.Soil:     return 1.60f;
            case Surf.Carton:   return 0.60f;
            case Surf.Leaves:   return 0.95f;
            default:            return 1.00f;
        }
    }

    // ───────────────────────── 对外入口 ─────────────────────────

    /// 把 root 底下所有「原始 Cube + 没有贴图」的渲染体换成带纹理的皮。
    internal static void Apply(Transform root, NbSceneKind kind)
    {
        if (root == null) return;

        Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
        int done = 0, skipped = 0;
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (!(r is MeshRenderer)) continue;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            bool isCube = mf.sharedMesh.name == "Cube";
            bool isBall = mf.sharedMesh.name == "Sphere";
            if (!isCube && !isBall) continue;

            Material old = r.sharedMaterial;
            if (old != null && old.HasProperty("_BaseMap") && old.GetTexture("_BaseMap") != null) continue;

            Surf s = Classify(kind, r);
            if (s == Surf.None) { skipped++; continue; }

            // 球体（灌木/树冠）只换材质：它的 UV 是球面展开，贴一张噪声叶簇正好，
            // 换成方盒网格会把轮廓变成方块，得不偿失。
            if (isCube)
            {
                Vector3 size = mf.transform.lossyScale;
                mf.sharedMesh = BoxMesh(size);
            }
            else if (s != Surf.Leaves) { skipped++; continue; }

            r.sharedMaterial = Get(s);
            done++;
        }

        if (kind == NbSceneKind.Street) ToneBuildings(root);

        Debug.LogFormat("[Nb] 材质换皮（{0}）：{1} 件贴图化，{2} 件保持纯色。", kind, done, skipped);
    }

    /// 两栋 FBX 楼的材质是纯白的（没有贴图），夜里读起来像"没上色的纸板"。
    /// 这里只压暗它们的底色，不动贴图 —— 它们的 UV 是整栋展开的，
    /// 硬套一张 1.2 m 的砖纹会得到"每层楼一块砖"，比白着更假。
    static void ToneBuildings(Transform root)
    {
        int n = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.sharedMaterial == null) continue;
            string m = r.sharedMaterial.name;
            if (m != "building 1" && m != "hara_u1_v1") continue;

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", new Color(0.30f, 0.29f, 0.27f));
            r.SetPropertyBlock(mpb);
            n++;
        }
        if (n > 0) Debug.LogFormat("[Nb] 纯白楼体压暗：{0} 件。", n);
    }

    /// 名字 → 表面。**按场景分表**：三个场景的命名是各自一套，
    /// 混在一起判会撞（街区的 "Wall" 是砖墙，冰箱的 "Fridge Wall" 是白色内胆）。
    static Surf Classify(NbSceneKind kind, Renderer r)
    {
        string n = r.name.ToLowerInvariant();

        switch (kind)
        {
            case NbSceneKind.Street:
                if (n.Contains("lawn")) return Surf.Grass;
                if (n.Contains("alley") || n.Contains("ring") || n.Contains("apron")) return Surf.Concrete;
                if (n.Contains("curb")) return Surf.Concrete;
                if (n.Contains("grate")) return Surf.Metal;
                if (n.Contains("pebble") || n.Contains("crack") || n.Contains("leaf")) return Surf.None;
                if (n.Contains("pole") || n.Contains("arm") || n.Contains("head") || n.Contains("lid")) return Surf.Metal;
                if (n.Contains("body")) return Surf.Metal;
                if (n.Contains("glass")) return Surf.None;
                if (n.Contains("trunk")) return Surf.Wood;
                if (n.Contains("crown") || n.Contains("bush")) return Surf.Leaves;
                if (n.Contains("soil")) return Surf.Soil;
                if (n.Contains("planter") || n.Contains("gate")) return Surf.Concrete;
                if (n.Contains("cap")) return Surf.Concrete;
                if (n.Contains("wall") || n.Contains("post") || n.Contains("lintel")) return Surf.Brick;
                return Surf.None;

            case NbSceneKind.Yard:
                if (n.Contains("ground")) return Surf.Grass;
                if (n.Contains("path")) return Surf.Soil;
                if (n.Contains("crop")) return Surf.None;
                if (n.Contains("bed")) return Surf.Soil;
                if (n.Contains("bush")) return Surf.Leaves;
                if (n == "woodpile" || n.Contains("woodpile cap")) return Surf.Wood;
                if (n.Contains("dog house")) return Surf.Wood;
                if (n.Contains("dog roof") || n.Contains("shed roof") || n.Contains("woodpile cap")) return Surf.Wood;
                if (n.Contains("shed")) return Surf.Stucco;
                if (n.Contains("house back")) return Surf.Brick;
                if (n.Contains("door")) return Surf.Wood;
                if (n.Contains("window")) return Surf.None;
                if (n.Contains("cap")) return Surf.Concrete;
                if (n.Contains("wall")) return Surf.Brick;
                if (n.Contains("bin")) return Surf.Metal;
                if (n.Contains("post") || n.Contains("rope") || n.Contains("cloth")) return Surf.Wood;
                if (n.Contains("step")) return Surf.Concrete;
                return Surf.None;

            case NbSceneKind.Fridge:
                if (n.Contains("lamp")) return Surf.None;
                if (n.Contains("shelf") || n.Contains("rib")) return Surf.Steel;
                if (n.Contains("cover") || n.Contains("carton") || n.Contains("melon") ||
                    n.Contains("bread") || n.Contains("butter") || n.Contains("band") ||
                    n.Contains("wrap") || n.Contains("lid")) return Surf.None;
                if (n.Contains("floor") || n.Contains("ceiling") || n.Contains("wall")) return Surf.Plastic;
                if (n.Contains("step")) return Surf.Plastic;
                return Surf.None;

            default:
                return Surf.None;
        }
    }

    // ───────────────────────── 材质 ─────────────────────────

    static Material Get(Surf s)
    {
        Material m;
        if (Mats.TryGetValue(s, out m) && m != null) return m;

        float mpt = MetersPerTile(s);
        Texture2D albedo, normal;
        float smooth, metal;
        Build(s, out albedo, out normal, out smooth, out metal);

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh);
        m.name = "NbSkin_" + s;

        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", normal);
        if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);

        // 网格 UV 是米，贴图一个 tile 是 mpt 米 ⇒ 缩放 1/mpt
        float k = 1f / mpt;
        m.SetTextureScale("_BaseMap", new Vector2(k, k));
        if (m.HasProperty("_BumpMap")) m.SetTextureScale("_BumpMap", new Vector2(k, k));

        Mats[s] = m;
        return m;
    }

    // ───────────────────────── 网格：UV 按米 ─────────────────────────

    /// 1×1×1 的立方体，但每个面的 UV 是**米**（u 方向 = 该面的宽，v 方向 = 高）。
    /// 这样同一个材质铺在 0.1 m 的压顶和 60 m 的铺装上，颗粒大小都一致。
    static Mesh BoxMesh(Vector3 size)
    {
        string key = size.x.ToString("F2") + "|" + size.y.ToString("F2") + "|" + size.z.ToString("F2");
        Mesh cached;
        if (Meshes.TryGetValue(key, out cached) && cached != null) return cached;

        float sx = Mathf.Max(0.001f, size.x), sy = Mathf.Max(0.001f, size.y), sz = Mathf.Max(0.001f, size.z);

        Vector3[] v = new Vector3[24];
        Vector3[] nrm = new Vector3[24];
        Vector2[] uv = new Vector2[24];
        int[] tri = new int[36];

        // 六个面：法线、四个角（逆时针，从外面看）、u/v 方向的真实米数
        AddFace(v, nrm, uv, 0, Vector3.right,
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), sz, sy);
        AddFace(v, nrm, uv, 4, Vector3.left,
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f), sz, sy);
        AddFace(v, nrm, uv, 8, Vector3.up,
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), sx, sz);
        AddFace(v, nrm, uv, 12, Vector3.down,
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f), sx, sz);
        AddFace(v, nrm, uv, 16, Vector3.forward,
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), sx, sy);
        AddFace(v, nrm, uv, 20, Vector3.back,
            new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), sx, sy);

        for (int f = 0; f < 6; f++)
        {
            int o = f * 4, t = f * 6;
            tri[t + 0] = o; tri[t + 1] = o + 1; tri[t + 2] = o + 2;
            tri[t + 3] = o; tri[t + 4] = o + 2; tri[t + 5] = o + 3;
        }

        Mesh m = new Mesh();
        m.name = "NbBox_" + key;
        m.vertices = v;
        m.normals = nrm;
        m.uv = uv;
        m.triangles = tri;
        m.RecalculateTangents();
        m.UploadMeshData(false);

        Meshes[key] = m;
        return m;
    }

    /// 一个面：四个角按「从外面看逆时针」给，u/v 是该面两个方向上的真实米数。
    static void AddFace(Vector3[] v, Vector3[] nrm, Vector2[] uv, int o, Vector3 n,
        Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, float uMeters, float vMeters)
    {
        v[o + 0] = c0; v[o + 1] = c1; v[o + 2] = c2; v[o + 3] = c3;
        uv[o + 0] = new Vector2(0f, 0f);
        uv[o + 1] = new Vector2(uMeters, 0f);
        uv[o + 2] = new Vector2(uMeters, vMeters);
        uv[o + 3] = new Vector2(0f, vMeters);
        for (int i = 0; i < 4; i++) nrm[o + i] = n;
    }

    // ───────────────────────── 贴图生成 ─────────────────────────

    static void Build(Surf s, out Texture2D albedo, out Texture2D normal, out float smooth, out float metal)
    {
        int size = 512;
        float[] h = new float[size * size];
        Color32[] px = new Color32[size * size];

        smooth = 0.10f;
        metal = 0f;
        int seed = 1000 + (int)s;

        switch (s)
        {
            case Surf.Grass:
                smooth = 0.06f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float clump = Fbm(u, vv, 5, 5, seed);
                        float blade = Fbm(u * 6f, vv * 1f, 40, 3, seed + 3);
                        float dry = Fbm(u, vv, 2, 3, seed + 9);
                        float g = 0.165f + 0.10f * clump + 0.04f * (blade - 0.5f);
                        float rr = 0.095f + 0.05f * clump;
                        float bb = 0.070f + 0.035f * clump;
                        if (dry > 0.62f) { float k = (dry - 0.62f) * 1.6f; rr += k * 0.12f; g += k * 0.06f; bb += k * 0.02f; }
                        int i = y * size + x;
                        px[i] = new Color32(B(rr), B(g), B(bb), 255);
                        h[i] = blade * 0.8f + clump * 0.2f;
                    }
                break;

            case Surf.Concrete:
                smooth = 0.12f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float broad = Fbm(u, vv, 3, 5, seed);
                        float grain = Fbm(u, vv, 48, 3, seed + 2);
                        float stain = Fbm(u, vv, 2, 4, seed + 7);
                        float sp = Hash(x, y, seed + 11);
                        float basev = 0.26f + 0.06f * (broad - 0.5f) + 0.035f * (grain - 0.5f) + 0.05f * (stain - 0.5f);
                        if (sp > 0.988f) basev -= 0.14f;     // 深色骨料
                        else if (sp < 0.010f) basev += 0.10f; // 浅色石子

                        // 伸缩缝：一个 tile = 2.2 m，缝正好落在 tile 边界上，
                        // 于是整条路每隔 2.2 m 有一道缝 —— 这是"水泥板"最直接的判据。
                        // 缝不是一条死直线：用低频噪声把位置推 ±1.5 cm。
                        float wob = (Fbm(u, vv, 3, 2, seed + 21) - 0.5f) * 0.012f;
                        float ju = Mathf.Abs(((u + wob + 0.5f) % 1f) - 0.5f);
                        float jv = Mathf.Abs(((vv + wob * 0.7f + 0.5f) % 1f) - 0.5f);
                        bool joint = ju > 0.4965f || jv > 0.4965f;
                        if (joint) { basev *= 0.62f; }
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 1.02f), B(basev), B(basev * 0.955f), 255);
                        h[i] = joint ? 0.05f : grain * 0.75f + broad * 0.25f;
                    }
                break;

            case Surf.Asphalt:
                smooth = 0.16f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float broad = Fbm(u, vv, 4, 4, seed);
                        float grain = Fbm(u, vv, 64, 2, seed + 5);
                        float sp = Hash(x, y, seed + 13);
                        float bv = 0.15f + 0.045f * (broad - 0.5f) + 0.04f * (grain - 0.5f);
                        if (sp > 0.975f) bv += 0.09f;
                        int i = y * size + x;
                        px[i] = new Color32(B(bv), B(bv * 0.98f), B(bv * 0.96f), 255);
                        h[i] = grain;
                    }
                break;

            case Surf.Brick:
                smooth = 0.09f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        const int cols = 4, rows = 12;
                        float fv = vv * rows;
                        int row = Mathf.FloorToInt(fv);
                        float off = (row % 2) * 0.5f;
                        float fu = u * cols + off;
                        int col = Mathf.FloorToInt(fu) % cols;
                        float bu = fu - Mathf.FloorToInt(fu);
                        float bv = fv - row;

                        const float mortarU = 0.055f, mortarV = 0.14f;
                        bool mortar = bu < mortarU || bu > 1f - mortarU || bv < mortarV || bv > 1f - mortarV;

                        float jitter = 0.80f + 0.40f * Hash(col, row, seed + 3);
                        float rough = Fbm(u * 16f, vv * 16f, 16, 3, seed + 4);
                        float basev;
                        float r, g, b;
                        if (mortar)
                        {
                            basev = 0.18f + 0.030f * rough;
                            r = basev * 0.98f; g = basev * 0.97f; b = basev * 0.93f;
                        }
                        else
                        {
                            basev = (0.13f + 0.05f * rough) * jitter;
                            r = basev * 1.45f; g = basev * 0.70f; b = basev * 0.58f;
                        }
                        int i = y * size + x;
                        px[i] = new Color32(B(r), B(g), B(b), 255);
                        h[i] = mortar ? 0.15f : 1f + rough * 0.2f;
                    }
                break;

            case Surf.Stucco:
                smooth = 0.11f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float grain = Fbm(u, vv, 64, 3, seed);
                        float patch = Fbm(u, vv, 3, 4, seed + 4);
                        float basev = 0.34f + 0.035f * (grain - 0.5f) + 0.06f * (patch - 0.5f);
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 1.03f), B(basev), B(basev * 0.94f), 255);
                        h[i] = grain;
                    }
                break;

            case Surf.Wood:
                smooth = 0.14f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        const int planks = 6;
                        float pv = vv * planks;
                        int pi = Mathf.FloorToInt(pv);
                        float pf = pv - pi;
                        float grain = Fbm(u * 2f, vv * 48f, 48, 3, seed + pi);
                        float knot = Fbm(u * 6f, vv * 6f, 6, 2, seed + 8);
                        float gap = (pf < 0.04f || pf > 0.96f) ? 0.45f : 1f;
                        float jitter = 0.85f + 0.3f * Hash(pi, 3, seed);
                        float basev = (0.18f + 0.07f * grain) * jitter * gap;
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 1.35f), B(basev * 0.98f), B(basev * 0.72f), 255);
                        h[i] = gap * (0.4f + grain * 0.6f);
                    }
                break;

            case Surf.Metal:
                smooth = 0.42f; metal = 0.55f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float brush = Fbm(u * 1f, vv * 90f, 90, 2, seed);
                        float rust = Fbm(u, vv, 3, 4, seed + 6);
                        float basev = 0.20f + 0.06f * (brush - 0.5f) + 0.07f * (rust - 0.5f);
                        float r = basev * 0.70f, g = basev * 1.02f, b = basev * 0.86f;
                        if (rust > 0.66f) { float k = (rust - 0.66f) * 2.2f; r += k * 0.30f; g += k * 0.12f; b += k * 0.02f; }
                        int i = y * size + x;
                        px[i] = new Color32(B(r), B(g), B(b), 255);
                        h[i] = brush * 0.6f + rust * 0.4f;
                    }
                break;

            case Surf.Steel:
                smooth = 0.55f; metal = 0.75f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float brush = Fbm(u * 1f, vv * 120f, 120, 2, seed);
                        float dirt = Fbm(u, vv, 5, 3, seed + 2);
                        float basev = 0.56f + 0.05f * (brush - 0.5f) + 0.04f * (dirt - 0.5f);
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 0.97f), B(basev * 0.99f), B(basev), 255);
                        h[i] = brush * 0.4f + dirt * 0.2f;
                    }
                break;

            case Surf.Soil:
                smooth = 0.08f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float clod = Fbm(u, vv, 8, 4, seed);
                        float grain = Fbm(u, vv, 64, 2, seed + 3);
                        float basev = 0.105f + 0.05f * (clod - 0.5f) + 0.025f * (grain - 0.5f);
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 1.55f), B(basev * 1.12f), B(basev * 0.80f), 255);
                        h[i] = clod * 0.7f + grain * 0.3f;
                    }
                break;

            case Surf.Plastic:
                smooth = 0.38f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float wrinkle = Fbm(u * 3f, vv * 2f, 6, 4, seed);
                        float basev = 0.52f + 0.04f * (wrinkle - 0.5f);
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 0.98f), B(basev), B(basev * 1.01f), 255);
                        h[i] = wrinkle;
                    }
                break;

            case Surf.Carton:
                smooth = 0.10f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float fiber = Fbm(u * 8f, vv * 2f, 40, 3, seed);
                        float basev = 0.28f + 0.04f * (fiber - 0.5f);
                        int i = y * size + x;
                        px[i] = new Color32(B(basev * 1.30f), B(basev * 1.02f), B(basev * 0.72f), 255);
                        h[i] = fiber;
                    }
                break;

            case Surf.Leaves:
                smooth = 0.16f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, vv = y / (float)size;
                        float clump = Fbm(u, vv, 5, 5, seed);
                        float leaf = Fbm(u * 12f, vv * 12f, 72, 2, seed + 5);
                        float spot = leaf > 0.58f ? 1.45f : (leaf < 0.42f ? 0.50f : 1.00f);
                        float shade = (0.70f + 0.85f * (clump - 0.5f)) * spot;
                        int i = y * size + x;
                        px[i] = new Color32(B(0.075f * shade * 1.20f), B(0.125f * shade), B(0.055f * shade), 255);
                        h[i] = leaf * 0.75f + clump * 0.25f;
                    }
                break;

            default:
                albedo = Texture2D.whiteTexture as Texture2D;
                normal = Texture2D.normalTexture as Texture2D;
                return;
        }

        albedo = MakeMap(px, size, false);
        normal = NormalsFrom(h, size, s == Surf.Brick ? 1.6f : 1.0f);
    }

    static byte B(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

    static Texture2D MakeMap(Color32[] px, int size, bool linear)
    {
        // 生成时写的是**线性**反射率（0.26 = 草地、0.50 = 水泥），但颜色贴图是 sRGB：
        // 直接把 0.26 塞进 sRGB 纹理，采样回来会变成 0.055 —— 所有表面都会黑五倍。
        // 法线图不能转（它是数据不是颜色），所以只在 albedo 这一路做转换。
        if (!linear)
        {
            for (int i = 0; i < px.Length; i++)
            {
                px[i].r = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(px[i].r / 255f) * 255f), 0, 255);
                px[i].g = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(px[i].g / 255f) * 255f), 0, 255);
                px[i].b = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(px[i].b / 255f) * 255f), 0, 255);
            }
        }

        Texture2D t = new Texture2D(size, size, TextureFormat.RGB24, true, linear);
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Trilinear;
        t.anisoLevel = 9;
        t.SetPixels32(px);
        t.Apply(true, false);
        return t;
    }

    static Texture2D NormalsFrom(float[] h, int size, float strength)
    {
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int yu = ((y + 1) % size) * size, yd = ((y - 1 + size) % size) * size, yc = y * size;
            for (int x = 0; x < size; x++)
            {
                int xr = (x + 1) % size, xl = (x - 1 + size) % size;
                float dx = (h[yc + xr] - h[yc + xl]) * strength;
                float dy = (h[yu + x] - h[yd + x]) * strength;
                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                px[yc + x] = new Color32(B(n.x * 0.5f + 0.5f), B(n.y * 0.5f + 0.5f), B(n.z * 0.5f + 0.5f), 255);
            }
        }
        return MakeMap(px, size, true);
    }

    // ───────────────────────── 噪声 ─────────────────────────

    static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

    static float Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static float Noise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = Mod(xi, period), x1 = Mod(xi + 1, period);
        int y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed);
        float c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// u,v ∈ [0,1)，所有八度都按整数周期采样 ⇒ 左右上下都能无缝平铺。
    static float Fbm(float u, float v, int period, int oct, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int p = period;
        for (int i = 0; i < oct; i++)
        {
            sum += amp * Noise(u * p, v * p, p, seed + i * 17);
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / norm;
    }
}
