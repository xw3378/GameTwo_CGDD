// NbFridge.cs — 场景 03「冰箱里」
//
// ───────────────────────── 为什么这一关和前两关完全不同 ─────────────────────────
// 街区/后院的威胁是**会动的**（猫、保安），这一关的威胁是**会开的门**：
// 没有人追你，但每隔几秒冰箱门会被打开一次，强光灌进来。
// 门开之前有 3 秒预警，你要在这 3 秒里跑到掩体后面 —— 慢一步就是 -10 血。
//
// 所以空间必须提供**可数的掩体**，而不是一片空地：
//   · 每层 4 个掩体（牛奶箱 / 西瓜 / 面包箱 / 黄油块），高 0.4~0.5 m
//   · 掩体必须**比玩家高**（玩家 0.30 m）—— 否则门光从头顶越过去，躲了也没用
//   · 掩体必须**分散**：贴着同一面墙排成一行，玩家会一直站在墙边不动
//
// ───────────────────────── 六层怎么上去（这张图是整关的关键） ─────────────────────────
//
//       门 z = ZMax                                 后壁 z = ZMin
//   ┌──────────────────────────────────────────────────┐
//   │  前厅（头顶全空）                                   │
//   ├──────────────────────────────────────────────────┤  ← L6 板
//   │  ▓▓▓▓楼梯▓▓▓▓            架板 L6                  │
//   ├──────────────────────────────────────────────────┤  ← L5 板
//   │  ▓▓▓▓楼梯▓▓▓▓       架板 L5                       │
//   ├──────────────────────────────────────────────────┤
//   │      ... 每层重复 ...                             │
//   └──────────────────────────────────────────────────┘
//
// **2026-10-17 改版：从"跳上去"改成"走上去"。**
// 旧版是让玩家在板前沿按空格跳上去（靠跳跃倍率 1.75 硬跳 0.56 m）。实测跳不上去 ——
// 所以现在每层之间是一段**真正的楼梯**：4 级、每级 0.14 m、踏面 0.30 m，
// 正好铺满"上一层板比这一层往里缩的那 1.2 m"。玩家走过去就上去了，不需要按空格。
//
// 楼梯宽 8 m、居中（x 51~59）：出生点正对着它，不可能找不到。
// 冰箱收窄到 24 m 之后，10 m 的楼梯会占到前沿的 42% —— 两侧各只剩 6.7 m 的活动面，
// 落地后想绕开楼梯去房间另一侧要贴着墙走。8 m 正好是宽度的三分之一。
// 而**不能**做成整宽 —— 楼梯如果横贯整个前沿，它就变成一堵墙，
// 每层房间里的人被自己头上的那段楼梯挡住，永远进不去。
//
// 结构上依赖一件事：层高 0.56 必须整除成 4×0.14，且每层前沿往里缩的量 1.2 必须
// 正好等于 4×0.30。改任一个数都要同时改另两个，否则最后一级台阶接不上板边（会有一个洞）。
//
// 层高 0.56、玩家 0.30 高 ⇒ 净空 0.51 m：够站、够走，层内跳不起来（会撞头）。
// 楼梯靠 CharacterController.stepOffset 上行，冰箱里这个值从 0.08 抬到 0.17
// （NbPlayer.SetStepUp）—— 0.14 的踏步必须小于它，否则会变成"贴着台阶推不动"。

// ───────────────────────── 门 ─────────────────────────
// 门板绕左侧铰链向外开 105°，同时一盏平行光从门口扫进来（NbGame 每帧渐变强度）。
// 那盏灯是**运行时**才创建的，而且默认 intensity = 0 ——
// NbLighting.Apply 会把场上最亮的平行光当主光、其余全关掉，门光如果先存在，
// 主光就可能变成它（它平时是 0），整关的布光会当场错位。见 CreateDoorLight。

using UnityEngine;

internal static class NbFridge
{
    // ───────────────────────── 范围 ─────────────────────────

