// NbYard.cs — 场景 02「夜巡后院」
//
// 这一关和街区唯一的区别就是**空间**，所以空间不能随便摆：
//   街区 = 墙就是掩体，通道 5 m 宽，压力来自"这条巷子里有没有猫"；
//   后院 = 掩体是点状的，中央 20 × 20 m 完全没有遮挡，压力来自"从这丛灌木跑到那个垃圾桶要几秒"。
//
// 两条硬约束，改场景时别破坏：
//   ① 掩体必须**散**。排成一排就成了第二条街。
//   ② 中央必须**空**（x 62…82，z −38…−18）。这块空地是整关的核心：
//      玩家跑过去要 3~4 秒，而保安的手电 18 m 长 —— 快没用，得等光扫过去。
//
// 威胁是「保安 + 手电」，**没有猫**（用户明确要求删掉）：
//   保安走 0.95 m/s（玩家走 2.30），慢得可笑，但手电锥 18 m、而且**四处扫**：
//   走的时候在前方 ±55° 里乱扫（偶尔甩到 ±78°），到点站住 1~3 秒、整个人转过去照身后。
//   他在自己那一带**随机游走**（航点 5~16 m、路径带缓弯），不是来回走直线 ——
//   所以"他什么时候背对我"不能靠数秒数等到，只能当场看灯。
//   被光扫到 0.3 秒后他会以 2.55~3.45 m/s 冲过来 —— 比走路快、比跑步慢，
//   所以玩家的正确反应只有一个：按 Shift 跑到 5 m 之外的掩体后面。
//   掩体不是装饰，是**唯一能切断"他看见你"的东西**（Sense 的第三条：射线被挡住）。
//
// 所有尺寸单位是米，地面 y = 0。

using UnityEngine;

internal static class NbYard
{
    // ───────────────────────── 范围 ─────────────────────────

    internal const float XMin = 40f, XMax = 104f;    // 64 m
    internal const float ZMin = -60f, ZMax = 0f;     // 60 m

    internal const float CX = (XMin + XMax) * 0.5f;  // 72
    internal const float CZ = (ZMin + ZMax) * 0.5f;  // −30

    // 出生点在南侧小径上，朝北 —— 一进来看见整片院子，也看见那片躲不开的空地。
    internal static readonly Vector3 Start = new Vector3(CX, 0.10f, -54f);
    internal static readonly Vector3 Forward = new Vector3(0f, 0f, 1f);

    // ───────────────────────── 调色板 ─────────────────────────
    // 整体比街区更黑更绿：月光下草坪是墨绿，砖墙是发灰的暗红。
    static readonly Color Grass   = new Color(0.13f, 0.19f, 0.12f);
    static readonly Color Dirt    = new Color(0.22f, 0.18f, 0.14f);
    static readonly Color WallCol = new Color(0.24f, 0.19f, 0.17f);
    static readonly Color WallCap = new Color(0.31f, 0.27f, 0.23f);
    static readonly Color HouseCol = new Color(0.20f, 0.19f, 0.19f);
    static readonly Color Door    = new Color(0.09f, 0.07f, 0.06f);
    static readonly Color BushA   = new Color(0.09f, 0.17f, 0.10f);
    static readonly Color BushB   = new Color(0.12f, 0.22f, 0.12f);
    static readonly Color BinA    = new Color(0.14f, 0.16f, 0.15f);
    static readonly Color BinB    = new Color(0.11f, 0.13f, 0.12f);
    static readonly Color Wood    = new Color(0.20f, 0.15f, 0.10f);
    static readonly Color RoofCol = new Color(0.16f, 0.13f, 0.11f);
    static readonly Color ShedCol = new Color(0.17f, 0.16f, 0.14f);
    static readonly Color Cloth   = new Color(0.34f, 0.33f, 0.31f);
    static readonly Color Soil    = new Color(0.15f, 0.12f, 0.09f);

