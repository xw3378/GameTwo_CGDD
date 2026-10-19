// NbKitchen.cs — 场景 03「深夜厨房」
//
// 空间设计：
//   六条纵向台面把 40 × 32 m 的房间切成**五条 1.2 m 宽的走道**。
//   街区是 5 m 巷子（能并肩跑），这里是 1.2 m（勉强过一只老鼠）—— 相机贴地时压迫感完全不同：
//   两侧台面比玩家高 3 倍，抬头只看得见台面边缘和天花板那一线。
//
// 两条硬约束：
//   ① **主走道（x = 60.6）一根灯管都不放。** 它是玩家唯一的纵向安全通道，
//      所有目标都藏在侧走道里 —— 于是"拿一个、退回主道、再去下一个"成了固定节奏。
//      把灯也摆到主道上，这一关会变成"无论怎么走都会死"。
//   ② 台面必须**留横通道**（z −18.5…−13.5）。没有它，五条走道就是五条死胡同，
//      玩家进去捡完必须原路退回，第 6 关以后根本走不完。
//
// 机制：食物腐坏（每块独立倒计时、全场同时开始）+ 灯管下的光区致命 + 暗角里的猫只能靠声音发现。

using UnityEngine;

internal static class NbKitchen
{
    // ───────────────────────── 范围 ─────────────────────────

    internal const float XMin = 40f, XMax = 80f;     // 40 m
    internal const float ZMin = -32f, ZMax = 0f;     // 32 m

    internal const float CX = (XMin + XMax) * 0.5f;  // 60
    internal const float CZ = (ZMin + ZMax) * 0.5f;  // −16

    internal static readonly Vector3 Start = new Vector3(60.6f, 0.10f, -28f);
    internal static readonly Vector3 Forward = new Vector3(0f, 0f, 1f);

    // 台面：宽 4.8、走道 1.2。中心线写死在这里，目标点和巡逻线全部由它推出来 ——
    // 三处（几何 / 目标 / 路线）读同一组数，改一处不会互相错位。
    internal static readonly float[] BenchX = { 45.6f, 51.6f, 57.6f, 63.6f, 69.6f, 75.6f };
    internal const float BenchW = 4.8f;
    internal const float AisleW = 1.2f;

    // 五条走道的中轴
    internal const float Ax0 = 48.6f, Ax1 = 54.6f, Ax2 = 60.6f, Ax3 = 66.6f, Ax4 = 72.6f;

    // 台面长度 / 横通道
    internal const float BenchS_Z0 = -26f, BenchS_Z1 = -18.5f;   // 南段
    internal const float BenchN_Z0 = -13.5f, BenchN_Z1 = -6f;    // 北段

    // ───────────────────────── 调色板 ─────────────────────────
    // 冷白日光灯 + 不锈钢。比后院亮一些，但亮的地方只有灯下 ——
    // 其余部分是纯黑，这正是"看得见的地方更危险"。
    static readonly Color FloorCol = new Color(0.26f, 0.27f, 0.28f);
    static readonly Color Steel    = new Color(0.22f, 0.23f, 0.24f);
    static readonly Color SteelTop = new Color(0.31f, 0.32f, 0.33f);
    static readonly Color WallK    = new Color(0.30f, 0.31f, 0.32f);
    static readonly Color Crate    = new Color(0.28f, 0.21f, 0.13f);
    static readonly Color Fridge   = new Color(0.35f, 0.36f, 0.37f);
    static readonly Color Sink     = new Color(0.16f, 0.18f, 0.19f);
    static readonly Color Tube     = new Color(1.00f, 0.96f, 0.84f);
    static readonly Color Pool     = new Color(0.42f, 0.40f, 0.34f);

    // ───────────────────────── 10 个目标点 ─────────────────────────
    // 顺序仍然按"离出生点由近到远"。注意第 4 个开始就全部落在**侧走道**里，
    // 主走道（x = 60.6）只在第 1 和第 10 个点上出现 —— 那是玩家的动线，不是目标的位置。

    internal static readonly Vector3[] Spots =
    {
        new Vector3(Ax2, 0f, -24f),    // 主走道，门口                          4.0 m
        new Vector3(Ax1, 0f, -24f),    // 西一                                  7.2 m
        new Vector3(Ax3, 0f, -24f),    // 东一                                  7.2 m
        new Vector3(Ax0, 0f, -21f),    // 最西                                  14.2 m
        new Vector3(Ax4, 0f, -21f),    // 最东                                  14.2 m
        new Vector3(Ax1, 0f, -12f),    // 西一·北段（横通道北侧）                16.9 m
        new Vector3(Ax3, 0f, -12f),    // 东一·北段                             16.9 m
        new Vector3(Ax0, 0f, -9f),     // 最西·北段（冰箱后）                    22.2 m
        new Vector3(Ax4, 0f, -9f),     // 最东·北段                             22.2 m
        new Vector3(Ax2, 0f, -6f),     // 主走道尽头（正对后门）                  22.0 m
    };