    // **2026-10-17 收窄：30 m → 24 m。** 30×20 的空地对一只 0.30 m 高的老鼠
    // 是一片仓库：站在门口看不见两侧的墙，掩体之间也远到"3 s 预警跑不过去"。
    //
    // 收窄只动这两个数 —— 所有横向位置都写成净宽的比例（见 Px），
    // 不再有 45.5 / 47.0 这样的世界坐标字面量。
    internal const float XMin = 43f, XMax = 67f;     // 24 m 宽
    internal const float ZMin = -20f, ZMax = 0f;     // 20 m 深，门在 ZMax 这一侧

    internal const float CX = (XMin + XMax) * 0.5f;  // 55
    internal const float CZ = (ZMin + ZMax) * 0.5f;  // −10

    internal const int Layers = 6;
    internal const float LayerH = 0.56f;             // 层高（板顶到板顶）
    internal const float Slab = 0.05f;               // 板厚
    internal const float WallT = 0.30f;
    internal const float CeilY = LayerH * (Layers + 1);   // 3.92：最高一层板顶 + 一个净空

    // ───────────────────────── 横向比例 ─────────────────────────

    /// 左右内壁之间的净宽（去掉两侧壁厚）。24 m 的外宽 ⇒ 23.4 m 可摆东西。
    internal const float InnerW = XMax - XMin - WallT * 2f;

    /// 横向比例换算：0 = 左内壁，1 = 右内壁。
    ///
    /// **掩体、奶酪点、商品堆头全部用它写**，不再写世界 x。
    /// 原来这些位置是 45.5 / 47.0 / 63.5 这样的字面量，一改宽度就得逐个手算，
    /// 漏掉一个的后果分别是"掩体穿进墙里""奶酪落在楼梯上""商品浮在墙外" ——
    /// 三种都不会报错，只会在画面里慢慢露出来。
    internal static float Px(float f) { return XMin + WallT + f * InnerW; }

    // ───────────────────────── 楼梯 ─────────────────────────
    //
    // 层高 0.56 = 4 级 × 0.14；每层前沿往里缩 1.2 = 4 级 × 0.30。
    // 这两个等式是**绑死**的：改层高就得改踏步数或踏步高，改缩进就得改踏面宽，
    // 否则最后一级台阶接不上上一层板的边缘，中间会留一条能掉下去的缝。
    internal const int   StepsPerLayer = 4;
    internal const float StepRise = 0.14f;
    internal const float StepRun  = 0.30f;
    internal const float Recede    = 1.2f;           // = StepsPerLayer * StepRun

    /// 楼梯宽度。居中的 8 m（24 m 宽的 1/3）—— 出生点正对，走两步就撞上，不存在"找不到路"。
    /// 不能整宽：整宽的楼梯就是每层房间门口的一堵墙。
    internal const float StairX0 = 51f, StairX1 = 59f;

    /// 第 n 层（1 起）的板顶高度。n=1 ⇒ 0.56
    internal static float Top(int n) { return LayerH * n; }

    /// 从**脚底高度**反推"第几层"：1 = 底层地板，`Layers + 1` = 最顶层（7）。
    ///
    /// HUD 的 "SHELF n" 和定位器方块里的数字**必须共用这一个函数**。
    /// 两处各写一份的话必然会在楼梯上打架（一个取整、一个 floor，差一级），
    /// 玩家看到"HUD 说 3 层、地图说 4 层"只会以为定位器坏了。
    /// 用 Round 而不是 Floor：0.56 的整数倍在 float 里常常是 0.5599999，
    /// floor 会把它算成下一层。
    internal static int ShelfOf(float y)
    {
        if (y <= 0f) return 1;
        return Mathf.Clamp(Mathf.RoundToInt(y / LayerH) + 1, 1, Layers + 1);
    }

    /// 第 n 层（1 起）的板前沿 z。**往里缩 1.2 m / 层** —— 见文件头。
    internal static float FrontZ(int n) { return ZMax - (4.5f + (n - 1) * Recede); }

    /// 第 n 层**能自由走动**的最前沿 z（再往前就是通往上一层的楼梯，或者顶层的外沿）。
    /// 摆掩体、摆装饰都用它来夹范围 —— 写死数字会在改了 Recede 之后全部错位。
    internal static float FlatFrontZ(int n) { return FrontZ(Mathf.Min(n + 1, Layers)); }