    // ───────────────────────── 10 个目标点 ─────────────────────────
    // **反过来是刻意的**：街区的顺序是"离出生点由近到远"，后院也是 ——
    // 但后院的"近"指的是沿掩体走，不是直线。第 1 关两块就在门口，
    // 第 10 关最后一块在东北角柴垛后面，要横穿整片空地。
    //
    // 距离基准是出生点 (72, −54)，平面直线距离写在行尾。

    internal static readonly Vector3[] Spots =
    {
        new Vector3(72f, 0f, -48f),    // 出生点正前的小径上             6.0 m
        new Vector3(60f, 0f, -46f),    // 西南灌木丛旁                  14.4 m
        new Vector3(86f, 0f, -44f),    // 东南灌木丛旁                  17.2 m
        new Vector3(52f, 0f, -40f),    // 西南角                        24.1 m
        new Vector3(96f, 0f, -40f),    // 东南角                        27.7 m
        new Vector3(48f, 0f, -26f),    // 西侧中段（工具棚后）           36.9 m
        new Vector3(100f, 0f, -26f),   // 东侧中段                      39.7 m
        new Vector3(56f, 0f, -12f),    // 西北（狗屋旁）                 44.8 m
        new Vector3(72f, 0f, -6f),     // 北侧正中（主屋后门）           48.0 m
        new Vector3(100f, 0f, -12f),   // 东北（柴垛后）                 50.6 m
    };

    // ───────────────────────── 10 条巡逻地带 ─────────────────────────
    // 保安**不再沿线段来回走**，而是拿这条线段当轴线、在两侧 ±7 m 里随机取航点
    // （见 NbGuard.PickWaypoint）—— 所以这一组数据定义的是"谁守哪一带"，不是"他走哪条线"。
    // **地带的轴线是穿过空地的**（第 2、5 条）——
    // 空地既是玩家的风险，也是巡逻者的主导区域；如果地带只沿着围墙，
    // 玩家可以一直贴着掩体走，这一关就没有"必须横穿"的时刻了。
    // （猫已经删掉了，但 Routes 仍然是 NbScene 的通用接口：别的场景还要用。）

    internal static readonly Vector3[][] Routes =
    {
        new[] { new Vector3(48f, 0f, -50f), new Vector3(96f, 0f, -50f) },    // 0 南侧横扫
        new[] { new Vector3(48f, 0f, -20f), new Vector3(96f, 0f, -20f) },    // 1 北侧横扫（穿空地北缘）
        new[] { new Vector3(72f, 0f, -46f), new Vector3(72f, 0f, -10f) },    // 2 中轴纵穿（直穿空地）
        new[] { new Vector3(56f, 0f, -40f), new Vector3(56f, 0f, -14f) },    // 3 西中纵线
        new[] { new Vector3(88f, 0f, -40f), new Vector3(88f, 0f, -14f) },    // 4 东中纵线
        new[] { new Vector3(44f, 0f, -34f), new Vector3(100f, 0f, -34f) },   // 5 横穿空地南缘
        new[] { new Vector3(44f, 0f, -6f),  new Vector3(100f, 0f, -6f) },    // 6 沿主屋
        new[] { new Vector3(46f, 0f, -44f), new Vector3(46f, 0f, -8f) },     // 7 沿西墙
        new[] { new Vector3(100f, 0f, -44f), new Vector3(100f, 0f, -8f) },   // 8 沿东墙
        new[] { new Vector3(56f, 0f, -52f), new Vector3(88f, 0f, -30f) },    // 9 斜穿（会从出生点前方掠过）
    };

    // ───────────────────────── 小地图要画的掩体 ─────────────────────────
    //
    // (x, z, 宽, 深)。**必须和 Cover() 里摆的尺寸一致** ——
    // 地图上多一块或漏一块，玩家就会按错误的地形做决策，而这一关的全部玩法就是地形。
    // 灌木在 3D 里是球，这里按外接方框画：小地图只负责"这块能不能躲"，不需要形状精度。

