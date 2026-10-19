// NeighbourhoodBuilder.cs — 把 12 栋楼之间的空地搭成一个封闭的居民区
//
// 只负责"环境"：草地、过道铺装、围墙+大门、路灯、树、垃圾桶、花坛。
// 楼栋本身由编辑器脚本 Assets/Editor/StreetFromModels.cs 换成真实模型（那一半走场景资源，
// 运行时没法实例化不在 Resources 里的 FBX）。
//
// 全部是原始几何拼的，不依赖任何美术资源；颜色统一在文件顶部，改一处就换整个街区的调子。
//
// **两处入口，同一份几何**：
//   ① 运行时的 `Build()`：场景里没有烘好的版本时，开机自己搭一份（老做法，兜底）；
//   ② 编辑器的 `Tools/Street/Bake Neighbourhood Props`：把同一份几何**写进场景文件**，
//      这样不按 Play、直接打开 SampleScene 就能看见树和路灯，Play 的第一帧也在。
//      `Build()` 开头会先找这份烘好的东西，找到就沿用，不会再生成第二套（那会 z-fighting）。

using UnityEngine;

// 公开（public）是因为 Assets/Editor 下的烘焙脚本在另一个程序集里，要调 Build()；
// 运行时其它脚本仍按 internal 的用法调，不受影响。
public static class NbBuilder
{
    static readonly Color Grass   = new Color(0.34f, 0.48f, 0.30f);
    static readonly Color Pave    = new Color(0.66f, 0.65f, 0.62f);
    static readonly Color PaveDark= new Color(0.55f, 0.54f, 0.52f);
    static readonly Color WallCol = new Color(0.56f, 0.45f, 0.40f);
    static readonly Color WallCap = new Color(0.74f, 0.70f, 0.64f);
    static readonly Color Iron    = new Color(0.20f, 0.21f, 0.23f);
    static readonly Color LampWarm= new Color(1.00f, 0.86f, 0.58f);
    static readonly Color Trunk   = new Color(0.33f, 0.24f, 0.17f);
    static readonly Color LeafA   = new Color(0.28f, 0.46f, 0.24f);
    static readonly Color LeafB   = new Color(0.36f, 0.55f, 0.29f);
    static readonly Color BinCol  = new Color(0.24f, 0.36f, 0.30f);

    // 编辑器烘焙和运行时装配共用同一个根名 —— 运行时就是靠这个名字判断"场景里已经有一份了"。
    public const string RootName = "Neighbourhood (auto)";

    public static Transform Build(Transform parent)
    {
        Transform baked = FindExisting();
        if (baked != null)
        {
            Debug.LogFormat("[Nb] 场景里已经有搭好的居民区「{0}」（{1} 个渲染器），直接沿用，不再重复生成。" +
                            "要按代码重搭，先删掉它或跑菜单 Tools/Street/Bake Neighbourhood Props。",
                RootName, baked.GetComponentsInChildren<Renderer>(true).Length);
            return baked;
        }

        Transform root = new GameObject(RootName).transform;
        root.SetParent(parent, false);

        BuildGround(root);
        BuildGroundDetail(root);
        BuildWalls(root);
        BuildLamps(root);
        BuildTrees(root);
        BuildProps(root);

        Debug.LogFormat("[Nb] 居民区环境已搭好：{0:F1} x {1:F1} m，12 栋楼之间 {2} 段过道，围墙 + 1 个大门。" +
                        "（只在场景里没有烘好的版本时才会走到这一步）",
            Nb.Width, Nb.Depth, 5);
        return root;
    }