    // 常用高度，写成 const 是为了在 Spots 里能直接当字面量用
    const float Y1 = LayerH, Y2 = LayerH * 2, Y3 = LayerH * 3, Y4 = LayerH * 4, Y5 = LayerH * 5,
                Y6 = LayerH * 6;

    // 出生点在门内侧的底层地面上，朝里（−z）。一进来就看见六层板一层层退进去。
    internal static readonly Vector3 Start = new Vector3(CX, 0.02f, -1.8f);
    internal static readonly Vector3 Forward = new Vector3(0f, 0f, -1f);

    // ───────────────────────── 调色板 ─────────────────────────
    // 冰箱内部是"冷白塑料 + 淡蓝光"，所有颜色都比真实世界低一档饱和，
    // 否则在冷藏灯下会糊成一片白。

    static readonly Color ShellCol = new Color(0.66f, 0.68f, 0.70f);  // 内壁
    static readonly Color ShellD  = new Color(0.44f, 0.46f, 0.49f);   // 内壁阴影侧 / 后壁
    static readonly Color Shelf   = new Color(0.74f, 0.77f, 0.79f);   // 搁板（半透明的错觉靠亮）
    static readonly Color ShelfE  = new Color(0.52f, 0.56f, 0.60f);   // 搁板前沿卷边
    static readonly Color Trim    = new Color(0.30f, 0.33f, 0.36f);
    static readonly Color DoorCol = new Color(0.78f, 0.80f, 0.81f);
    static readonly Color DoorIn  = new Color(0.62f, 0.65f, 0.67f);
    static readonly Color Lamp    = new Color(0.86f, 0.94f, 1.00f);
    static readonly Color Crate   = new Color(0.36f, 0.40f, 0.44f);
    static readonly Color Carton  = new Color(0.80f, 0.80f, 0.76f);
    static readonly Color CartonB = new Color(0.14f, 0.24f, 0.42f);
    static readonly Color Melon   = new Color(0.16f, 0.32f, 0.15f);
    static readonly Color MelonL  = new Color(0.22f, 0.40f, 0.18f);
    static readonly Color Bread   = new Color(0.44f, 0.29f, 0.14f);
    static readonly Color Butter  = new Color(0.84f, 0.76f, 0.42f);
    static readonly Color StepCol = new Color(0.58f, 0.62f, 0.66f);   // 台阶侧面（比板暗一档，才有体积感）
    static readonly Color StepEdge= new Color(0.40f, 0.76f, 0.92f);   // 踏面前沿的冷蓝亮线

    // ───────────────────────── 12 个食物点 ─────────────────────────
    //
    // **顺序 = 出场顺序 = 由低到高。** 第 n 关取前 Cheese 个点，所以：
    //   第 1 关两块在底层（家门口，先学会看门的节奏）
    //   第 3 关第一次要爬楼梯上到第 1 层（4 块：底 2 + L1 2）
    //   第 10 关 12 块铺满六层，一路爬到顶层
    // 改顺序 = 难度曲线倒过来，而且第 1 关会变成"先爬三层"。
    //
    // 每个点的 y 是该层**板顶**，SpawnCheese 对冰箱场景不做贴地（见 UsesDoor 分支）——
    // 贴地会把所有点压回 y=0，六层全塌成一层。
    //
    // x 一律写成 Px(比例)。比例取自"收窄前"的位置（原净宽 29.4 m），
    // 所以 24 m 之后这 12 个点的**相对站位完全不变**，只是整体压紧了一点。