    internal static readonly Vector4[] MapCover =
    {
        new Vector4(60.0f, -46f, 3.0f, 3.0f),   // Bush SW
        new Vector4(86.0f, -44f, 3.2f, 3.2f),   // Bush SE
        new Vector4(52.0f, -25f, 2.8f, 2.8f),   // Bush W
        new Vector4(94.0f, -24f, 3.0f, 3.0f),   // Bush E
        new Vector4(66.0f, -11f, 2.9f, 2.9f),   // Bush NW
        new Vector4(82.0f, -13f, 2.6f, 2.6f),   // Bush NE
        new Vector4(47.5f, -44f, 2.8f, 2.6f),   // Bins W
        new Vector4(98.0f, -38f, 2.8f, 2.6f),   // Bins E
        new Vector4(99.0f, -14f, 2.6f, 1.4f),   // Woodpile
        new Vector4(56.0f, -10f, 2.2f, 2.0f),   // Dog House
        new Vector4(44.5f, -17f, 3.4f, 3.0f),   // Shed
    };

    // ───────────────────────── 写进当前场景 ─────────────────────────

    internal static void Setup()
    {
        NbScene.Name  = "BACKYARD PATROL";
        NbScene.Code  = "YARD";
        NbScene.Blurb = "One open lawn.  Nothing to hide behind in the middle.";

        NbScene.XMin = XMin;
        NbScene.XMax = XMax;
        NbScene.ZMin = ZMin;
        NbScene.ZMax = ZMax;

        NbScene.Start   = Start;
        NbScene.Forward = Forward;
        NbScene.Targets = Spots;
        NbScene.Routes  = Routes;
        NbScene.MaxTargets = Spots.Length;
        NbScene.MaxRoutes  = Routes.Length;
    }

    // ───────────────────────── 搭场景 ─────────────────────────

    internal static void Build(Transform p)
    {
        Ground(p);
        Walls(p);
        House(p);
        Cover(p);
        Clothesline(p, "Line W", -42f, 34f);
        Clothesline(p, "Line E", -24f, 30f);
        Beds(p);

        Debug.LogFormat("[Nb] 后院就位：{0:F0} × {1:F0} m，中央 20 × 20 m 空地无掩体，" +
                        "{2} 个目标点、{3} 段巡逻线。",
            XMax - XMin, ZMax - ZMin, Spots.Length, Routes.Length);
    }

    /// 地面是一块**实体**薄板（玩家要站在上面），土路只是贴在地上的色块、不参与碰撞。
    static void Ground(Transform p)
    {
        Nb.Box(p, "Yard Ground", new Vector3(CX, -0.20f, CZ),
            new Vector3(XMax - XMin, 0.40f, ZMax - ZMin), Grass, true);

        Nb.Box(p, "Path N-S", new Vector3(CX, 0.004f, CZ),
            new Vector3(2.2f, 0.008f, ZMax - ZMin - 1.5f), Dirt, false);
        Nb.Box(p, "Path E-W", new Vector3(CX, 0.006f, -34f),
            new Vector3(XMax - XMin - 1.5f, 0.008f, 2.0f), Dirt, false);
    }