    // ───────────────────────── 10 条巡逻路线 ─────────────────────────
    // 五条走道各切南北两段 ⇒ 正好 10 条。猫在第 10 关会同时占住五条纵向道，
    // 玩家唯一的横向通路只剩中间那条 5 m 宽的横通道 —— 那是最危险的一段。

    internal static readonly Vector3[][] Routes =
    {
        new[] { new Vector3(Ax1, 0f, -25f), new Vector3(Ax1, 0f, -19f) },
        new[] { new Vector3(Ax1, 0f, -13f), new Vector3(Ax1, 0f,  -7f) },
        new[] { new Vector3(Ax3, 0f, -25f), new Vector3(Ax3, 0f, -19f) },
        new[] { new Vector3(Ax3, 0f, -13f), new Vector3(Ax3, 0f,  -7f) },
        new[] { new Vector3(Ax0, 0f, -25f), new Vector3(Ax0, 0f, -19f) },
        new[] { new Vector3(Ax0, 0f, -13f), new Vector3(Ax0, 0f,  -7f) },
        new[] { new Vector3(Ax4, 0f, -25f), new Vector3(Ax4, 0f, -19f) },
        new[] { new Vector3(Ax4, 0f, -13f), new Vector3(Ax4, 0f,  -7f) },
        new[] { new Vector3(Ax2, 0f, -25f), new Vector3(Ax2, 0f, -19f) },
        new[] { new Vector3(Ax2, 0f, -13f), new Vector3(Ax2, 0f,  -7f) },
    };

    // ───────────────────────── 灯管（光区）─────────────────────────
    // 八个位置：四条侧走道 × 南北两段。**主走道一根都没有**。
    // 灯下 4.4 × 4.4 m 是亮的，站进去累积曝光；这块亮斑同时也画在地上，
    // 玩家能看见边界 —— 致命区不可见是最糟的设计，那叫背板不是关卡。

    internal static readonly Vector2[] Lamps =
    {
        new Vector2(Ax0, -22f), new Vector2(Ax0, -10f),
        new Vector2(Ax1, -22f), new Vector2(Ax1, -10f),
        new Vector2(Ax3, -22f), new Vector2(Ax3, -10f),
        new Vector2(Ax4, -22f), new Vector2(Ax4, -10f),
    };

    internal const float PoolHalf = 2.2f;

    // ───────────────────────── 写进当前场景 ─────────────────────────

    internal static void Setup()
    {
        NbScene.Name  = "KITCHEN AT NIGHT";
        NbScene.Code  = "KITCHEN";
        NbScene.Blurb = "Six benches.  Five aisles.  Nothing keeps.";

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
        Shell(p);
        Benches(p);
        Props(p);
        Lamps3D(p);

        Debug.LogFormat("[Nb] 厨房就位：{0:F0} × {1:F0} m，5 条 {2:F1} m 走道，" +
                        "{3} 个目标点、{4} 段巡逻线、{5} 根灯管（主走道 0 根）。",
            XMax - XMin, ZMax - ZMin, AisleW, Spots.Length, Routes.Length, Lamps.Length);
    }

    static void Shell(Transform p)
    {
        // 地板（实体）。台面高 1.0，而玩家只有 0.3 —— 地面必须清楚，
        // 否则在纯黑里连"我在走道中间还是贴着台面"都判断不了。
        Nb.Box(p, "Kitchen Floor", new Vector3(CX, -0.20f, CZ),
            new Vector3(XMax - XMin, 0.40f, ZMax - ZMin), FloorCol, true);

        const float h = 3.0f, t = 0.40f;
        Nb.Box(p, "Kitchen Wall S", new Vector3(CX, h * 0.5f, ZMin + t * 0.5f),
            new Vector3(XMax - XMin, h, t), WallK, true);
        Nb.Box(p, "Kitchen Wall N", new Vector3(CX, h * 0.5f, ZMax - t * 0.5f),
            new Vector3(XMax - XMin, h, t), WallK, true);
        Nb.Box(p, "Kitchen Wall W", new Vector3(XMin + t * 0.5f, h * 0.5f, CZ),
            new Vector3(t, h, ZMax - ZMin), WallK, true);
        Nb.Box(p, "Kitchen Wall E", new Vector3(XMax - t * 0.5f, h * 0.5f, CZ),
            new Vector3(t, h, ZMax - ZMin), WallK, true);

        // 天花板：只是视觉封顶（非实体），挡不住任何东西 —— 它的作用是让"上方有东西"这件事成立，
        // 不然抬头是一片虚空，1.2 m 走道的压迫感会整个消失。
        Nb.Box(p, "Kitchen Ceiling", new Vector3(CX, h - 0.05f, CZ),
            new Vector3(XMax - XMin, 0.10f, ZMax - ZMin), new Color(0.09f, 0.09f, 0.10f));
    }