    internal static readonly Vector3[] Spots =
    {
        new Vector3(Px(0.398f), 0f,  -6f),    // 底层 · 近
        new Vector3(Px(0.704f), 0f, -13f),    // 底层 · 远
        new Vector3(Px(0.330f), Y1,  -7f),    // L1
        new Vector3(Px(0.772f), Y1, -12f),    // L1
        new Vector3(Px(0.670f), Y2,  -9f),    // L2
        new Vector3(Px(0.228f), Y2, -12f),    // L2
        new Vector3(Px(0.500f), Y3, -15f),    // L3
        new Vector3(Px(0.806f), Y3, -13f),    // L3
        new Vector3(Px(0.330f), Y4, -16f),    // L4
        new Vector3(Px(0.704f), Y4, -18f),    // L4
        new Vector3(Px(0.534f), Y5, -13f),    // L5
        new Vector3(Px(0.300f), Y6, -15f),    // L6 · 顶层（楼梯直通到这里，最后一关才用得上）
    };

    // 这一关没有猫：威胁只有门。NbScene.Routes 留空，SpawnCats 会摆 0 只。
    internal static readonly Vector3[][] NoRoutes = new Vector3[0][];

    // ───────────────────────── 写进当前场景 ─────────────────────────

    internal static void Setup()
    {
        NbScene.Name  = "INSIDE THE FRIDGE";
        NbScene.Code  = "FRIDGE";
        NbScene.Blurb = "Six shelves.  One door.  Nowhere to stand in the light.";

        NbScene.XMin = XMin;
        NbScene.XMax = XMax;
        NbScene.ZMin = ZMin;
        NbScene.ZMax = ZMax;

        NbScene.Start   = Start;
        NbScene.Forward = Forward;
        NbScene.Targets = Spots;
        NbScene.Routes  = NoRoutes;
        NbScene.MaxTargets = Spots.Length;
        NbScene.MaxRoutes  = 0;
    }

    // ───────────────────────── 搭场景 ─────────────────────────

    internal static Transform DoorHinge;
    internal static Light DoorLight;

    internal static void Build(Transform p)
    {
        Shell(p);
        Shelves(p);
        Covers(p);
        Door(p);
        Outside(p);

        Debug.LogFormat("[Nb] 冰箱就位：{0:F0} × {1:F0} m，{2} 层板（层高 {3:F2}，净空 {4:F2}）；" +
                        "楼梯 {5} 级（每级 {6:F2} 高 / {7:F2} 深，宽 {8:F0} m，居中）；{9} 个奶酪点、0 只猫。",
            XMax - XMin, ZMax - ZMin, Layers, LayerH, LayerH - Slab,
            StepsPerLayer * Layers, StepRise, StepRun, StairX1 - StairX0, Spots.Length);
    }

    /// 内壁：底、顶、两侧、后壁。**全部实体**（玩家撞得着、门光也被它们收住）。
    static void Shell(Transform p)
    {
        // 底板：顶面正好 y = 0，玩家出生就站在上面
        Nb.Box(p, "Fridge Floor", new Vector3(CX, -WallT * 0.5f, CZ),
            new Vector3(XMax - XMin, WallT, ZMax - ZMin), ShellD, true);

        Nb.Box(p, "Fridge Ceiling", new Vector3(CX, CeilY + WallT * 0.5f, CZ),
            new Vector3(XMax - XMin, WallT, ZMax - ZMin), ShellCol, true);

        Nb.Box(p, "Fridge Wall W", new Vector3(XMin + WallT * 0.5f, CeilY * 0.5f, CZ),
            new Vector3(WallT, CeilY, ZMax - ZMin), ShellCol, true);
        Nb.Box(p, "Fridge Wall E", new Vector3(XMax - WallT * 0.5f, CeilY * 0.5f, CZ),
            new Vector3(WallT, CeilY, ZMax - ZMin), ShellCol, true);
        Nb.Box(p, "Fridge Wall N", new Vector3(CX, CeilY * 0.5f, ZMin + WallT * 0.5f),
            new Vector3(XMax - XMin, CeilY, WallT), ShellD, true);

        // 后壁的竖条纹：冰箱内胆的压花。纯视觉，加碰撞体没意义（后壁已经挡着了）。
        for (int i = 0; i < 11; i++)
        {
            float x = XMin + 2.6f + i * 2.5f;
            Nb.Box(p, "Rib " + i, new Vector3(x, CeilY * 0.5f, ZMin + WallT + 0.02f),
                new Vector3(0.16f, CeilY - 0.5f, 0.04f), ShellCol, false);
        }
    }