    /// 南 / 西 / 东三面砖墙，北面是主屋自己挡着。墙厚 0.4、高 2.6 ——
    /// 比玩家高 8 倍，比保安的视线高 2 倍，靠跳是看不到院外的。
    static void Walls(Transform p)
    {
        const float h = 2.6f, t = 0.40f;

        Nb.Box(p, "Yard Wall S", new Vector3(CX, h * 0.5f, ZMin + t * 0.5f),
            new Vector3(XMax - XMin, h, t), WallCol, true);
        Nb.Box(p, "Yard Wall W", new Vector3(XMin + t * 0.5f, h * 0.5f, CZ),
            new Vector3(t, h, ZMax - ZMin), WallCol, true);
        Nb.Box(p, "Yard Wall E", new Vector3(XMax - t * 0.5f, h * 0.5f, CZ),
            new Vector3(t, h, ZMax - ZMin), WallCol, true);

        // 压顶：给墙一条亮边，夜里才看得出"这是边界"而不是一片黑
        Nb.Box(p, "Yard Cap S", new Vector3(CX, h + 0.06f, ZMin + t * 0.5f),
            new Vector3(XMax - XMin, 0.12f, t + 0.12f), WallCap, false);
        Nb.Box(p, "Yard Cap W", new Vector3(XMin + t * 0.5f, h + 0.06f, CZ),
            new Vector3(t + 0.12f, 0.12f, ZMax - ZMin), WallCap, false);
        Nb.Box(p, "Yard Cap E", new Vector3(XMax - t * 0.5f, h + 0.06f, CZ),
            new Vector3(t + 0.12f, 0.12f, ZMax - ZMin), WallCap, false);
    }

    /// 北侧的主屋（玩家从后院看是"房子背面"）。它同时是北边界和一整片遮挡 ——
    /// 第 8、9、10 关的目标点藏在它的屋檐下。
    static void House(Transform p)
    {
        Nb.Box(p, "House Back", new Vector3(CX, 2.0f, ZMax + 1.8f),
            new Vector3(XMax - XMin, 4.0f, 3.6f), HouseCol, true);

        // 后门 + 门前的两级台阶（台阶是装饰，不挡人）
        Nb.Box(p, "Back Door", new Vector3(CX, 1.05f, ZMax - 0.03f),
            new Vector3(1.10f, 2.10f, 0.10f), Door, false);
        Nb.Box(p, "Step Low", new Vector3(CX, 0.07f, ZMax - 0.55f),
            new Vector3(1.60f, 0.14f, 0.90f), WallCap, false);
        Nb.Box(p, "Step High", new Vector3(CX, 0.16f, ZMax - 0.20f),
            new Vector3(1.30f, 0.10f, 0.50f), WallCap, false);

        // 两扇窗（自发光的暗黄）：夜里给北侧一点可辨的方向
        for (int i = 0; i < 2; i++)
        {
            float x = CX + (i == 0 ? -9.5f : 9.5f);
            Nb.Box(p, "Window " + i, new Vector3(x, 2.15f, ZMax - 0.02f),
                new Vector3(1.30f, 1.05f, 0.06f), new Color(0.34f, 0.27f, 0.14f), false);
        }
    }

    /// 掩体。**中央那块空地不许放任何东西** —— 见文件头。
    static void Cover(Transform p)
    {
        // ── 灌木（球体碰撞：挡人、也挡保安的视线）──
        Bush(p, "Bush SW", 60f, -46f, 3.0f);
        Bush(p, "Bush SE", 86f, -44f, 3.2f);
        Bush(p, "Bush W",  52f, -25f, 2.8f);
        Bush(p, "Bush E",  94f, -24f, 3.0f);
        Bush(p, "Bush NW", 66f, -11f, 2.9f);
        Bush(p, "Bush NE", 82f, -13f, 2.6f);

        // ── 垃圾桶堆（三只一组，靠墙）──
        Bins(p, "Bins W", 47.5f, -44f);
        Bins(p, "Bins E", 98.0f, -38f);

        // ── 柴垛 / 狗屋 / 工具棚 ──
        Nb.Box(p, "Woodpile", new Vector3(99f, 0.65f, -14f),
            new Vector3(2.6f, 1.30f, 1.4f), Wood, true);
        Nb.Box(p, "Woodpile Cap", new Vector3(99f, 1.36f, -14f),
            new Vector3(2.9f, 0.14f, 1.7f), RoofCol, false);

        Nb.Box(p, "Dog House", new Vector3(56f, 0.75f, -10f),
            new Vector3(2.2f, 1.50f, 2.0f), Wood, true);
        Nb.Box(p, "Dog Roof", new Vector3(56f, 1.62f, -10f),
            new Vector3(2.7f, 0.22f, 2.5f), RoofCol, false);

        Nb.Box(p, "Shed", new Vector3(44.5f, 1.05f, -17f),
            new Vector3(3.4f, 2.10f, 3.0f), ShedCol, true);
        Nb.Box(p, "Shed Roof", new Vector3(44.5f, 2.16f, -17f),
            new Vector3(3.8f, 0.16f, 3.4f), RoofCol, false);
    }