    /// 场景里是否已经有烘好的居民区（编辑期写进场景文件的那一份）。
    /// 不进 Play 就能在 Scene / Game 视图里看见树、路灯、花坛，靠的就是它。
    /// 用 `scene.GetRootGameObjects()` 而不是 `GameObject.Find`：后者**找不到未激活的物体**，
    /// 万一有人把这一坨关掉了，运行时会在旁边再生成一套完全重叠的（z-fighting 闪成一片）。
    internal static Transform FindExisting()
    {
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i] != null && roots[i].name == RootName) return roots[i].transform;
        return null;
    }

    // ───────────────────────── 地面与铺装 ─────────────────────────

    static void BuildGround(Transform p)
    {
        // 整片草地（比围墙再大一圈，省得边界露白）
        Nb.Box(p, "Lawn", new Vector3((Nb.XMin + Nb.XMax) * 0.5f, 0.005f, (Nb.ZMin + Nb.ZMax) * 0.5f),
            new Vector3(Nb.Width + 4f, 0.01f, Nb.Depth + 4f), Grass, true);

        // 铺装全部按「楼群实测包络」切缝，不再用固定 16×14 的网格：
        // 楼栋外沿 BlockX0/X1、BlockZ0/Z1，过道就是这些外沿之间的空档。
        float ix0 = Nb.XMin + 0.30f, ix1 = Nb.XMax - 0.30f;
        float iz0 = Nb.ZMin + 0.30f, iz1 = Nb.ZMax - 0.30f;

        // 环形通道：贴墙一圈，四段拼起来（内侧顶到楼群外沿）
        PaveRect(p, "Ring W", ix0, Nb.BlockX0, iz0, iz1, 0.040f);
        PaveRect(p, "Ring E", Nb.BlockX1, ix1, iz0, iz1, 0.040f);
        PaveRect(p, "Ring N", Nb.BlockX0, Nb.BlockX1, Nb.BlockZ1, iz1, 0.040f);
        PaveRect(p, "Ring S", Nb.BlockX0, Nb.BlockX1, iz0, Nb.BlockZ0, 0.040f);

        // 两条纵向通道（贯穿整片楼群）
        PaveRect(p, "Alley V West", Nb.VAx0, Nb.VAx1, Nb.BlockZ0, Nb.BlockZ1, 0.044f);
        PaveRect(p, "Alley V East", Nb.VBx0, Nb.VBx1, Nb.BlockZ0, Nb.BlockZ1, 0.044f);

        // 三条横向通道（排与排之间）
        PaveRect(p, "Alley H 1", Nb.BlockX0, Nb.BlockX1, Nb.H1z0, Nb.H1z1, 0.048f);
        PaveRect(p, "Alley H 2", Nb.BlockX0, Nb.BlockX1, Nb.H2z0, Nb.H2z1, 0.048f);
        PaveRect(p, "Alley H 3", Nb.BlockX0, Nb.BlockX1, Nb.H3z0, Nb.H3z1, 0.048f);

        // 门洞前的一小片水泥地，让大门外不是悬空的草皮
        Nb.Box(p, "Gate Apron", new Vector3((Nb.GateX0 + Nb.GateX1) * 0.5f, 0.012f, Nb.ZMax + 1.6f),
            new Vector3(Nb.GateX1 - Nb.GateX0 + 1.2f, 0.024f, 3.4f), PaveDark, true);
    }

    /// 一段水泥铺装：x0..x1 / z0..z1 的矩形。
    /// `top` 是顶面高度 —— 几段铺装互相重叠（环道和纵向道交叉）时必须在 y 上错开几千分之一，
    /// 否则两个共面的面会 z-fighting 闪成一片。
    static void PaveRect(Transform p, string name, float x0, float x1, float z0, float z1, float top)
    {
        float h = 0.02f;
        Nb.Box(p, name, new Vector3((x0 + x1) * 0.5f, top - h * 0.5f, (z0 + z1) * 0.5f),
            new Vector3(x1 - x0, h, z1 - z0), Pave, true);

        // 长边各压一条深色收边，读起来像"路缘"
        if (z1 - z0 >= x1 - x0)
        {
            Nb.Box(p, name + " Curb A", new Vector3((x0 + x1) * 0.5f, top + 0.004f, z0 + 0.09f),
                new Vector3(x1 - x0, 0.03f, 0.18f), PaveDark, false);
            Nb.Box(p, name + " Curb B", new Vector3((x0 + x1) * 0.5f, top + 0.004f, z1 - 0.09f),
                new Vector3(x1 - x0, 0.03f, 0.18f), PaveDark, false);
        }
        else
        {
            Nb.Box(p, name + " Curb A", new Vector3(x0 + 0.09f, top + 0.004f, (z0 + z1) * 0.5f),
                new Vector3(0.18f, 0.03f, z1 - z0), PaveDark, false);
            Nb.Box(p, name + " Curb B", new Vector3(x1 - 0.09f, top + 0.004f, (z0 + z1) * 0.5f),
                new Vector3(0.18f, 0.03f, z1 - z0), PaveDark, false);
        }
    }

    // ───────────────────────── 地面细节（低机位专用）─────────────────────────

    // 老鼠视角（眼高 0.16 m）下，画面下半部几乎全是路面。
    // 如果路面是一个纯色平面，走起来画面上没有任何东西在动 —— 这正是之前几轮
    // "看着像卡住"的根源。所以沿过道撒一层**贴地的参照物**：石子、裂纹、落叶、井盖。
    //
    // 全部是无碰撞的装饰（显式传 solid=false ⇒ 删碰撞体 + Ignore Raycast 层）：
    // 不挡人、不挡猫的视线、也不参与地面探测（TryGroundY 只打 DefaultRaycastLayers）。
    // 位置用固定种子的线性同余生成，**每次搭出来的都一样** —— 否则每局地形细节都在变，
    // 烘焙进场景和运行时生成的两份也会对不上。

    static readonly Color PebbleCol = new Color(0.44f, 0.42f, 0.38f);
    static readonly Color CrackCol  = new Color(0.24f, 0.23f, 0.22f);
    static readonly Color LeafA2    = new Color(0.36f, 0.28f, 0.15f);
    static readonly Color LeafB2    = new Color(0.30f, 0.36f, 0.18f);

    static void BuildGroundDetail(Transform p)
    {
        // { x0, x1, z0, z1, 铺装顶面 y } —— 与 BuildGround 里 PaveRect 的调用一一对应
        float[,] lanes =
        {
            { Nb.VAx0, Nb.VAx1, Nb.BlockZ0, Nb.BlockZ1, 0.044f },   // 纵向 A
            { Nb.VBx0, Nb.VBx1, Nb.BlockZ0, Nb.BlockZ1, 0.044f },   // 纵向 B
            { Nb.BlockX0, Nb.BlockX1, Nb.H1z0, Nb.H1z1, 0.048f },   // 横向 1
            { Nb.BlockX0, Nb.BlockX1, Nb.H2z0, Nb.H2z1, 0.048f },   // 横向 2
            { Nb.BlockX0, Nb.BlockX1, Nb.H3z0, Nb.H3z1, 0.048f },   // 横向 3
            { Nb.XMin + 0.30f, Nb.BlockX0, Nb.ZMin + 0.30f, Nb.ZMax - 0.30f, 0.040f },  // 环道西
            { Nb.BlockX1, Nb.XMax - 0.30f, Nb.ZMin + 0.30f, Nb.ZMax - 0.30f, 0.040f },  // 环道东
            { Nb.BlockX0, Nb.BlockX1, Nb.BlockZ1, Nb.ZMax - 0.30f, 0.040f },            // 环道北
            { Nb.BlockX0, Nb.BlockX1, Nb.ZMin + 0.30f, Nb.BlockZ0, 0.040f },            // 环道南
        };

        int seed = 20261017;
        int n = 0;

        for (int L = 0; L < lanes.GetLength(0); L++)
        {
            float x0 = lanes[L, 0], x1 = lanes[L, 1], z0 = lanes[L, 2], z1 = lanes[L, 3], top = lanes[L, 4];
            float area = (x1 - x0) * (z1 - z0);
            int count = Mathf.Clamp(Mathf.RoundToInt(area / 22f), 4, 34);

            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(x0 + 0.35f, x1 - 0.35f, Rnd(ref seed));
                float z = Mathf.Lerp(z0 + 0.35f, z1 - 0.35f, Rnd(ref seed));
                float pick = Rnd(ref seed);
                float yaw = Rnd(ref seed) * 360f;

                if (pick < 0.46f)
                {
                    // 石子：压扁的小方块，贴地 4~7 cm
                    float w = 0.05f + 0.055f * Rnd(ref seed);
                    GameObject g = Nb.Box(p, "Pebble " + n, new Vector3(x, top + 0.012f, z),
                        new Vector3(w, 0.045f, w * 1.15f), PebbleCol, false);
                    g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                else if (pick < 0.78f)
                {
                    // 裂纹：细长的深色条，0.4~1.3 m
                    float len = 0.4f + 0.9f * Rnd(ref seed);
                    GameObject g = Nb.Box(p, "Crack " + n, new Vector3(x, top + 0.004f, z),
                        new Vector3(0.035f, 0.006f, len), CrackCol, false);
                    g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                else
                {
                    // 落叶
                    GameObject g = Nb.Box(p, "Leaf " + n, new Vector3(x, top + 0.009f, z),
                        new Vector3(0.10f, 0.006f, 0.07f), Rnd(ref seed) < 0.5f ? LeafA2 : LeafB2, false);
                    g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                n++;
            }
        }

        // 井盖：5 个，钉在过道交叉口 + 出生点正前方 4.5 m 处。
        // 它们既是参照物，也是"我到底走了多远"的天然里程碑。
        float[,] grates =
        {
            { 78.00f, Nb.H2cz },      // 出生点朝东 4.5 m —— 一开局正前方就有一个
            { Nb.VAcx, Nb.H1cz },
            { Nb.VAcx, Nb.H3cz },
            { Nb.VBcx, Nb.H2cz },
            { Nb.VBcx, Nb.H1cz },
        };
        for (int i = 0; i < grates.GetLength(0); i++)
        {
            float gx = grates[i, 0], gz = grates[i, 1];
            float top = (Mathf.Abs(gx - Nb.VAcx) < 3f || Mathf.Abs(gx - Nb.VBcx) < 3f) ? 0.048f : 0.044f;
            Nb.Box(p, "Grate " + i, new Vector3(gx, top + 0.007f, gz),
                new Vector3(0.56f, 0.014f, 0.56f), Iron, false);
            Nb.Box(p, "Grate Rim " + i, new Vector3(gx, top + 0.004f, gz),
                new Vector3(0.68f, 0.008f, 0.68f), new Color(0.30f, 0.29f, 0.27f), false);
        }

        Debug.LogFormat("[Nb] 地面细节：{0} 个石子/裂纹/落叶 + {1} 个井盖（全部无碰撞、Ignore Raycast 层）。",
            n, grates.GetLength(0));
    }

    /// 固定种子的线性同余（0..1）。不用 Random：要保证每次搭出来完全一致，
    /// 否则烘焙进场景的那份和运行时补的那份会错位。
    static float Rnd(ref int s)
    {
        s = s * 1103515245 + 12345;
        return ((s >> 8) & 0xFFFFFF) / (float)0x1000000;
    }

    // ───────────────────────── 围墙与大门 ─────────────────────────

    static void BuildWalls(Transform p)
    {
        const float h = 2.5f, t = 0.4f;
        float zc = Nb.ZMax + 0.2f;      // 前墙稍微外移，给门柱留厚度
        float zb = Nb.ZMin - 0.2f;

        Wall(p, "Wall Front W", new Vector3((Nb.XMin + Nb.GateX0) * 0.5f, h * 0.5f, zc),
            new Vector3(Nb.GateX0 - Nb.XMin, h, t));
        Wall(p, "Wall Front E", new Vector3((Nb.GateX1 + Nb.XMax) * 0.5f, h * 0.5f, zc),
            new Vector3(Nb.XMax - Nb.GateX1, h, t));
        Wall(p, "Wall Back", new Vector3((Nb.XMin + Nb.XMax) * 0.5f, h * 0.5f, zb),
            new Vector3(Nb.XMax - Nb.XMin, h, t));
        Wall(p, "Wall West", new Vector3(Nb.XMin - 0.2f, h * 0.5f, (Nb.ZMin + Nb.ZMax) * 0.5f),
            new Vector3(t, h, Nb.Depth));
        Wall(p, "Wall East", new Vector3(Nb.XMax + 0.2f, h * 0.5f, (Nb.ZMin + Nb.ZMax) * 0.5f),
            new Vector3(t, h, Nb.Depth));

        // 门柱 + 过梁：让"大门"这件事在远处也能看出来
        float px0 = Nb.GateX0 - 0.35f, px1 = Nb.GateX1 + 0.35f;
        Nb.Box(p, "Gate Post W", new Vector3(px0, 1.6f, zc), new Vector3(0.7f, 3.2f, 0.9f), WallCol, true);
        Nb.Box(p, "Gate Post E", new Vector3(px1, 1.6f, zc), new Vector3(0.7f, 3.2f, 0.9f), WallCol, true);
        Nb.Box(p, "Gate Post W Cap", new Vector3(px0, 3.28f, zc), new Vector3(0.92f, 0.16f, 1.1f), WallCap, false);
        Nb.Box(p, "Gate Post E Cap", new Vector3(px1, 3.28f, zc), new Vector3(0.92f, 0.16f, 1.1f), WallCap, false);
        Nb.Box(p, "Gate Lintel", new Vector3((px0 + px1) * 0.5f, 3.05f, zc),
            new Vector3(px1 - px0 + 0.7f, 0.34f, 0.5f), WallCol, false);
    }

    static void Wall(Transform p, string name, Vector3 center, Vector3 size)
    {
        Nb.Box(p, name, center, size, WallCol, true);
        // 压顶：比墙体宽 8 cm，像真围墙的水泥帽
        Nb.Box(p, name + " Cap", new Vector3(center.x, size.y + 0.07f, center.z),
            new Vector3(size.x + 0.08f, 0.14f, size.z + 0.08f), WallCap, false);
    }

    // ───────────────────────── 路灯 ─────────────────────────

    static void BuildLamps(Transform p)
    {
        // 灯杆必须站在过道**里面**：A 道 x 68.80…73.90、B 道 x 91.00…96.40（实测楼体包络）。
        // 灯头再朝巷子中间偏 0.9 m，光才照在路面上而不是照在墙上。
        Lamp(p, "Lamp V West 0", new Vector3(69.35f, 0f, Nb.H1cz), 1f);
        Lamp(p, "Lamp V West 1", new Vector3(69.35f, 0f, Nb.H3cz), 1f);
        Lamp(p, "Lamp V East 0", new Vector3(95.85f, 0f, Nb.H2cz), -1f);
        Lamp(p, "Lamp V East 1", new Vector3(95.85f, 0f, Nb.H1cz), -1f);
    }

    /// side = +1 时灯臂朝 +x，-1 朝 -x（让灯头伸向过道中间）
    static void Lamp(Transform p, string name, Vector3 at, float side)
    {
        Transform root = new GameObject(name).transform;
        root.SetParent(p, false);
        root.localPosition = at;

        Nb.Cyl(root, "Pole", new Vector3(0f, 1.7f, 0f), 0.14f, 3.4f, Iron, false);
        Nb.Box(root, "Arm", new Vector3(side * 0.45f, 3.32f, 0f), new Vector3(0.9f, 0.1f, 0.1f), Iron, false);
        Nb.Box(root, "Head", new Vector3(side * 0.9f, 3.2f, 0f), new Vector3(0.42f, 0.16f, 0.26f), Iron, false);
        // 灯罩玻璃要自发光：夜里布光是暗的，靠材质自己亮才看得出"灯是开的"
        GameObject glass = Nb.Box(root, "Glass", new Vector3(side * 0.9f, 3.11f, 0f),
            new Vector3(0.32f, 0.06f, 0.18f), LampWarm, false);
        Renderer gr = glass.GetComponent<Renderer>();
        if (gr != null) gr.sharedMaterial = Nb.MatEmissive(LampWarm, 2.4f);
    }

    // ───────────────────────── 树 ─────────────────────────

    static void BuildTrees(Transform p)
    {
        // 环形通道里种一圈，顺便把"围墙内只有沥青"的空旷感压掉。
        // 注意树要**贴墙那侧**（墙在 46.6 / 118.4），把环道中轴让出来 —— 环形通道那只猫就是沿中轴兜圈的。
        float twx = Nb.XMin + 1.20f, tex = Nb.XMax - 1.20f;
        float tzn = Nb.ZMax - 1.00f, tzs = Nb.ZMin + 1.00f;

        float[] westZ = { -6.0f, -26.0f, -46.0f, -68.0f };
        float[] eastZ = { -2.0f, -24.0f, -44.0f, -66.0f };
        for (int i = 0; i < westZ.Length; i++) Tree(p, "Tree W " + i, new Vector3(twx, 0f, westZ[i]), i);
        for (int i = 0; i < eastZ.Length; i++) Tree(p, "Tree E " + i, new Vector3(tex, 0f, eastZ[i]), i + 3);

        // 南北两条环道各摆三棵（避开大门正对的位置，别一进门就被树挡死）
        float[] northX = { 56.0f, 84.0f, 104.0f };
        float[] southX = { 58.0f, 82.0f, 106.0f };
        for (int i = 0; i < northX.Length; i++) Tree(p, "Tree N " + i, new Vector3(northX[i], 0f, tzn), i + 5);
        for (int i = 0; i < southX.Length; i++) Tree(p, "Tree S " + i, new Vector3(southX[i], 0f, tzs), i + 7);
    }

    static void Tree(Transform p, string name, Vector3 at, int seed)
    {
        float k = 0.86f + 0.05f * (seed % 5);        // 高矮错开一点，不要一片复制的树
        Transform root = new GameObject(name).transform;
        root.SetParent(p, false);
        root.localPosition = at;

        Nb.Cyl(root, "Trunk", new Vector3(0f, 1.3f * k, 0f), 0.34f * k, 2.6f * k, Trunk);
        Nb.Ball(root, "Crown A", new Vector3(0f, 3.5f * k, 0f), 3.1f * k, (seed & 1) == 0 ? LeafA : LeafB, false);
        Nb.Ball(root, "Crown B", new Vector3(0.5f * ((seed % 3) - 1), 4.35f * k, 0.4f * ((seed % 4) - 1.5f)),
            2.05f * k, (seed & 1) == 0 ? LeafB : LeafA, false);
    }

    // ───────────────────────── 垃圾桶 / 花坛 ─────────────────────────

    static void BuildProps(Transform p)
    {
        // 垃圾桶：贴着过道边缘放，不占中线（A 道 x 68.80…73.90，B 道 91.00…96.40，桶半径 0.86）
        Vector3[] bins =
        {
            new Vector3(69.75f, 0f, -12.0f), new Vector3(72.95f, 0f, -17.0f),
            new Vector3(69.75f, 0f, -30.0f), new Vector3(72.95f, 0f, -44.5f),
            new Vector3(92.00f, 0f, -12.0f), new Vector3(95.40f, 0f, -31.0f),
            new Vector3(92.00f, 0f, -47.5f), new Vector3(95.40f, 0f, -63.0f),
        };
        for (int i = 0; i < bins.Length; i++)
        {
            Transform root = new GameObject("Bin " + i).transform;
            root.SetParent(p, false);
            root.localPosition = bins[i];
            Nb.Cyl(root, "Body", new Vector3(0f, 0.55f, 0f), 0.86f, 1.1f, BinCol);
            Nb.Cyl(root, "Lid", new Vector3(0f, 1.14f, 0f), 0.94f, 0.08f, Iron);
        }

        // 花坛：放在过道与楼栋的交接处，纯装饰（1.5 宽，所以两边都留了 0.9 的余量）
        Vector3[] planters =
        {
            new Vector3(69.90f, 0f, Nb.H1cz + 3.6f), new Vector3(72.70f, 0f, Nb.H1cz + 3.6f),
            new Vector3(92.40f, 0f, Nb.H2cz - 2.8f), new Vector3(95.00f, 0f, Nb.H2cz - 2.8f),
            new Vector3(69.90f, 0f, Nb.H3cz + 2.4f), new Vector3(72.70f, 0f, Nb.H3cz + 2.4f),
        };
        for (int i = 0; i < planters.Length; i++)
        {
            Transform root = new GameObject("Planter " + i).transform;
            root.SetParent(p, false);
            root.localPosition = planters[i];
            Nb.Box(root, "Box", new Vector3(0f, 0.28f, 0f), new Vector3(1.5f, 0.56f, 0.8f), WallCap);
            Nb.Box(root, "Soil", new Vector3(0f, 0.57f, 0f), new Vector3(1.3f, 0.06f, 0.62f), new Color(0.24f, 0.18f, 0.13f), false);
            for (int b = 0; b < 3; b++)
                Nb.Ball(root, "Bush " + b, new Vector3(-0.42f + b * 0.42f, 0.74f, 0f), 0.62f, LeafB, false);
        }
    }
}