    /// 六层搁板。每层一块实体薄板 —— 它同时是地板、天花板和"光的遮挡"。
    /// 每层前沿往里缩 1.2 m，缩出来的那一条就是**通往上一层的楼梯所占的坑位**（见 Stairs）。
    static void Shelves(Transform p)
    {
        for (int n = 1; n <= Layers; n++)
        {
            float front = FrontZ(n);
            float z0 = ZMin + WallT;
            float depth = front - z0;
            if (depth <= 1.0f) continue;      // 前沿退到头了就不要再放板

            float y = Top(n);

            Nb.Box(p, "Shelf L" + n, new Vector3(CX, y - Slab * 0.5f, z0 + depth * 0.5f),
                new Vector3(XMax - XMin - WallT * 2f, Slab, depth), Shelf, true);

            // 前沿卷边：比板稍微高一点点，是玩家用眼睛判断"这里到边了"的唯一参照。
            // **楼梯那 8 m 要断开** —— 不断的话，最上面一级台阶的落点正好被一条
            // 3.6 cm 的坎挡住，走上去会顿一下（0.14 的踏步配一条 0.036 的坎，很难解释）。
            float wl = StairX0 - (XMin + WallT);
            if (wl > 0.2f)
                Nb.Box(p, "Shelf Lip L" + n + " W", new Vector3(XMin + WallT + wl * 0.5f, y + 0.018f, front - 0.05f),
                    new Vector3(wl, 0.036f, 0.10f), ShelfE, false);
            float wr = (XMax - WallT) - StairX1;
            if (wr > 0.2f)
                Nb.Box(p, "Shelf Lip L" + n + " E", new Vector3(StairX1 + wr * 0.5f, y + 0.018f, front - 0.05f),
                    new Vector3(wr, 0.036f, 0.10f), ShelfE, false);

            // 板下的一根加强筋：低头看这块板时，它给出"我在板下面"的明确证据
            Nb.Box(p, "Shelf Rib L" + n, new Vector3(CX, y - Slab - 0.03f, z0 + depth * 0.5f),
                new Vector3(XMax - XMin - WallT * 2.6f, 0.05f, 0.22f), ShelfE, false);
        }

        Stairs(p);

        // 每层的灯带（自发光，只负责视觉；真正照度来自 NbLighting 的平行光）。
        // 从"前沿−1.2"挪进房间：那个位置现在是楼梯，灯管会横在台阶上方。
        for (int n = 1; n <= Layers; n++)
        {
            float y = Top(n) - 0.09f;
            Nb.Box(p, "Fridge Lamp L" + n, new Vector3(CX, y, FlatFrontZ(n) - 0.90f),
                new Vector3(XMax - XMin - WallT * 2f - 1.5f, 0.05f, 0.16f), Lamp, false);
        }
    }

    /// 从地面一直到顶层的一段楼梯（每层 4 级）。
    ///
    /// 位置由两个量决定：第 n 层通往 n+1 层的那一段，占 z ∈ [FrontZ(n+1), FrontZ(n+1)+1.2]。
    /// 也就是"上一层板往里缩出来的那条坑" —— 那里的头顶是空的，所以站得下人；
    /// 而台阶的最深一级，板顶正好等于上一层的板顶，接缝在 FrontZ(n+1) 处对齐。
    ///
    /// 每一级都是一块**落到本层地面**的实体方块（不是架空的一片板）：
    /// 架空的话走上去会看见台阶侧面一条缝，而且缝里能看见下一层。
    static void Stairs(Transform p)
    {
        float w = StairX1 - StairX0;

        for (int n = 0; n < Layers; n++)          // n = 起点所在层（0 = 地面）
        {
            float y0 = Top(n);
            float zEnd = FrontZ(n + 1);          // 最深一级的位置，正好接上一层板边

            for (int k = 1; k <= StepsPerLayer; k++)
            {
                float top = y0 + k * StepRise;
                float z0 = zEnd + (StepsPerLayer - k) * StepRun;   // 这一级的近边
                float cz = z0 + StepRun * 0.5f;

                Nb.Box(p, "Step L" + (n + 1) + "." + k,
                    new Vector3((StairX0 + StairX1) * 0.5f, (y0 + top) * 0.5f, cz),
                    new Vector3(w, top - y0, StepRun), StepCol, true);

                // 踏面前沿的一道亮线：24 级台阶排成一条斜线，从下往上看就是一条"路"。
                // 没有它，六层灰板在冷光下会糊成一堵墙。
                Nb.Box(p, "Step Edge L" + (n + 1) + "." + k,
                    new Vector3((StairX0 + StairX1) * 0.5f, top + 0.006f, z0 + StepRun - 0.045f),
                    new Vector3(w, 0.012f, 0.09f), StepEdge, false);
            }
        }
    }

