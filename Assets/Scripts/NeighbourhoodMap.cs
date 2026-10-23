// @role Bottom-right minimap: player arrow, cat/cheese dots, scale bar, per-scene bounds.
// @calls NbGame NbScene NbCat NbCheese NbUi

// NeighbourhoodMap.cs — 右下角的方形定位盘
//
// 显示四件事：
//   ① 街区规模 —— 外框 + 右下角 20 m 比例尺 + 标题行的 "70.9 × 81.0 m"
//   ② 自己的位置 —— 青色三角箭头，朝向取**真实位移方向**（不是相机朝向）
//   ③ 敌人的位置 —— 每只猫一个红点（第 n 关 n 个），追你的时候变亮
//   ④ 奶酪的位置 —— 金色小点（第 n 关 n+1 个），被捡走就消失
//
// 点是一次性按场地容量（10 块 / 10 只）建好的，每帧按本关实际数量开关 ——
// 换关时不用重建 UI，少一个点就是"这一关没有那只猫"。
//
// 坐标映射：世界 -z 朝屏幕上方（玩家从大门进来是往"上"走），世界 +x 朝屏幕右方。
// 所以 py 用的是 (ZMax - z)，这是全图唯一容易搞反的地方。

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal class NbMap : MonoBehaviour
{
    internal static NbMap I;

    // 面板几何（单位：Canvas 参考像素）
    const float CardW = 340f, CardH = 400f;

    // 绘图区：地图内容能占的最大矩形。每个场景按自己的长宽比缩进这个框、再居中。
    // 街区 71.8×81.4、后院 64×60、厨房 40×32 —— 宽高比差一倍多，用固定的 px/m
    // 要么把厨房撑出面板，要么把后院缩成一个小方块。所以 K 是**算出来的**，不是常量。
    const float AreaX = 35f, AreaY = 48f;
    const float AreaW = 269.3f, AreaH = 305.3f;

    static float MapX0 = AreaX, MapY0 = AreaY;   // 居中之后的左下角
    static float K = 3.75f;                      // px per meter（Build 时重算）

    static readonly Color CardBg = new Color(0.08f, 0.08f, 0.09f, 0.74f);
    static readonly Color LineCol = new Color(0.86f, 0.80f, 0.66f, 0.55f);
    static readonly Color BlockCol = new Color(0.62f, 0.62f, 0.64f, 0.92f);
    static readonly Color CheeseCol = new Color(1.00f, 0.84f, 0.26f, 1f);
    // 层号方块的配色：本层是实心金 + 近黑字，别层是暗铜 + 金字。
    // 冰箱七层在俯视图上会叠成一团：全亮等于一片金点糊在中间；全暗又等于
    // "定位器上根本没有它"。但只靠亮度分"本层/别层"仍然回答不了
    // "那一块在第几层"—— 所以块里直接写层号，这一对颜色只负责强调本层。
    static readonly Color ChipFarBg  = new Color(0.26f, 0.22f, 0.10f, 0.92f);
    static readonly Color ChipFarInk = new Color(1.00f, 0.84f, 0.26f, 0.80f);
    static readonly Color ChipInk    = new Color(0.14f, 0.10f, 0.02f, 1f);
    static readonly Color CatCol = new Color(0.93f, 0.35f, 0.30f, 1f);
    static readonly Color CatHotCol = new Color(1.00f, 0.18f, 0.10f, 1f);

    // 后院：保安点。暖金 = 手电还亮着、在巡逻；红 = 已经看见你、正在追。
    // 猫从后院删掉之后，地图上不能一个威胁都没有 —— 这一关要求玩家算"往哪丛灌木跑 5 m"，
    // 看不见人在哪，那个计算就只能靠猜。
    static readonly Color GuardCol = new Color(1.00f, 0.80f, 0.42f, 1f);
    static readonly Color GuardHotCol = new Color(1.00f, 0.26f, 0.16f, 1f);
    static readonly Color SelfCol = new Color(0.55f, 0.93f, 1.00f, 1f);
    static readonly Color StairCol = new Color(0.34f, 0.62f, 0.78f, 0.55f);

    /// 箭头追赶目标角度的速度（1/秒）。0 = 完全跟手但不平滑（快速甩鼠标会一帧跳 90°）；
    /// 22 ≈ 90° 的转角 0.1 s 内走完九成 —— 看着是"转过去"而不是"跳过去"。
    const float ArrowTurn = 22f;

    readonly List<Image> _cheeseDots = new List<Image>();
    readonly List<TextMeshProUGUI> _cheeseNums = new List<TextMeshProUGUI>();
    readonly List<Image> _catDots = new List<Image>();
    readonly List<Image> _guardDots = new List<Image>();
    Image _self;
    float _arrowAng;        // 当前箭头角度（度，0 = 屏幕正上方 = 世界 −z）
    bool _arrowInit;        // 第一帧直接就位，别从 0° 转过来

    internal static NbMap Create()
    {
        GameObject go = new GameObject("Neighbourhood Map (auto)");
        NbMap m = go.AddComponent<NbMap>();
        m.Build();
        return m;
    }

    void Awake()
    {
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    static Vector2 ToMap(float x, float z)
    {
        return new Vector2(MapX0 + (x - NbScene.XMin) * K, MapY0 + (NbScene.ZMax - z) * K);
    }

    // ── 场景专属地形（地图要看得出"哪里不能走"）──

    static int DrawKitchen(RectTransform card)
    {
        float sLen = NbKitchen.BenchS_Z1 - NbKitchen.BenchS_Z0;
        float sZ   = (NbKitchen.BenchS_Z0 + NbKitchen.BenchS_Z1) * 0.5f;
        float nLen = NbKitchen.BenchN_Z1 - NbKitchen.BenchN_Z0;
        float nZ   = (NbKitchen.BenchN_Z0 + NbKitchen.BenchN_Z1) * 0.5f;

        int n = 0;
        for (int i = 0; i < NbKitchen.BenchX.Length; i++)
        {
            float x = NbKitchen.BenchX[i];
            n += MapRect(card, "Bench S" + i, x, sZ, NbKitchen.BenchW, sLen, BlockCol);
            n += MapRect(card, "Bench N" + i, x, nZ, NbKitchen.BenchW, nLen, BlockCol);
        }
        return n;
    }

    static int DrawYard(RectTransform card)
    {
        int n = 0;
        for (int i = 0; i < NbYard.MapCover.Length; i++)
        {
            Vector4 r = NbYard.MapCover[i];   // (x, z, 宽, 深) 存在 (.x, .y, .z, .w)
            n += MapRect(card, "Cover " + i, r.x, r.y, r.z, r.w, BlockCol);
        }
        return n;
    }

    /// 冰箱：六层搁板的前沿线 + 中间那条楼梯。
    ///
    /// 这一支**必须存在**，否则会掉进下面的兜底分支，把街区的 12 栋楼画到冰箱的地图上 ——
    /// NbColliders.Footprints 是街区留下的数据，切场景时没人清它，
    /// 于是"冰箱的平面图"会长成一片居民区，而且不报错。
    ///
    /// 六条前沿线由浅到深排成一道看台，一眼就能看出"越往上越靠里"；
    /// 中间那条竖着的亮带就是楼梯 —— 玩家在小地图上认路，靠的就是这条带。
    static int DrawFridge(RectTransform card)
    {
        int n = 0;
        float x0 = NbFridge.XMin + NbFridge.WallT;
        float x1 = NbFridge.XMax - NbFridge.WallT;
        float cx = (x0 + x1) * 0.5f;
        float w = x1 - x0;

        for (int i = 1; i <= NbFridge.Layers; i++)
            n += MapRect(card, "Shelf front L" + i, cx, NbFridge.FrontZ(i), w, 0.12f, LineCol);

        n += MapRect(card, "Fridge back", cx, NbFridge.ZMin + NbFridge.WallT + 0.06f, w, 0.12f, LineCol);

        float sz0 = NbFridge.FrontZ(1) + NbFridge.StepsPerLayer * NbFridge.StepRun;  // 最下面一级的起点
        float sz1 = NbFridge.FrontZ(NbFridge.Layers);                                // 最上面一级
        n += MapRect(card, "Stairs",
            (NbFridge.StairX0 + NbFridge.StairX1) * 0.5f, (sz0 + sz1) * 0.5f,
            NbFridge.StairX1 - NbFridge.StairX0, Mathf.Abs(sz0 - sz1), StairCol);
        return n;
    }

    static int MapRect(RectTransform card, string name, float x, float z, float w, float d, Color col)
    {
        Vector2 p = ToMap(x, z);
        float bw = w * K, bd = d * K;
        if (bw < 1f || bd < 1f) return 0;

        RectTransform r = NbUi.Rect(name, card);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f);
        r.anchoredPosition = new Vector2(p.x - bw * 0.5f, p.y - bd * 0.5f);
        r.sizeDelta = new Vector2(bw, bd);

        Image im = r.gameObject.AddComponent<Image>();
        im.sprite = NbUi.White;
        im.color = col;
        im.raycastTarget = false;
        return 1;
    }

    void Build()
    {
        GameObject canvasGo = new GameObject("NbMap Canvas (auto)", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 41;               // 压在 HUD 之上，红闪也不会盖住它

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 按当前场景的长宽比重算比例尺，并把地图在绘图区里居中。
        // 这一步必须在**第一个 ToMap 调用之前** —— 后面的地面底色、楼块、点全都读 K。
        K = Mathf.Min(AreaW / NbScene.Width, AreaH / NbScene.Depth);
        MapX0 = AreaX + (AreaW - NbScene.Width * K) * 0.5f;
        MapY0 = AreaY + (AreaH - NbScene.Depth * K) * 0.5f;

        // ── 卡片 ──
        RectTransform card = NbUi.Rect("Card", canvasGo.transform);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 0f);
        card.anchoredPosition = new Vector2(-36f, 36f);
        card.sizeDelta = new Vector2(CardW, CardH);

        Image bg = card.gameObject.AddComponent<Image>();
        bg.sprite = NbUi.Round;
        bg.type = Image.Type.Sliced;
        bg.color = CardBg;
        bg.raycastTarget = false;

        // ── 标题行 ──
        TextMeshProUGUI title = NbUi.Text("Title", card, NbScene.Code + "  MAP", 22f,
            new Color(1f, 0.94f, 0.84f, 1f), TextAlignmentOptions.BottomLeft);
        AnchorLeft(title.rectTransform, new Vector2(20f, CardH - 34f), new Vector2(200f, 26f));

        TextMeshProUGUI size = NbUi.Text("Size", card,
            string.Format("{0:F1} x {1:F1} m", NbScene.Width, NbScene.Depth), 18f,
            new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.BottomRight);
        AnchorRight(size.rectTransform, new Vector2(-20f, CardH - 34f), new Vector2(200f, 24f));

        // ── 地面底色（街区范围）──
        Vector2 a = ToMap(NbScene.XMin, NbScene.ZMax);
        Vector2 b = ToMap(NbScene.XMax, NbScene.ZMin);
        RectTransform field = NbUi.Rect("Field", card);
        field.anchorMin = field.anchorMax = field.pivot = new Vector2(0f, 0f);
        field.anchoredPosition = a;
        field.sizeDelta = new Vector2(b.x - a.x, b.y - a.y);
        Image fi = field.gameObject.AddComponent<Image>();
        fi.sprite = NbUi.White;
        // 底色是**地面**（沥青/铺装），不是草地：楼是亮块、巷道是暗底，过道一眼就能读出来。
        fi.color = new Color(0.17f, 0.19f, 0.18f, 0.92f);
        fi.raycastTarget = false;

        // ── 楼栋：直接画 NbColliders 量到的**真实占地**（12 栋的世界 AABB）──
        //
        // 旧版是按「3 列 × 4 排 + 每列/每排各一个宽深」的网格画的。可这 12 栋是三种
        // 不同模型混排的：实测占地是 16.9×7.7、13.1×13.2、16.9×13.1 三种，
        // 而网格一律按 18.5/16.9/18.3 宽、14.4/13.2/13.2/14.7 深去画 ——
        // 每块都比真正的楼大一圈，边缘压进巷道里，看起来就是"方块和楼对不上"。
        // 现在不猜了：NbColliders 每局开头会按网格实测重算一次，这里直接照抄。
        int drawn = 0;

        // 后院 / 厨房没有"楼"，画它们自己的地形。
        // 这两处的物体是运行时生成的、不进 NbColliders.Footprints，
        // 但坐标全是 NbYard / NbKitchen 里的常量 —— 照着画，和 3D 里一处不差。
        if (NbScene.Kind == NbSceneKind.Kitchen)
        {
            drawn += DrawKitchen(card);
        }
        else if (NbScene.Kind == NbSceneKind.Yard)
        {
            drawn += DrawYard(card);
        }
        else if (NbScene.Kind == NbSceneKind.Fridge)
        {
            drawn += DrawFridge(card);
        }
        else if (NbColliders.Footprints.Count > 0)
        {
            for (int i = 0; i < NbColliders.Footprints.Count; i++)
            {
                Bounds f = NbColliders.Footprints[i];
                Vector2 p = ToMap(f.center.x, f.center.z);
                float bw = f.size.x * K;
                float bd = f.size.z * K;
                if (bw < 1f || bd < 1f) continue;

                RectTransform r = NbUi.Rect("Block " + i, card);
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f);
                r.anchoredPosition = new Vector2(p.x - bw * 0.5f, p.y - bd * 0.5f);
                r.sizeDelta = new Vector2(bw, bd);
                Image im = r.gameObject.AddComponent<Image>();
                im.sprite = NbUi.White;
                im.color = BlockCol;
                im.raycastTarget = false;
                drawn++;
            }
        }
        else
        {
            // 兜底：场景里量不到楼（没有 Street Models，或校正失败）时退回网格近似 ——
            // 至少地图上不是一片空地，但精度只能这样。
            Debug.LogWarning("[Nb] 小地图：拿不到实测占地，退回网格近似 —— 方块位置可能有偏差。");
            for (int i = 0; i < Nb.Slots; i++)
            {
                Vector3 c = Nb.SlotCenter(i);
                Vector2 p = ToMap(c.x, c.z);
                float bw = Nb.ColW[i % Nb.Cols] * K;
                float bd = Nb.RowD[i / Nb.Cols] * K;
                RectTransform r = NbUi.Rect("Block " + i, card);
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f);
                r.anchoredPosition = new Vector2(p.x - bw * 0.5f, p.y - bd * 0.5f);
                r.sizeDelta = new Vector2(bw, bd);
                Image im = r.gameObject.AddComponent<Image>();
                im.sprite = NbUi.White;
                im.color = BlockCol;
                im.raycastTarget = false;
                drawn++;
            }
        }

        // ── 围墙 ──
        // 街区底边留大门缺口；后院和厨房是封闭空间，四边画满。
        if (NbScene.Kind == NbSceneKind.Street)
        {
            Edge(card, Nb.XMin, Nb.ZMax, Nb.GateX0, Nb.ZMax);   // 前墙西段
            Edge(card, Nb.GateX1, Nb.ZMax, Nb.XMax, Nb.ZMax);   // 前墙东段
        }
        else
        {
            Edge(card, NbScene.XMin, NbScene.ZMax, NbScene.XMax, NbScene.ZMax);
        }
        Edge(card, NbScene.XMin, NbScene.ZMin, NbScene.XMax, NbScene.ZMin);   // 后墙
        Edge(card, NbScene.XMin, NbScene.ZMin, NbScene.XMin, NbScene.ZMax);   // 西墙
        Edge(card, NbScene.XMax, NbScene.ZMin, NbScene.XMax, NbScene.ZMax);   // 东墙

        // ── 奶酪点 ──
        // 冰箱里是"带层号的方块"，别的场景是普通小点（16 px 的方块在街区图上太大）。
        // 尺寸和文字都在 Update 里按场景设，这里只负责建出来。
        for (int i = 0; i < NbScene.Targets.Length; i++)
            _cheeseDots.Add(Chip(card, "CheeseMark " + i, _cheeseNums));

        // ── 猫点 ──
        for (int i = 0; i < NbScene.Routes.Length; i++)
            _catDots.Add(Dot(card, "CatDot " + i, CatCol, 10f));

        // ── 保安点（只有后院）──
        // 建满路线容量（10），每帧按实际人数开关 —— 和猫点同一套做法。
        if (NbScene.UsesGuards)
            for (int i = 0; i < NbScene.Routes.Length; i++)
                _guardDots.Add(Dot(card, "GuardDot " + i, GuardCol, 12f));

        // ── 自己（三角形）──
        // 锚点留在左下角（地图坐标就是从那儿算的），但**枢轴必须放中心**：
        // 枢轴写在 (0,0) 时，localEulerAngles 是绕**左下角**转的 —— 箭头会一边指方向、
        // 一边画一个半径 14 px 的圈。冰箱里比例尺 11 px/m，等于位置随转身抖动 ±1.3 m。
        RectTransform selfRt = NbUi.Rect("Self", card);
        selfRt.anchorMin = selfRt.anchorMax = new Vector2(0f, 0f);
        selfRt.pivot = new Vector2(0.5f, 0.5f);
        selfRt.sizeDelta = new Vector2(20f, 20f);
        _self = selfRt.gameObject.AddComponent<Image>();
        _self.sprite = NbUi.Triangle;
        _self.color = SelfCol;
        _self.raycastTarget = false;

        // 箭头外面加一圈深色描边，浅色草地上也能看清
        RectTransform haloRt = NbUi.Rect("SelfHalo", selfRt);
        haloRt.anchorMin = Vector2.zero;
        haloRt.anchorMax = Vector2.one;
        haloRt.offsetMin = new Vector2(-2f, -2f);
        haloRt.offsetMax = new Vector2(2f, 2f);
        Image halo = haloRt.gameObject.AddComponent<Image>();
        halo.sprite = NbUi.Triangle;
        halo.color = new Color(0.05f, 0.10f, 0.12f, 0.85f);
        halo.raycastTarget = false;
        haloRt.SetAsFirstSibling();

        BuildScaleBar(card);

        // ── 图例：只有冰箱要 ──
        // 一个方块里写的数字就是层号。玩家第一次看到那张图时必须**不用试错**就知道
        // 这件事，否则他会把 3 读成"这里有三块奶酪"。
        // 位置贴着绘图区上沿，不和标题行抢（冰箱的图只有 180 px 高，上面空着 70 px）。
        // 文案全用 ASCII：HUD 字体是 Kindly Crunch，`·` / `→` 这类符号不一定有字形，
        // 缺字形是"静默空白"，不会报错（见 memory）。
        if (NbScene.Kind == NbSceneKind.Fridge)
        {
            TextMeshProUGUI lg = NbUi.Text("Legend", card,
                "NUMBER = SHELF  (1 = BOTTOM)", 14f,
                new Color(1f, 1f, 1f, 0.60f), TextAlignmentOptions.BottomLeft);
            AnchorLeft(lg.rectTransform, new Vector2(MapX0, MapY0 + NbScene.Depth * K + 12f),
                new Vector2(280f, 18f));
        }

        Debug.LogFormat("[Nb] 小地图就位（{0}）：{1:F1} x {2:F1} m，比例尺 {3:F2} px/m，" +
                        "地形 {4} 块，目标点 {5} 个、威胁点 {6} 个。",
            NbScene.Code, NbScene.Width, NbScene.Depth, K, drawn,
            _cheeseDots.Count, _catDots.Count);
    }

    static void AnchorLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void AnchorRight(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    /// 一格点：8~10 px 的小方块（四角略圆看不出来，直接用白图）
    static Image Dot(Transform parent, string name, Color color, float px)
    {
        RectTransform rt = NbUi.Rect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(px, px);
        Image im = rt.gameObject.AddComponent<Image>();
        im.sprite = NbUi.White;
        im.color = color;
        im.raycastTarget = false;
        return im;
    }

    /// 奶酪标记：16 px 的方块，中间写**层号**（1 = 底层，7 = 顶层）。
    ///
    /// 层号是这块地方唯一真正缺的信息。俯视图把七层压成一张，两块不同层的奶酪
    /// 可能只差 9 px —— 只把别层的调暗，玩家知道"它不在我这层"，
    /// 仍然不知道"那是第几层"，而这一关的全部玩法就是决定先爬哪一层。
    static Image Chip(Transform parent, string name, List<TextMeshProUGUI> nums)
    {
        RectTransform rt = NbUi.Rect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(16f, 16f);

        Image im = rt.gameObject.AddComponent<Image>();
        im.sprite = NbUi.White;
        im.color = CheeseCol;
        im.raycastTarget = false;

        TextMeshProUGUI t = NbUi.Text("N", rt, "", 13f, ChipInk, TextAlignmentOptions.Center);
        RectTransform tr = t.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;

        nums.Add(t);
        return im;
    }

    // 错开时依次试的偏移（单位：自己的边长 + 1）。0 是原始位置，先试 0 再上下挪。
    static readonly float[] ChipNudge = { 0f, -1f, 1f, -2f, 2f, -3f, 3f };

    /// 开关一个奶酪标记。关整个 GameObject —— 数字是子物体，
    /// 只关 Image 会留下一串"没有方块的数字"。
    static void CheeseOn(Image dot, bool on)
    {
        if (dot.gameObject.activeSelf != on) dot.gameObject.SetActive(on);
    }

    /// 改变方块边长（单层场景 9 px / 冰箱 16 px）。尺寸没变就不碰 ——
    /// 每帧写 sizeDelta 会让 RectTransform 一直标脏。
    static void ChipSize(Image dot, float px)
    {
        if (!Mathf.Approximately(dot.rectTransform.sizeDelta.x, px))
            dot.rectTransform.sizeDelta = new Vector2(px, px);
    }

    /// 把第 i 个标记放到 p（p 是投影中心），若和前面已放好的标记压在一起就上下错开。
    ///
    /// 实测（按 Spots 的 12 个点算）：16 px 的方块有 **1 处**正压 ——
    /// (63, L2, -12) 与 (64, L4, -13) 在图上只差 9 px。那两块会叠成一个，
    /// 而且留下的是后画的那个编号 ⇒ 玩家会照着错的层号白爬两层。
    /// 所以这一个函数不是为了好看，是为了"编号不会互相冒充"。
    ///
    /// 每帧重算、按列表顺序扫前面的标记 ⇒ 结果稳定（不会抖），
    /// 前面的那块被捡走之后后面的会自动回到原位。
    void SettleChip(int i, Vector2 p, float px)
    {
        RectTransform rt = _cheeseDots[i].rectTransform;
        float step = px + 1f;
        float bx = p.x - px * 0.5f, by = p.y - px * 0.5f;

        for (int k = 0; k < ChipNudge.Length; k++)
        {
            Vector2 q = new Vector2(bx, by + ChipNudge[k] * step);
            if (!ChipBlocked(i, q, px)) { rt.anchoredPosition = q; return; }
        }
        rt.anchoredPosition = new Vector2(bx, by);   // 七个位置都满（不可能），认了
    }

    bool ChipBlocked(int i, Vector2 q, float px)
    {
        for (int j = 0; j < i; j++)
        {
            Image o = _cheeseDots[j];
            if (o == null || !o.gameObject.activeSelf) continue;   // 已经被捡走的不占位
            Vector2 r = o.rectTransform.anchoredPosition;
            if (Mathf.Abs(q.x - r.x) < px && Mathf.Abs(q.y - r.y) < px) return true;
        }
        return false;
    }

    /// 沿世界坐标画一条边（2 px 粗）
    static void Edge(Transform parent, float x0, float z0, float x1, float z1)
    {
        Vector2 a = ToMap(x0, z0);
        Vector2 b = ToMap(x1, z1);
        RectTransform rt = NbUi.Rect("Edge", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(Mathf.Min(a.x, b.x) - 1f, Mathf.Min(a.y, b.y) - 1f);
        rt.sizeDelta = new Vector2(Mathf.Abs(b.x - a.x) + 2f, Mathf.Abs(b.y - a.y) + 2f);
        Image im = rt.gameObject.AddComponent<Image>();
        im.sprite = NbUi.White;
        im.color = LineCol;
        im.raycastTarget = false;
    }

    void BuildScaleBar(Transform card)
    {
        // 20 m 的比例尺，画在地图下沿的留白里
        float len = 20f * K;
        RectTransform bar = NbUi.Rect("ScaleBar", card);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 0f);
        bar.anchoredPosition = new Vector2(MapX0, 18f);
        bar.sizeDelta = new Vector2(len, 3f);
        Image bi = bar.gameObject.AddComponent<Image>();
        bi.sprite = NbUi.White;
        bi.color = new Color(1f, 1f, 1f, 0.65f);
        bi.raycastTarget = false;

        // 两端竖线
        for (int i = 0; i < 2; i++)
        {
            RectTransform tick = NbUi.Rect("Tick" + i, card);
            tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0f, 0f);
            tick.anchoredPosition = new Vector2(MapX0 + (i == 0 ? 0f : len) - 1f, 14f);
            tick.sizeDelta = new Vector2(2f, 11f);
            Image ti = tick.gameObject.AddComponent<Image>();
            ti.sprite = NbUi.White;
            ti.color = new Color(1f, 1f, 1f, 0.65f);
            ti.raycastTarget = false;
        }

        TextMeshProUGUI label = NbUi.Text("ScaleLabel", card, "20 m", 16f,
            new Color(1f, 1f, 1f, 0.62f), TextAlignmentOptions.BottomLeft);
        AnchorLeft(label.rectTransform, new Vector2(MapX0 + len + 8f, 13f), new Vector2(120f, 20f));
    }

    // ───────────────────────── 每帧 ─────────────────────────

    void Update()
    {
        NbGame g = NbGame.I;
        if (g == null) return;

        // 奶酪：捡走就灭点（掉在地上时会重新亮，位置也跟着变）
        //
        // **多出来的点必须显式关掉。** 点是按场地容量（10 块 / 10 只）一次性建好的，
        // 而第 1 关只有 2 块奶酪 —— 不关的话左下角会永远挂着 8 个不动的点，
        // 看着像"奶酪掉在墙外/地图外"。这个循环因此遍历**所有**点，而不是只遍历到 Count。
        //
        // 关的是 **GameObject**，不是 Image 组件：标记里的数字是子物体，
        // 只关 Image 的话方块没了、数字还留在原处 —— 看起来像地图上飘着一串 3。
        //
        // 多层场景（冰箱）里，"我在第几层"只算一次，然后每个标记自己比。
        bool multi = NbScene.KeepsHeight;
        int myShelf = (multi && g.Player != null) ? NbFridge.ShelfOf(g.Player.position.y) : 0;

        for (int i = 0; i < _cheeseDots.Count; i++)
        {
            Image dot = _cheeseDots[i];
            if (dot == null) continue;

            if (i >= g.Cheeses.Count) { CheeseOn(dot, false); continue; }

            NbCheese c = g.Cheeses[i];
            if (c == null) { CheeseOn(dot, false); continue; }

            // 冰箱是多层的：地图是俯视投影，七层的点全落在同一张图上。
            //
            // 旧版只显示**玩家脚下这一层**的目标 —— 结果玩家站在 L2 时，L5 那两块奶酪在
            // 定位器上根本不存在，他能看见的只有手边一两块，"定位器"等于没用。
            // 再往后是"全部显示、只靠亮度分层"：玩家知道它不在我这层，
            // 但仍然不知道**它在第几层** —— 而这一关的策略就是决定先爬哪层。
            //
            // 所以现在每个标记里直接写层号，本层实心金、别层暗铜。
            // 位置仍然是投影坐标 ⇒ "它在我哪个方向"照样读得对。
            bool on = !c.Taken;
            CheeseOn(dot, on);
            if (!on) continue;

            Vector2 p = ToMap(c.transform.position.x, c.transform.position.z);
            TextMeshProUGUI num = (i < _cheeseNums.Count) ? _cheeseNums[i] : null;

            // 单层场景（街区/后院/厨房）：退回普通小点，不写数字。
            if (!multi)
            {
                if (num != null) num.text = "";
                dot.color = CheeseCol;
                ChipSize(dot, 9f);
                dot.rectTransform.anchoredPosition = new Vector2(p.x - 4.5f, p.y - 4.5f);
                continue;
            }

            int shelf = NbFridge.ShelfOf(c.transform.position.y);
            bool here = shelf == myShelf;

            dot.color = here ? CheeseCol : ChipFarBg;
            if (num != null)
            {
                num.text = shelf.ToString();
                num.color = here ? ChipInk : ChipFarInk;
            }
            ChipSize(dot, 16f);
            SettleChip(i, p, 16f);
        }

        // 猫：同上，第 n 关只亮前 n 个点
        for (int i = 0; i < _catDots.Count; i++)
        {
            Image dot = _catDots[i];
            if (dot == null) continue;

            if (i >= g.Cats.Count) { if (dot.enabled) dot.enabled = false; continue; }

            NbCat c = g.Cats[i];
            if (c == null) { if (dot.enabled) dot.enabled = false; continue; }

            if (!dot.enabled) dot.enabled = true;
            Vector2 p = ToMap(c.transform.position.x, c.transform.position.z);
            dot.rectTransform.anchoredPosition = new Vector2(p.x - 5f, p.y - 5f);
            dot.color = c.IsChasing ? CatHotCol : CatCol;
        }

        // 保安点：同上，按实际人数开关；追人的那一个变红。
        // 它同时是"5 m"这件事的参照物 —— 玩家看地图上的距离远比在黑暗里目测可靠。
        for (int i = 0; i < _guardDots.Count; i++)
        {
            Image dot = _guardDots[i];
            if (dot == null) continue;

            if (i >= g.Guards.Count || g.Guards[i] == null)
            {
                if (dot.enabled) dot.enabled = false;
                continue;
            }

            if (!dot.enabled) dot.enabled = true;
            Vector3 gp = g.Guards[i].transform.position;
            Vector2 p = ToMap(gp.x, gp.z);
            dot.rectTransform.anchoredPosition = new Vector2(p.x - 6f, p.y - 6f);
            dot.color = g.Guards[i].Chasing ? GuardHotCol : GuardCol;
        }

        // 自己：位置 + 箭头朝向
        if (_self != null && g.Player != null)
        {
            Vector3 pos = g.Player.position;
            Vector2 p = ToMap(pos.x, pos.z);
            // 枢轴在中心 ⇒ 这里直接给中心坐标（旧版按"中心对齐"减了半个边长，
            // 但枢轴其实是角，减出来的位置只在 0° 时是对的）。
            _self.rectTransform.anchoredPosition = p;

            // 箭头 = 身体朝向。站着只转鼠标，它也要跟着转。
            Vector3 h = g.Heading;
            if (h.sqrMagnitude < 1e-4f) h = NbScene.Forward;

            // 屏幕上方 = 世界 −z，所以是 Atan2(−x, −z)：
            // 朝 −z ⇒ 0°（正上），朝 +x ⇒ −90°（转成顺时针 90°，指向右）。
            float want = Mathf.Atan2(-h.x, -h.z) * Mathf.Rad2Deg;

            // 按**角度**插值，不按向量。向量插值在两个方向接近相反时走哪边不确定，
            // 旧代码只好用 Dot<0 直接跳过去 —— 快速环视时能看见箭头"啪"地掉一格。
            // DeltaAngle 自动取最短弧，跨 ±180 也是连续的。
            if (!_arrowInit) { _arrowAng = want; _arrowInit = true; }
            float step = Mathf.Clamp01(ArrowTurn * Time.unscaledDeltaTime);
            _arrowAng = Mathf.Repeat(_arrowAng + Mathf.DeltaAngle(_arrowAng, want) * step, 360f);
            _self.rectTransform.localEulerAngles = new Vector3(0f, 0f, _arrowAng);
        }
    }
}