    static void Bush(Transform p, string n, float x, float z, float d)
    {
        // 主球是实体的：它既是视觉主体，也是保安视线的遮挡物。
        Nb.Ball(p, n, new Vector3(x, d * 0.30f, z), d, BushA, true);
        // 两个小一点的副球不挡人，只是把轮廓打碎，别让灌木看起来像一个台球。
        Nb.Ball(p, n + " b", new Vector3(x + d * 0.26f, d * 0.20f, z + d * 0.18f), d * 0.66f, BushB, false);
        Nb.Ball(p, n + " c", new Vector3(x - d * 0.22f, d * 0.16f, z - d * 0.20f), d * 0.58f, BushB, false);
    }

    static void Bins(Transform p, string n, float x, float z)
    {
        Nb.Cyl(p, n + " a", new Vector3(x, 0.55f, z), 1.15f, 1.10f, BinA, true);
        Nb.Cyl(p, n + " b", new Vector3(x + 0.95f, 0.50f, z + 0.55f), 1.05f, 1.00f, BinB, true);
        Nb.Cyl(p, n + " c", new Vector3(x - 0.60f, 0.45f, z + 0.80f), 0.95f, 0.90f, BinA, true);
    }

    /// 晾衣绳：两根柱子是实体的（能挡人），绳子和布是装饰（挡不了人、也挡不了视线）。
    /// 它横在院子里有两个作用：视觉上把空间切开，跑动时又不会真的挡路 ——
    /// 玩家会本能地绕开它，于是路线被"软"约束了，这是空地设计的一部分。
    static void Clothesline(Transform p, string n, float z, float len)
    {
        float hw = len * 0.5f;
        Nb.Box(p, n + " Post W", new Vector3(CX - hw, 1.10f, z),
            new Vector3(0.14f, 2.20f, 0.14f), Wood, true);
        Nb.Box(p, n + " Post E", new Vector3(CX + hw, 1.10f, z),
            new Vector3(0.14f, 2.20f, 0.14f), Wood, true);
        Nb.Box(p, n + " Rope", new Vector3(CX, 2.08f, z),
            new Vector3(len, 0.03f, 0.03f), Cloth, false);

        for (int i = 0; i < 3; i++)
        {
            float x = CX - len * 0.30f + i * len * 0.28f;
            Nb.Box(p, n + " Cloth " + i, new Vector3(x, 1.58f, z),
                new Vector3(0.75f, 0.95f, 0.04f), Cloth, false);
        }
    }

    /// 菜畦：只有 20 cm 高，**故意不加碰撞体** ——
    /// 它是地面上唯一有颜色的东西，用来让玩家在暗处还能量距离（一条 3.6 m 的畦 ≈ 一秒半的跑动）。
    static void Beds(Transform p)
    {
        float[] xs = { 62f, 68f, 76f, 82f };
        for (int i = 0; i < xs.Length; i++)
        {
            Nb.Box(p, "Bed " + i, new Vector3(xs[i], 0.10f, -47f),
                new Vector3(3.6f, 0.20f, 1.4f), Soil, false);
            Nb.Box(p, "Bed " + i + " Crop", new Vector3(xs[i], 0.22f, -47f),
                new Vector3(3.2f, 0.10f, 1.0f), BushB, false);
        }
    }
}