    /// 掩体。**每层六个**：房间里四个（位置错开、高 0.40~0.50 m）+ 前沿一对（见下）。
    /// 高度上限 0.50 是硬的：层内净空只有 0.51，再高就会插进上一层板里，
    /// 玩家会看到一块西瓜从地板里穿出来 —— 而且它顶住板之后，玩家会被挤在缝里。
    static void Covers(Transform p)
    {
        // n 从 0 起：**地面层也要有掩体**。开局玩家就站在门里，如果脚下这一层
        // 一个能躲的东西都没有，门第一次开的时候他连"该往哪跑"都没有答案 ——
        // 只能往上爬一层，而 3 秒不够。地面层的掩体摆在货架底下（z ≤ −6.5），
        // 于是"钻到板子底下"这件事本身也成了躲法。
        for (int n = 0; n <= Layers; n++)
        {
            float y = Top(n);

            // 6 个掩体按**这一层实际可用的进深**铺成 3 排 × 2 列，不写死 z。
            //
            // 楼层越高房间越浅（FrontZ 每层退 1.2 m）：地面层可用 10.8 m，
            // 到第 5/6 层只剩 4.8 m。写死 z 的话最后两层会有掩体叠在一起，
            // 叠起来的东西既挡路又不好看 —— 而且两个正好同高的方块还会共面闪。
            float zMin = ZMin + WallT + 2.4f;        // 后壁留 2.4 m 给商品货架
            float zMax = FlatFrontZ(n) - 2.0f;       // 前沿留 2 m 给楼梯
            if (zMax - zMin < 1.0f) continue;
            float d = zMax - zMin;

            // 最后那排贴着 zMax：玩家从楼梯落地在 FrontZ(n)+0.15，
            // 走到这一排约 3.2 m（≈1.4 s），是"听到预警还来得及"的量级。
            // 原来最近的掩体在 5 m 开外，预警 3 s 一响就只剩挨打。
            //
            // x 写成净宽的比例（Px），跟奶酪点同一套坐标 —— 这样"掩体是否压住奶酪"
            // 在任何宽度下都只是同一组数字的比较，不会因为改了 XMin/XMax 而失效。
            CoverAt(p, n, y, Px(0.177f), zMin + d * 0.12f, 0);
            CoverAt(p, n, y, Px(0.891f), zMin + d * 0.12f, 3);
            CoverAt(p, n, y, Px(0.449f), zMin + d * 0.50f, 1);
            CoverAt(p, n, y, Px(0.228f), zMin + d * 0.50f, 2);
            CoverAt(p, n, y, Px(0.755f), zMin + d * 0.88f, 2);
            CoverAt(p, n, y, Px(0.228f), zMin + d * 0.88f, 0);
        }
    }