    /// 六条台面，每条切成南北两段，中间留 5 m 横通道。
    static void Benches(Transform p)
    {
        float southLen = BenchS_Z1 - BenchS_Z0;
        float southZ = (BenchS_Z0 + BenchS_Z1) * 0.5f;
        float northLen = BenchN_Z1 - BenchN_Z0;
        float northZ = (BenchN_Z0 + BenchN_Z1) * 0.5f;

        for (int i = 0; i < BenchX.Length; i++)
        {
            float x = BenchX[i];

            Nb.Box(p, "Bench S" + i, new Vector3(x, 0.50f, southZ),
                new Vector3(BenchW, 1.00f, southLen), Steel, true);
            Nb.Box(p, "Bench S" + i + " top", new Vector3(x, 1.03f, southZ),
                new Vector3(BenchW + 0.10f, 0.06f, southLen + 0.10f), SteelTop);

            Nb.Box(p, "Bench N" + i, new Vector3(x, 0.50f, northZ),
                new Vector3(BenchW, 1.00f, northLen), Steel, true);
            Nb.Box(p, "Bench N" + i + " top", new Vector3(x, 1.03f, northZ),
                new Vector3(BenchW + 0.10f, 0.06f, northLen + 0.10f), SteelTop);
        }
    }

    /// 道具：冰箱（最西）、水池（最东）、纸箱堆（横通道里，会挡路也会挡视线）。
    static void Props(Transform p)
    {
        // 冰箱：贴着西墙，正好挡住最西走道的北段尽头
        Nb.Box(p, "Fridge", new Vector3(XMin + 1.1f, 1.05f, -7.5f),
            new Vector3(1.8f, 2.10f, 2.0f), Fridge, true);
        Nb.Box(p, "Fridge Door", new Vector3(XMin + 2.02f, 1.10f, -7.5f),
            new Vector3(0.06f, 1.80f, 1.70f), new Color(0.18f, 0.19f, 0.20f));

        // 水池：东墙
        Nb.Box(p, "Sink", new Vector3(XMax - 1.2f, 0.52f, -7.5f),
            new Vector3(2.0f, 1.04f, 2.2f), Sink, true);
        Nb.Box(p, "Sink Rim", new Vector3(XMax - 1.2f, 1.06f, -7.5f),
            new Vector3(2.2f, 0.06f, 2.4f), SteelTop);

        // 纸箱堆：横通道里两组，玩家横穿时必须绕 —— 也挡住横向的视线
        Crates(p, 52.0f, -16.0f, 2);
        Crates(p, 68.5f, -15.6f, 3);

        // 地漏 / 排水沟：纯装饰，给地面一点纹理（否则一条 22 m 的长走道毫无距离感）
        for (int i = 0; i < 6; i++)
            Nb.Box(p, "Drain " + i, new Vector3(Ax2, 0.006f, -27f + i * 4.0f),
                new Vector3(0.9f, 0.012f, 0.5f), Sink);
    }

    static void Crates(Transform p, float x, float z, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float dx = (i % 2) * 1.3f;
            float dz = (i / 2) * 1.3f;
            float h = 1.0f + (i == 2 ? 0.9f : 0f);
            Nb.Box(p, "Crate " + x + "_" + i, new Vector3(x + dx, h * 0.5f, z + dz),
                new Vector3(1.2f, h, 1.2f), Crate, true);
        }
    }

    /// 灯管：自发光的细长条 + 地上一块亮斑。
    ///
    /// **亮斑不是装饰，是判定区的可视化。** 玩家站进去会累积曝光，
    /// 而那块 4.4 × 4.4 的方形亮斑就是判定的边界 —— 两者必须一样大。
    /// 只做光、不做亮斑，玩家就只能靠死记位置，那这关会变成背板。
    static void Lamps3D(Transform p)
    {
        for (int i = 0; i < Lamps.Length; i++)
        {
            Vector2 at = Lamps[i];

            GameObject tube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tube.name = "Tube " + i;
            tube.transform.SetParent(p, false);
            tube.transform.localPosition = new Vector3(at.x, 2.72f, at.y);
            tube.transform.localScale = new Vector3(0.16f, 0.10f, 3.2f);
            Nb.Kill(tube.GetComponent<Collider>());
            Renderer tr = tube.GetComponent<Renderer>();
            if (tr != null) tr.sharedMaterial = Nb.MatEmissive(Tube, 1.35f);
            tube.layer = Nb.DecorLayer >= 0 ? Nb.DecorLayer : 0;

            // 地面亮斑（非实体：玩家要能踩进去，只是"被看见"）
            Nb.Box(p, "Light Pool " + i, new Vector3(at.x, 0.010f, at.y),
                new Vector3(PoolHalf * 2f, 0.012f, PoolHalf * 2f), Pool);
        }
    }

    /// 目标点是否落在某个灯管的光区里。NbGame 每帧问它一次，用来累积曝光。
    internal static bool InLight(Vector3 p, int lampIndex)
    {
        Vector2 at = Lamps[lampIndex];
        return Mathf.Abs(p.x - at.x) <= PoolHalf && Mathf.Abs(p.z - at.y) <= PoolHalf;
    }
}