    static void CoverAt(Transform p, int layer, float y, float x, float z, int variant)
    {
        // 夹进这一层的**房间**范围：后壁那头要给商品货架留 2.4 m，前沿那头要避开楼梯 2 m。
        // 写死数字的话，改了 Recede / LayerH 之后掩体会跑到台阶上去（那样它会变成一个
        // 谁都能站的观景台，而门光从它头顶越过 —— 这一关就废了）。
        float zMin = ZMin + WallT + 2.4f;
        float zMax = FlatFrontZ(layer) - 2.0f;
        if (zMax - zMin < 1.0f) return;
        z = Mathf.Clamp(z, zMin, zMax);

        string n = "Cover L" + layer + "." + variant;

        switch (variant)
        {
            case 0:   // 牛奶箱：三只纸盒并排，最经典的可躲物
                for (int i = 0; i < 3; i++)
                    Nb.Box(p, n + " carton " + i,
                        new Vector3(x + (i - 1) * 0.52f, y + 0.24f, z),
                        new Vector3(0.46f, 0.48f, 0.42f), Carton, true);
                Nb.Box(p, n + " band", new Vector3(x, y + 0.10f, z + 0.215f),
                    new Vector3(1.60f, 0.10f, 0.02f), CartonB, false);
                break;

            case 1:   // 西瓜：圆的，绕过去要跑半圈 —— 是四样里最好用的
                Nb.Ball(p, n + " melon", new Vector3(x, y + 0.24f, z), 0.50f, Melon, true);
                Nb.Ball(p, n + " stripe", new Vector3(x, y + 0.24f, z), 0.515f, MelonL, false);
                break;

            case 2:   // 面包箱：长条，能一次挡住两个人宽的光
                Nb.Box(p, n + " box", new Vector3(x, y + 0.22f, z),
                    new Vector3(1.35f, 0.44f, 0.55f), Bread, true);
                Nb.Box(p, n + " lid", new Vector3(x, y + 0.45f, z),
                    new Vector3(1.42f, 0.05f, 0.60f), new Color(0.30f, 0.19f, 0.09f), false);
                break;

            default:  // 黄油块：低一些（0.40），但宽
                Nb.Box(p, n + " butter", new Vector3(x, y + 0.20f, z),
                    new Vector3(1.10f, 0.40f, 0.80f), Butter, true);
                Nb.Box(p, n + " wrap", new Vector3(x, y + 0.405f, z),
                    new Vector3(1.14f, 0.02f, 0.84f), new Color(0.70f, 0.64f, 0.36f), false);
                break;
        }
    }

    /// 门：绕左侧铰链向外开 105°。板高到顶、宽到两侧，关着的时候就是一面墙。
    static void Door(Transform p)
    {
        float w = XMax - XMin - WallT * 2f;

        GameObject hinge = new GameObject("Fridge Door Hinge");
        hinge.transform.SetParent(p, false);
        hinge.transform.position = new Vector3(XMin + WallT, 0f, ZMax - 0.07f);
        hinge.transform.rotation = Quaternion.identity;
        DoorHinge = hinge.transform;

        // 门板挂在铰链的 +x 方向：旋转铰链 = 整块门绕左边缘转
        Nb.Box(hinge.transform, "Door Panel", new Vector3(w * 0.5f, CeilY * 0.5f, 0f),
            new Vector3(w, CeilY, 0.14f), DoorCol, true);
        Nb.Box(hinge.transform, "Door Inner", new Vector3(w * 0.5f, CeilY * 0.5f, -0.09f),
            new Vector3(w - 0.30f, CeilY - 0.30f, 0.04f), DoorIn, false);
        // 门内侧的两道横档（搁物架）：冰箱门的经典结构，也给"门开着"多一个可读的形状
        for (int i = 0; i < 2; i++)
        {
            float y = 1.10f + i * 1.30f;
            Nb.Box(hinge.transform, "Door Shelf " + i, new Vector3(w * 0.5f, y, -0.24f),
                new Vector3(w - 0.50f, 0.06f, 0.30f), DoorIn, false);
            Nb.Box(hinge.transform, "Door Shelf Lip " + i, new Vector3(w * 0.5f, y + 0.09f, -0.38f),
                new Vector3(w - 0.50f, 0.12f, 0.04f), DoorCol, false);
        }

        // 关门时门口那一圈密封条：玩家低头能看见自己站在门内侧
        Nb.Box(p, "Fridge Gasket W", new Vector3(XMin + WallT * 0.5f, CeilY * 0.5f, ZMax - 0.16f),
            new Vector3(WallT, CeilY, 0.08f), Trim, false);
        Nb.Box(p, "Fridge Gasket E", new Vector3(XMax - WallT * 0.5f, CeilY * 0.5f, ZMax - 0.16f),
            new Vector3(WallT, CeilY, 0.08f), Trim, false);
    }

    /// 门外：一小块地面 + 三面墙。
    /// 为什么需要它：门打开时如果外面是虚空，玩家会看到"冰箱浮在黑色里"，一眼出戏。
    /// 三面墙同时保证玩家不会在门开着的那两秒跑远（跑出去就回不来了）。
    static void Outside(Transform p)
    {
        float d = 7f, w = XMax - XMin + 4f;
        Nb.Box(p, "Kitchen Floor", new Vector3(CX, -0.16f, ZMax + d * 0.5f),
            new Vector3(w, 0.30f, d), new Color(0.30f, 0.29f, 0.27f), true);
        Nb.Box(p, "Kitchen Wall W", new Vector3(XMin - 2f, 2.2f, ZMax + d * 0.5f),
            new Vector3(0.4f, 4.4f, d), new Color(0.20f, 0.19f, 0.18f), true);
        Nb.Box(p, "Kitchen Wall E", new Vector3(XMax + 2f, 2.2f, ZMax + d * 0.5f),
            new Vector3(0.4f, 4.4f, d), new Color(0.20f, 0.19f, 0.18f), true);
        Nb.Box(p, "Kitchen Wall S", new Vector3(CX, 2.2f, ZMax + d),
            new Vector3(w, 4.4f, 0.4f), new Color(0.20f, 0.19f, 0.18f), true);
    }

    // ───────────────────────── 门光 ─────────────────────────

    /// **必须在 NbLighting.Apply 之后调用。**
    /// Apply 会把场上最亮的平行光当主光、其余全部强度归零并 disabled；
    /// 这盏门灯平时 intensity = 0，如果它先于 Apply 存在，排序就会把它当成"无关的平行光"关掉，
    /// 之后 SetDoor 再把 intensity 抬起来也不会亮（enabled 已经是 false）。
    internal static void CreateDoorLight(Transform root)
    {
        GameObject go = new GameObject("Fridge Door Light (auto)");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(CX, CeilY * 0.5f, ZMax + 5f);
        // 从门口朝里、带 28° 俯角：光会斜着打在每一层板的前沿上，
        // 于是"门开了"在画面上表现为**一排从门口往深处推的亮边**，一眼就懂。
        go.transform.rotation = Quaternion.Euler(28f, 180f, 0f);

        Light l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1.00f, 0.95f, 0.82f);
        l.intensity = 0f;
        l.shadows = LightShadows.None;      // 不开阴影：薄板 + 无贴图，开阴影只会出现条纹
        l.enabled = false;
        DoorLight = l;
    }

    static readonly Color AmbClosed = new Color(0.052f, 0.058f, 0.072f);
    static readonly Color AmbOpen   = new Color(0.200f, 0.195f, 0.180f);

    /// 门全开时的平行光强度。**不要往上调**：内壁是 0.66 的浅灰，
    /// 2.6 会把整屏推到纯白 —— 那一刻玩家最需要的恰恰是"看清自己站在哪"，
    /// 而不是"被闪光弹打了一下"。1.9 足够亮到认路，又还留着颜色。
    const float DoorLightMax = 1.9f;

    /// 开门程度：0 = 关，1 = 全开。门板角度、门光强度、环境光一起插值 ——
    /// 三样必须同一条曲线，只改其中一样就会出现"门还在关着但已经满屏白光"。
    internal static void SetDoor(float f)
    {
        f = Mathf.Clamp01(f);

        if (DoorHinge != null)
            DoorHinge.localRotation = Quaternion.Euler(0f, -105f * f, 0f);

        if (DoorLight != null)
        {
            float want = DoorLightMax * f;
            DoorLight.intensity = want;
            bool on = want > 0.02f;
            if (DoorLight.enabled != on) DoorLight.enabled = on;
        }

        RenderSettings.ambientLight = Color.Lerp(AmbClosed, AmbOpen, f);
    }
}
