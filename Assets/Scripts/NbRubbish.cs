// @role Dresses the baked RubbishBox cube piles into readable refuse (skip, bags, cardboard, pallets, litter).
// @calls Nb

// NbRubbish.cs — 把场景里 12 堆「RubbishBox」换成看得懂的垃圾堆
//
// 那 12 堆原本是 16 个随机旋转的灰方块（父级 ==Residential Rubbish==，缩放 0.6），
// 远看像塌了一半的水泥板，读不出"这是垃圾"。这里按项目一贯的做法处理：
// **运行时把灰方块的 Renderer 关掉，在原地按同一副包络重新长出一堆垃圾。**
// （改预制体没用 —— 场景里这 12 份是展开的副本；重烘又要开编辑器。）
//
// 三条硬约束，动之前先看清楚：
//
//   ① **碰撞体一个都不动。** 灰方块不只是装饰，它们是这一关的掩体：猫的视线射线要靠它们截断，
//      玩家也得绕开走。这里只 `Renderer.enabled = false`，16 个 BoxCollider 原样留着
//      ⇒ 遮挡与碰撞和改动之前**完全一致**，不可能因为"换皮"把某条巷子堵死或变透明。
//      唯一例外是文件末尾那条「奶酪通道」：它必须关掉压在一条窄线上的方块碰撞体，
//      否则玩家永远走不到被压在堆中间的那颗奶酪。关之前先按 OverlapBox 精确筛，
//      关之后沿通道补一圈实心围壳 —— 挖开的空腔一步也进不去，见 Burrow 那一节。
//   ② 新长出来的零件全部**没有碰撞体**（建完就 Kill 掉），并且放在 Ignore Raycast 层。
//      否则从垃圾袋上发出的射线会先打在自己身上，猫的判视/落地探测都会被搅乱。
//   ③ 尺寸全部按**量出来的包络**算，不写死数字。现在每堆缩放 0.6，将来调了会自动跟着变。
//
// 最后一点是给调参的人看的：整堆的"读感"主要由三样东西决定 ——
// 垃圾桶的硬轮廓（给一个可辨认的方盒子剪影）、袋子铺满的圆丘（给"软"的体积）、
// 地面上散落的易拉罐/纸片（给"尺度"，让玩家知道自己只有 0.3 m 高）。三样缺一样就会重新变回"方块"。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class NbRubbish
{
    const string PilePrefix = "RubbishBox";      // 场景里那 12 个根球的名字
    const string ArtName    = "Rubbish Art";     // 换皮产物的名字；同时当"已经换过"的记号

    // ───────────────────────── 配色 ─────────────────────────
    // 夜里暗巷里，垃圾堆一度灰扑扑的、和路面糊成一块。现在把整堆染成**彩虹**：
    // 红/橙/黄/绿/青/蓝/紫一档一档铺开。明度再压低一档、饱和度保持高位 ——
    // 要的是"又艳又脏"：色相够跳、能和暗巷拉开对比，却几乎不反光、不干净，
    // 像在泥里滚过一圈的彩垃圾。最艳的那几只袋子（BagC 黄 / BagE 青）相对亮两档，剪影边缘才有可数的台阶。
    static readonly Color BagA   = new Color(0.250f, 0.035f, 0.030f);   // 脏红袋子
    static readonly Color BagB   = new Color(0.320f, 0.120f, 0.030f);   // 脏橙袋子
    static readonly Color BagC   = new Color(0.340f, 0.280f, 0.030f);   // 脏黄袋子（相对最艳）
    static readonly Color BagD   = new Color(0.080f, 0.240f, 0.050f);   // 脏绿袋子
    static readonly Color BagE   = new Color(0.035f, 0.220f, 0.210f);   // 脏青/水鸭袋子
    static readonly Color BagF   = new Color(0.060f, 0.090f, 0.290f);   // 脏蓝袋子
    static readonly Color Card   = new Color(0.280f, 0.140f, 0.035f);   // 纸板（橙褐）
    static readonly Color CardB  = new Color(0.220f, 0.110f, 0.030f);   // 湿过/压旧的纸板
    static readonly Color Tape   = new Color(0.360f, 0.290f, 0.035f);   // 胶带（脏黄）
    static readonly Color Wood   = new Color(0.240f, 0.130f, 0.035f);   // 托盘木条（褐）
    static readonly Color SkipBody = new Color(0.210f, 0.055f, 0.290f); // 垃圾箱（脏紫）
    static readonly Color SkipRim  = new Color(0.250f, 0.180f, 0.310f); // 磨掉漆的箱口（灰紫）
    static readonly Color Iron   = new Color(0.100f, 0.100f, 0.130f);   // 轮子/铁件（青黑）
    static readonly Color TarpBlue = new Color(0.100f, 0.070f, 0.320f); // 防雨布（蓝紫）
    static readonly Color Alu    = new Color(0.290f, 0.340f, 0.390f);   // 易拉罐（钢青）
    static readonly Color GlassG = new Color(0.070f, 0.290f, 0.100f);   // 绿玻璃
    static readonly Color GlassB = new Color(0.055f, 0.130f, 0.340f);   // 蓝玻璃
    static readonly Color Paper  = new Color(0.410f, 0.390f, 0.340f);   // 湿纸（脏白）

    // ───────────────────────── 入口 ─────────────────────────

    /// 扫整个场景，把每一堆 RubbishBox 都换掉。只在 Play 里调用 ——
    /// 编辑器烘焙（Tools/Street/Bake …）走的是另一条路，那边不应该长出这些运行时物件。
    internal static int DressAll()
    {
        if (!Application.isPlaying) return 0;

        Scene s = SceneManager.GetActiveScene();
        if (!s.IsValid()) return 0;

        // 先定通道（只要一颗奶酪点 + 各堆的包络），再换皮 —— 换皮时每只袋子都要按它剔除。
        // 反过来就不行了：袋子已经落地，再挖通道只能关碰撞体，挡在洞口的袋子是删不掉的。
        _burrow = PlanBurrow();

        int piles = 0, pieces = 0;
        GameObject[] roots = s.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            pieces += DressTree(roots[i].transform, ref piles);
        }

        // 通道放在最后建：它要把所有堆压在自己身上的方块碰撞体关掉，
        // 而"哪些方块压着"只有在全部换完皮、几何定下来之后才是真值。
        if (_burrow.On) pieces += BuildBurrow();

        if (piles > 0)
            Debug.LogFormat("[Nb] 垃圾堆 {0} 处已换皮：共 {1} 件（垃圾箱 / 垃圾袋 / 纸箱 / 托盘 / 散落杂物），" +
                            "灰方块只关渲染、碰撞体原样保留（奶酪通道那一条线除外）。", piles, pieces);
        return piles;
    }

    /// 递归找 RubbishBox。**从根一路走下来**，因为 12 堆挂在 ==Residential Rubbish== 下面，
    /// 另有三份是预制体实例、挂得更深；只扫一层会漏掉它们。
    static int DressTree(Transform t, ref int piles)
    {
        int pieces = 0;
        if (t.name.StartsWith(PilePrefix, System.StringComparison.Ordinal))
        {
            int n = Dress(t);
            if (n > 0) { piles++; pieces += n; }
        }

        for (int i = 0; i < t.childCount; i++)
            pieces += DressTree(t.GetChild(i), ref piles);

        return pieces;
    }

    // ───────────────────────── 通往奶酪的窄通道 ─────────────────────────
    //
    // 第 1 关那颗奶酪（VBcx, -31）被 8 号堆压在**正中间**：丘心 (93.97, -30.44)，半径 2.9 m，
    // 奶酪离丘心只有 0.6 m。玩家撞上去就是一堵看不见的墙 —— 灰方块的渲染早关了，
    // 底下那段只是袋子堆出来的"山顶"，所以看起来是"奶酪在垃圾堆里，但走不进去"。
    //
    // 做法：从堆的**北坡脚**（H1 那条横巷一路往南走到堆跟前）挖一条 0.58 × 0.55 的窄通道，
    // 直通奶酪，奶酪后面留 0.6 m 死胡同。四条规矩：
    //
    //   ① 净空必须**在构造上是空的**：按 OverlapBox 精确找出压在这条线上的方块碰撞体（带旋转的
    //      方块用世界 AABB 筛会把旁边的一起圈进来），关掉；再沿通道补一圈实心围壳
    //      （左右墙 + 顶板 + 尾板）。围壳是从**净空边界往外**长的 ⇒ 被挖开的空腔一步也进不去，
    //      关碰撞体不会让堆旁边的墙跟着变透明。
    //   ② 装饰袋子按**球心**是否落在净空里剔除。中心在外、只把肚子压进来一点的那几只保留 ——
    //      那是通道壁的软边；全剔掉就成了塑料管。剔除盒比净空宽（0.58 → 1.10）：
    //      这些袋子直径 1.1 m，只剔中心正好落在 0.58 里的那几只，剩下的会把洞堵得只剩一条缝。
    //   ③ 通道壁 / 洞顶用**自己的袋子**重铺一遍，球心放在净空外 6 cm。袋子没有碰撞体，
    //      相机贴墙走时会钻进袋子里（一脸黑），留这 6 cm 就是为了这个。
    //   ④ 自检只看 `CheckCapsule`，不看射线：**射线从 BoxCollider 内部出发是不报命中的**，
    //      之前用射线探"能不能走到奶酪"，探出来一路通畅，实际人整个埋在实心盒子里。

    struct Burrow
    {
        internal bool  On;
        internal float X;            // 通道中线
        internal float Ground;       // 地面高度（奶酪点自带）
        internal float HalfW;        // 净空半宽
        internal float Height;       // 净空高
        internal float ZMouth;       // 洞口（北，靠 H1 那头，z 大）
        internal float ZEnd;         // 死胡同（南，z 小）

        /// 这只袋子是不是压住通道口 / 净空（按球心判）。
        /// 🔴 剔除盒比净空**宽一倍多**（0.58 → 1.58）。这些袋子直径 1.1 m：只剔球心落在 0.58
        /// 里的那几只，两侧的邻居球心在盒外、肚子照样鼓到中线，洞口只剩一条 10 cm 的横缝，
        /// 站在三米外根本看不出那是个洞（第二版渲染就是这样）。剔宽了以后再用自己的小袋
        /// （0.6~0.9）把洞框重新垒出来 —— 洞多大，是由"清掉哪一圈"决定的。
        internal bool Holds(Vector3 w) { return Holds(w, 0f); }

        /// `pad` 是给**大件**留的余量：纸箱 1.3~2.4 宽、木板 2.6 长，球心（基准点）在盒外
        /// 也照样有一整面平板横进洞里 —— 洞里出现一面方方正正的暗墙，比洞口露着还难看。
        internal bool Holds(Vector3 w, float pad)
        {
            if (!On) return false;
            if (w.x < X - HalfW - 0.50f - pad || w.x > X + HalfW + 0.50f + pad) return false;
            if (w.z < ZEnd - pad || w.z > ZMouth + 0.70f + pad) return false;
            return w.y > Ground - 0.40f - pad && w.y < Ground + Height + 0.40f + pad;
        }
    }

    static Burrow _burrow;

    /// 只在"奶酪确实被某座丘压住"时才挖。判定用和 Build 完全同一条式子（c / R），
    /// 免得这里算一套、堆长成另一套 —— 之前的悬空就是三处各算一条曲面造成的。
    static Burrow PlanBurrow()
    {
        Burrow b = new Burrow();
        if (Nb.CheeseSpots == null || Nb.CheeseSpots.Length == 0) return b;

        // 第 1 关那颗。数组是按"离出生点由近到远"排的，改它的位置要连注释一起改。
        Vector3 spot = Nb.CheeseSpots[0];

        Scene s = SceneManager.GetActiveScene();
        if (!s.IsValid()) return b;

        Transform pile = null;
        float bestR = 0f, bestD = float.MaxValue;
        Vector3 bestC = Vector3.zero;
        GameObject[] roots = s.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            ScanPile(roots[i].transform, spot, ref pile, ref bestR, ref bestC, ref bestD);
        }
        if (pile == null) return b;

        // 🔴 光"离丘心 < 0.9R"不够：奶酪扔在堆脚外面时，紧挨着的那座丘照样能算进来，
        //    于是白挖一条洞（还会关掉一串碰撞体）。真正的判据是 —— 它现在**被方块的实心闷住了**。
        //    露在巷子地上的奶酪一个 OverlapSphere 都碰不到，直接不挖。
        Collider[] boxed = Physics.OverlapSphere(spot + Vector3.up * 0.15f, 0.02f, ~0, QueryTriggerInteraction.Ignore);
        bool stuck = false;
        for (int i = 0; i < boxed.Length; i++)
            if (boxed[i] != null && !boxed[i].isTrigger && UnderPile(boxed[i].transform)) { stuck = true; break; }
        if (!stuck)
        {
            Debug.Log("[Nb] 奶酪没被方块闷住（露在巷子里）⇒ 不挖通道。");
            return b;
        }

        b.On     = true;
        b.X      = spot.x;
        b.Ground = spot.y;
        b.HalfW  = 0.29f;                       // 净宽 0.58：老鼠胶囊直径 0.20，两边各留 0.19
        b.Height = 0.55f;                       // 净高 0.55：老鼠 0.30，奶酪 0.34，头顶还留 0.2
        b.ZMouth = bestC.z + bestR + 0.02f;     // 北坡脚（+z 朝北，朝 H1 那条横巷）
        b.ZEnd   = spot.z - 0.62f;              // 奶酪后面 0.6 m 死胡同，别让人一眼看到头

        // 太短就不值当挖（短到 1.6 m 以下，读起来只是堆脚上一个凹坑）。
        if (b.ZMouth - b.ZEnd < 1.6f) b.On = false;

        Debug.LogFormat("[Nb] 奶酪通道：压在「{0}」里（丘心 {1:F2},{2:F2}，R={3:F2}，奶酪离丘心 {4:F2} m）⇒ {5}",
            pile.name, bestC.x, bestC.z, bestR, bestD, b.On ? "挖" : "太近，不挖");
        return b;
    }

    /// 递归找"哪座丘把这一点圈在里面"，顺带用 Build 同一条式子算出它的 c / R。
    static void ScanPile(Transform t, Vector3 spot,
        ref Transform best, ref float bestR, ref Vector3 bestC, ref float bestD)
    {
        if (t.name.StartsWith(PilePrefix, System.StringComparison.Ordinal))
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds lb;
            if (rs.Length > 0 && Envelope(t, rs, out lb))
            {
                float k = Mathf.Abs(t.lossyScale.x);
                float hw = Mathf.Max(1.5f, lb.extents.x);
                float hd = Mathf.Max(1.5f, lb.extents.z);
                float R = Mathf.Min(hw, hd) * 0.90f * k;
                Vector3 c = t.TransformPoint(lb.center);
                float d = Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(c.x, c.z));
                if (d < R * 0.90f && d < bestD) { best = t; bestR = R; bestC = c; bestD = d; }
            }
        }

        for (int i = 0; i < t.childCount; i++)
            ScanPile(t.GetChild(i), spot, ref best, ref bestR, ref bestC, ref bestD);
    }

    /// 这一串父级里有没有 RubbishBox —— 只挖自己的方块，别把地面 / 楼碰掉。
    static bool UnderPile(Transform t)
    {
        for (int i = 0; i < 24 && t != null; i++)
        {
            if (t.name.StartsWith(PilePrefix, System.StringComparison.Ordinal)) return true;
            t = t.parent;
        }
        return false;
    }

    /// 一块只有碰撞体、没有渲染的实心盒子。父物体在世界原点且无旋转无缩放 ⇒ local 就是 world。
    static void Slab(Transform p, string name, Vector3 center, Vector3 size)
    {
        GameObject go = new GameObject("Burrow " + name);
        go.layer = 0;                       // Default：玩家撞得上、猫的视线也拦得住
        go.transform.SetParent(p, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = Vector3.zero;
        bc.size = size;
    }

    static int BuildBurrow()
    {
        Burrow b = _burrow;
        int n = 0;

        Transform root = new GameObject("Rubbish Burrow").transform;
        root.position = Vector3.zero;
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one;

        float len  = b.ZMouth - b.ZEnd;
        float midZ = (b.ZMouth + b.ZEnd) * 0.5f;

        // ① 关掉压在净空里的方块碰撞体
        Vector3 half = new Vector3(b.HalfW + 0.03f, b.Height * 0.5f + 0.03f, len * 0.5f + 0.03f);
        Collider[] hits = Physics.OverlapBox(new Vector3(b.X, b.Ground + b.Height * 0.5f, midZ), half,
                                            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        int carved = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null || hits[i].isTrigger || !hits[i].enabled) continue;
            if (!UnderPile(hits[i].transform)) continue;
            hits[i].enabled = false;
            carved++;
        }

        // ② 围壳。高度取 1.30：猫的眼睛就在这个高度以下，围壳太矮等于从洞顶看穿过去。
        const float Wall = 0.12f;
        const float Tall = 1.30f;
        Slab(root, "Left",  new Vector3(b.X - b.HalfW - Wall * 0.5f, b.Ground + Tall * 0.5f, midZ),
             new Vector3(Wall, Tall, len));
        Slab(root, "Right", new Vector3(b.X + b.HalfW + Wall * 0.5f, b.Ground + Tall * 0.5f, midZ),
             new Vector3(Wall, Tall, len));
        Slab(root, "Roof",  new Vector3(b.X, b.Ground + (b.Height + Tall) * 0.5f, midZ),
             new Vector3(b.HalfW * 2f + Wall * 2f, Tall - b.Height, len));
        Slab(root, "End",   new Vector3(b.X, b.Ground + Tall * 0.5f, b.ZEnd - Wall * 0.5f),
             new Vector3(b.HalfW * 2f + Wall * 2f, Tall, Wall));

        // ③ 内衬：左右各两层袋 + 一排洞顶袋。球心离净空边 6 cm（见 ④ 的反面：袋子没碰撞体，
        //    相机能穿进去，所以袋子本身必须退到墙外）。
        float lip = b.HalfW + 0.06f;
        int k = 0;
        for (float z = b.ZMouth - 0.30f; z > b.ZEnd + 0.24f; z -= 0.46f)
        {
            float wob = Mathf.Sin(z * 3.1f) * 0.05f;         // 让两侧不那么对称
            for (int s = -1; s <= 1; s += 2)
            {
                BurrowBag(root, new Vector3(b.X + s * (lip + 0.32f), b.Ground + 0.21f, z), 0.64f, BurrowColor(k + 3));
                // 第二层**整条都要有**：只在两头补的话，中段 0.42…0.67 会留一条通长的横缝，
                // 从缝里能看见堆内部的其他袋子（一条与洞不同向的暗带，很出戏）。
                BurrowBag(root, new Vector3(b.X + s * (lip + 0.30f), b.Ground + 0.48f, z + 0.14f), 0.60f, BurrowColor(k + 1));
                n += 2;
            }
            // 洞顶：袋心 0.74 ⇒ 肚子最低 0.52，刚好压在净空顶 0.55 上。
            // 🔴 第一版给到 0.86：视觉上的"天花板"比碰撞顶高 30 cm，从洞里看像一条**高顶的洞**，
            // 而不是要低头钻的缝（渲染里整个洞的味道都从这 30 cm 跑掉了）。
            BurrowBag(root, new Vector3(b.X + wob, b.Ground + 0.74f, z), 0.86f, BurrowColor(k + 2));
            n++;
            k++;
        }

        // ④ 洞口：两侧各压一只大袋当门框，正中压一只横躺的当门楣。
        //    🔴 第一版在这里搭了一块 1.95×1.2 的铁皮当"屋檐"，从巷子里看是一块**浮在袋子上方的亮板**
        //    （夜景里它比周围袋子亮两档，整个堆的视线全被它抢走），从洞里往上看又是一块压得很低的平板 ——
        //    两边都不像垃圾。删掉，改回袋子本身：洞的大小是靠**清掉哪一圈袋子**决定的，不是靠加东西。
        float zm = b.ZMouth + 0.10f;
        for (int s = -1; s <= 1; s += 2)
        {
            BurrowBag(root, new Vector3(b.X + s * (b.HalfW + 0.46f), b.Ground + 0.30f, zm - 0.04f), 0.84f, BagColor(s > 0 ? 3 : 6));
            BurrowBag(root, new Vector3(b.X + s * (b.HalfW + 0.40f), b.Ground + 0.80f, zm + 0.16f), 0.70f, BagColor(s > 0 ? 7 : 2));
            n += 2;
        }
        BurrowBag(root, new Vector3(b.X - 0.14f, b.Ground + 0.84f, zm + 0.06f), 0.92f, BagColor(1));
        BurrowBag(root, new Vector3(b.X + 0.32f, b.Ground + 0.90f, zm - 0.14f), 0.74f, BagColor(4));
        n += 2;

        // ④b 门楣：洞口上方横一块旧木板。
        //     为什么非加不可：洞口本身只是"袋子少了一圈"，从巷子里 3 m 外看，和"堆底没压实
        //     留出来的那条缝"长得一模一样。玩家眼睛只有 16 cm 高，走到跟前也未必想到那能钻。
        //     一块比袋子亮两档的棕色板压在洞顶，视线才有落点 —— 板子底下那条黑，就是洞。
        //     只做一根歪的横板，不做两侧立柱：立柱 + 横梁会读成一扇"门"，那成了布景，不是垃圾堆。
        //     高度压在净空顶 0.55 之上（板底 0.55），不占通道。
        Transform lintel = new GameObject("Burrow Lintel").transform;
        lintel.SetParent(root, false);
        lintel.localPosition = new Vector3(b.X + 0.05f, b.Ground + b.Height + 0.13f, zm + 0.12f);
        lintel.localRotation = Quaternion.Euler(5f, -11f, -4f);
        Piece(lintel, PrimitiveType.Cube, "Lintel", Vector3.zero,
            new Vector3(b.HalfW * 2f + 0.66f, 0.24f, 0.07f), Quaternion.identity, MatOf(Wood, 0.06f, 0f));
        n++;

        // 靠在洞口左边坡上的一块木板。**必须斜着靠**：第一版给的是 Euler(…, 58°)，
        // 板子立起来成了一根竖在门框边的棕色柱子，比洞口本身还显眼。
        Transform board = new GameObject("Burrow Board").transform;
        board.SetParent(root, false);
        board.localPosition = new Vector3(b.X - b.HalfW - 0.72f, b.Ground + 0.42f, zm + 0.34f);
        board.localRotation = Quaternion.Euler(-46f, 24f, 0f);
        Piece(board, PrimitiveType.Cube, "Board", Vector3.zero,
            new Vector3(0.98f, 0.08f, 0.40f), Quaternion.identity, MatOf(Wood, 0.06f, 0f));
        n++;

        // 洞口外面撒两件：巷子里走过来先看见地上的罐子，视线顺着它们才能落到那个黑口子上。
        for (int i = 0; i < 2; i++)
        {
            float z = b.ZMouth + 0.75f + i * 0.85f;
            float x = b.X + (i % 2 == 0 ? -0.42f : 0.36f);
            Piece(root, PrimitiveType.Cylinder, "Burrow Spill " + i,
                new Vector3(x, b.Ground + 0.10f, z),
                new Vector3(0.11f, 0.09f, 0.11f),
                Quaternion.Euler(88f, Random.value * 360f, 0f), MatOf(Alu, 0.55f, 0.65f));
            n++;
        }

        // ⑤ 死胡同的堵头。**必须有。** 洞尾只有一块没有渲染的围壳，袋子之间只要有一条缝，
        //    从洞里就能一直看到堆**背面**的巷子 —— 渲染里那根"竖在洞底的灰杆子"就是背面
        //    的路灯杆，中间的亮区是另一条巷子的天光。三只袋子横着把断面塞满。
        for (int i = 0; i < 3; i++)
        {
            BurrowBag(root, new Vector3(b.X + (i - 1) * 0.30f, b.Ground + 0.24f, b.ZEnd - 0.30f),
                0.86f, BurrowColor(i));
            n++;
        }
        BurrowBag(root, new Vector3(b.X - 0.06f, b.Ground + 0.60f, b.ZEnd - 0.24f), 0.82f, BurrowColor(3));
        n++;

        // ⑥ 洞里散几件：给深度和尺度。都贴边放（±0.21），中线留空 —— 第一版有只罐子摆在
        //    通道正中，从洞口看过去像路被堵住了。
        for (int i = 0; i < 3; i++)
        {
            float z = Mathf.Lerp(b.ZMouth - 0.70f, b.ZEnd + 0.60f, (i + 0.5f) / 3f);
            float s = (i % 2 == 0) ? 1f : -1f;
            // 罐子半径 0.05，铺装顶面在 Ground+0.048 —— 压在 0.052 的话有半截埋在路面里，
            // 渲染里就是一只"从地里冒出来的圆顶"。抬到 0.098 才是整只躺在洞底。
            Piece(root, PrimitiveType.Cylinder, "Burrow Can",
                new Vector3(b.X + s * 0.21f, b.Ground + 0.098f, z),
                new Vector3(0.12f, 0.10f, 0.12f),
                Quaternion.Euler(90f, Random.value * 360f, 0f), MatOf(Alu, 0.55f, 0.65f));
            n++;
        }
        Piece(root, PrimitiveType.Cube, "Burrow Paper",
            new Vector3(b.X - 0.12f, b.Ground + 0.055f, b.ZEnd + 0.85f),
            new Vector3(0.30f, 0.02f, 0.38f), Quaternion.Euler(0f, 28f, 0f), MatOf(Paper, 0.03f, 0f));
        n++;

        // ⑧ 洞底压一层脏土/湿纸板。
        //    为什么非加不可：洞里的地面就是巷子的铺装，路灯照着，比两侧的黑袋子亮三档 ——
        //    渲染里整条洞读成了"一条干净的灰走廊，两边堆着黑袋子"，而不是"垃圾堆里钻出来的缝"。
        //    现实里堆底是背光的，地面本来就该是暗的；贴一层几乎不反光的深色薄板，最省事。
        //    只铺到洞口里侧 0.10 m 为止，不从巷子里露出来（露出来的直边比亮地面更假）。
        //    宽度给到 1.36（比净空 0.58 宽一倍多）：袋子是球，贴地那一圈收得很厉害（球心 0.21、
        //    半径 0.32，在 y=0.05 处只剩 0.28 的截圆）—— 按净空宽度铺，两侧会各留一条 7 cm 的
        //    铺装亮边，从洞里看是两道平行的白线，比原来的亮地面还扎眼。铺到袋心下面才收得干净。
        float fz0 = b.ZEnd - 0.02f, fz1 = b.ZMouth - 0.10f;
        Piece(root, PrimitiveType.Cube, "Burrow Floor",
            new Vector3(b.X, b.Ground + 0.0475f, (fz0 + fz1) * 0.5f),
            new Vector3(1.36f, 0.006f, fz1 - fz0),
            Quaternion.identity, MatOf(new Color(0.082f, 0.077f, 0.068f), 0.04f, 0f));
        n++;

        // ⑥ 自检：整条中线做胶囊检测。**这是唯一能证明"钻得进去"的证据** ——
        //    射线在 BoxCollider 内部不报命中，之前就是被这个骗过去的。
        //    胶囊按玩家真实尺寸：脚底 0.048（铺装顶面），高 0.30，半径 0.10。
        int blocked = 0;
        string first = "";
        for (int i = 0; i <= 14; i++)
        {
            float z = Mathf.Lerp(b.ZMouth - 0.10f, b.ZEnd + 0.18f, i / 14f);
            float foot = b.Ground + 0.048f;
            if (Physics.CheckCapsule(new Vector3(b.X, foot + 0.10f, z), new Vector3(b.X, foot + 0.20f, z),
                                     0.095f, ~0, QueryTriggerInteraction.Ignore))
            {
                blocked++;
                if (first.Length == 0)
                {
                    Collider[] why = Physics.OverlapCapsule(new Vector3(b.X, foot + 0.10f, z),
                        new Vector3(b.X, foot + 0.20f, z), 0.095f, ~0, QueryTriggerInteraction.Ignore);
                    for (int w = 0; w < why.Length; w++)
                        if (why[w] != null && UnderPile(why[w].transform) == false) { first = why[w].name; break; }
                    if (first.Length == 0 && why.Length > 0) first = "围壳/方块自己";
                }
            }
        }

        if (blocked > 0)
            Debug.LogWarningFormat("[Nb] 通道自检：中线 15 个采样点里有 {0} 个被挡（第一个挡路的：{1}）—— 玩家可能还是钻不进去。",
                blocked, first);
        else
            Debug.Log("[Nb] 通道自检：中线 15 个采样点的玩家胶囊全空 ✓");

        Debug.LogFormat("[Nb] 奶酪通道已挖通：x={0:F2}，z {1:F2} → {2:F2}（长 {3:F2} m，净空 {4:F2}×{5:F2}）；" +
                        "关掉 {6} 个方块碰撞体，补 4 面围壳 + {7} 件袋子/杂物。",
            b.X, b.ZMouth, b.ZEnd, len, b.HalfW * 2f, b.Height, carved, n);
        return n;
    }

    /// 通道用的袋子：**朝向只绕 Y 转**，缩放固定。
    /// 这样它的水平投影永远是个直径 dia 的圆，内侧面能严格落在净空边界外 6 cm ——
    /// 用 Bag() 那套随机 x/z 缩放 + 三轴旋转，同一只袋子能往洞里鼓 20 cm，相机就撞进袋子里了。
    static void BurrowBag(Transform p, Vector3 at, float dia, Material mat)
    {
        MeshPiece(p, BagMesh(), "Burrow Bag", at,
            new Vector3(dia, dia * 0.54f, dia),
            Quaternion.Euler(Random.Range(-4f, 4f), Random.value * 360f, Random.Range(-4f, 4f)), mat);
    }

    /// 通道内衬的配色：沿用整堆的彩虹袋，但只取较暗的三档（BagA/BagB/BagC）。
    /// 洞里本来就该比外面暗，太艳的袋子会变成一面发亮的墙，还抢洞口外垃圾堆的视线。
    static Material BurrowColor(int i)
    {
        switch (((i % 4) + 4) % 4)
        {
            case 0:  return MatOf(BagB, 0.26f, 0f);
            case 1:  return MatOf(BagC, 0.20f, 0f);
            case 2:  return MatOf(BagA, 0.34f, 0f);
            default: return MatOf(BagB, 0.40f, 0f);
        }
    }

    // ───────────────────────── 一堆 ─────────────────────────

    /// 返回这一堆生成了多少件；0 表示跳过（已经换过 / 量不到）。
    static int Dress(Transform root)
    {
        if (root.Find(ArtName) != null) return 0;      // 换过了，别叠第二层

        Renderer[] grey = root.GetComponentsInChildren<Renderer>(true);
        if (grey.Length == 0) return 0;

        Bounds box;
        if (!Envelope(root, grey, out box) || box.size.sqrMagnitude < 0.01f) return 0;

        // 地面在这一堆的局部坐标里有多高（根节点被整体抬了 0.51 m，不能按 y=0 算）
        float ground = root.InverseTransformPoint(new Vector3(root.position.x, 0f, root.position.z)).y;

        // 🔴 只关渲染。**不要**顺手把 Collider 也删了 —— 那是这一关的掩体。
        for (int i = 0; i < grey.Length; i++)
            if (grey[i] != null) grey[i].enabled = false;

        // 同一堆每局长得一样，不同的堆不一样：用世界坐标当种子。
        // 不固定种子的话每次 Play 垃圾堆都会重排，调好的构图没法复现。
        Random.State saved = Random.state;
        Random.InitState(Mathf.Abs(Mathf.RoundToInt(root.position.x * 71f + root.position.z * 137f)) + 17);

        Transform art = new GameObject(ArtName).transform;
        art.SetParent(root, false);
        art.localPosition = Vector3.zero;
        art.localRotation = Quaternion.identity;
        art.localScale = Vector3.one;

        int n = Build(art, box, ground);

        Random.state = saved;
        return n;
    }

    /// 16 个方块的世界包围盒 → 这一堆在**自身局部空间**里的包络。
    /// 用 8 个角点反变换而不是 `Renderer.bounds` 直接取：后者是世界轴对齐的，
    /// 根节点一旦带旋转（有三份是转了 66° 的），世界盒会把假想的空角一起算进来。
    static bool Envelope(Transform root, Renderer[] rs, out Bounds local)
    {
        local = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;

        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null) continue;
            Bounds w = rs[i].bounds;
            Vector3 c = w.center, e = w.extents;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = c + new Vector3(
                    ((k & 1) == 0) ? -e.x : e.x,
                    ((k & 2) == 0) ? -e.y : e.y,
                    ((k & 4) == 0) ? -e.z : e.z);
                Vector3 p = root.InverseTransformPoint(corner);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }
        return any;
    }

    // ───────────────────────── 构图 ─────────────────────────

    static int Build(Transform art, Bounds b, float ground)
    {
        int n = 0;

        Vector3 c = b.center;
        float hw = Mathf.Max(1.5f, b.extents.x);
        float hd = Mathf.Max(1.5f, b.extents.z);

        // 堆的半径取短边的一个比例。**别铺满整个包络**：留一点空地，
        // 边缘才不会顶到楼墙上（12 堆里有一半是贴着巷子的）。
        float R  = Mathf.Min(hw, hd) * 0.90f;
        float top = Mathf.Clamp(b.max.y, ground + 1.2f, ground + 3.9f);
        float h  = Mathf.Max(1.2f, top - ground);

        // ① 铺底：压扁的纸板/防雨布，先把"这块地是垃圾场"写出来
        n += Sheets(art, c, ground, R);

        // ② 垃圾箱：整堆唯一有硬边的东西，玩家靠它一眼认出"这是垃圾"
        // 🔴 它必须**高过圆丘**，否则会被埋掉（实测：按包络 0.70 给高时，箱顶只比椭球表面高
        // 0.19 局部 = 11 cm，等于消失）。所以高度给到 0.98h，并且往外挪到 0.58hw 那一圈 ——
        // 那里椭球已经很低，箱子能露出整面侧板。
        n += Skip(art, new Vector3(c.x - hw * 0.58f, ground, c.z + hd * 0.10f),
                  Random.Range(-24f, 24f), Mathf.Min(hw * 0.88f, 4.8f), Mathf.Min(h * 0.98f, 3.4f));

        // ③ 袋子圆丘：主体体积。`surf` 是它顺带量出来的"袋子顶面高度表"，
        // 后面纸箱 / 木板 / 防雨布全靠它落位 —— 上一版三处各算一条椭球公式，
        // 对不上就会出现"箱子浮在袋子上方"。
        float[] surf = new float[Bands];
        for (int i = 0; i < Bands; i++) surf[i] = ground;
        n += BagHill(art, c, ground, R, h, surf);

        // ④ 纸箱：把圆丘的轮廓打碎，避免"一个完美的包子"
        n += Boxes(art, c, ground, R, h, surf);

        // ④b 斜插的木板：专门对付"顶上一排圆包"的天际线
        n += Planks(art, c, ground, R, h, surf);

        // ⑤ 防雨布：盖一部分，让表面有第二种材质
        n += Tarp(art, c, ground, R, h, surf);

        // ⑥ 托盘：斜靠在垃圾箱上
        n += Pallet(art, new Vector3(c.x - hw * 0.52f + 1.9f, ground, c.z + hd * 0.10f + 2.1f));

        // ⑦ 散落：易拉罐 / 瓶子 / 纸片，围着堆脚铺一圈 —— 这一圈是给玩家"尺度感"的
        n += Litter(art, c, ground, R);

        return n;
    }

    // 12 版起没有再"算一条曲面"了：堆面高度一律来自 BagHill 实测的 `surf` 表（见 SurfY）。
    // 之前这里那个 DomeA() 是椭球的竖直半轴，椭球删掉之后它算出来的高度和真实袋子无关，
    // 纸箱/木板/防雨布照它落位就会浮空 —— 那正是用户截图里看到的问题。

    // ── ① 铺底 ──
    static int Sheets(Transform p, Vector3 c, float ground, float R)
    {
        int n = 0;
        int count = 2;
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * 6.283f;
            float r = R * Random.Range(0.15f, 0.75f);
            Vector3 at = new Vector3(c.x + Mathf.Cos(a) * r, ground + 0.035f, c.z + Mathf.Sin(a) * r);
            Vector3 size = new Vector3(R * Random.Range(0.7f, 1.15f), 0.07f, R * Random.Range(0.55f, 0.95f));
            // 🔴 通道里的"压平纸箱"必须跳过。它们平时埋在袋子底下看不见，可通道一挖，
            //    3.8 m × 17 cm 的纸板块就露在脚下 —— 渲染里最出戏的就是它：一块带硬边的
            //    棕色厚板横在洞里，读起来像一整块地板/月台，而不是垃圾。铺装地面反而更像个洞。
            // ⚠️ `at` 是 **父节点的局部坐标**，Holds 比的是世界坐标 —— 必须 TransformPoint。
            //    第一版忘了转，这一条永远不成立（件数一件没少），平台照样横在洞里。
            //    余量给到 2.2：纸板块最长 3.8 m（半长 1.9），球心落在盒外 2 m 也压得住通道。
            if (_burrow.On && _burrow.Holds(p.TransformPoint(at), 2.20f)) continue;
            Piece(p, PrimitiveType.Cube, "Sheet " + i, at, size,
                Quaternion.Euler(Random.Range(-2f, 2f), Random.value * 360f, Random.Range(-2f, 2f)),
                MatOf(i == 1 ? CardB : Card, 0.04f, 0f));
            n++;
        }
        return n;
    }

    // ── ② 垃圾箱（开口朝上的铁皮箱，盖子掀在一边）──
    static int Skip(Transform p, Vector3 foot, float yaw, float len, float hgt)
    {
        float wid = Mathf.Min(hgt * 1.15f, len * 0.52f);

        Transform root = new GameObject("Skip").transform;
        root.SetParent(p, false);
        root.localPosition = foot;
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);

        Material body = MatOf(SkipBody, 0.28f, 0.35f);
        Material rim  = MatOf(SkipRim, 0.34f, 0.45f);

        // 箱体：上宽下窄，靠两段叠出来（一个 box 直上直下会像集装箱）
        Piece(root, PrimitiveType.Cube, "Body Low", new Vector3(0f, hgt * 0.36f, 0f),
            new Vector3(len, hgt * 0.72f, wid), Quaternion.identity, body);
        Piece(root, PrimitiveType.Cube, "Body High", new Vector3(0f, hgt * 0.80f, 0f),
            new Vector3(len * 1.02f, hgt * 0.26f, wid * 1.10f), Quaternion.identity, body);
        // 箱口一圈
        Piece(root, PrimitiveType.Cube, "Rim", new Vector3(0f, hgt * 0.96f, 0f),
            new Vector3(len * 1.05f, hgt * 0.07f, wid * 1.13f), Quaternion.identity, rim);
        // 里面是黑的（不然箱子看着是实心的）
        Piece(root, PrimitiveType.Cube, "Fill", new Vector3(0f, hgt * 0.90f, 0f),
            new Vector3(len * 0.96f, hgt * 0.05f, wid * 1.04f), Quaternion.identity,
            MatOf(new Color(0.03f, 0.03f, 0.03f), 0.05f, 0f));

        // 盖子掀开：一侧压在箱口、一侧翘起来。
        // 🔴 用箱体色而不是箱口色：箱口那档（0.215）在夜景里会被路灯打得发白，
        // 铺在箱顶就是一整块"浅灰水泥板"（第一版渲染里非常抢眼，完全不像铁皮）。
        Material lid = MatOf(new Color(0.250f, 0.080f, 0.340f), 0.30f, 0.35f);
        Piece(root, PrimitiveType.Cube, "Lid", new Vector3(len * 0.30f, hgt * 1.06f, 0f),
            new Vector3(len * 0.66f, hgt * 0.06f, wid * 1.10f),
            Quaternion.Euler(0f, 0f, -13f), lid);

        int n = 5;      // 两段箱体 + 箱口 + 内底 + 盖

        // 轮子（竖着的小圆柱 ⇒ 绕 Z 转 90°）
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * len * 0.32f;
            Piece(root, PrimitiveType.Cylinder, "Wheel " + i,
                new Vector3(x, hgt * 0.09f, -wid * 0.46f), new Vector3(hgt * 0.13f, hgt * 0.13f, hgt * 0.13f),
                Quaternion.Euler(0f, 0f, 90f), MatOf(Iron, 0.20f, 0.30f));
            n++;
        }

        // 箱顶再堆三袋溢出来的（不然箱子是空的，一眼看穿）
        for (int i = 0; i < 3; i++)
        {
            float t = (i - 1) * len * 0.26f;
            Bag(root, new Vector3(t, hgt * 1.02f, Random.Range(-wid * 0.25f, wid * 0.25f)),
                Random.Range(hgt * 0.52f, hgt * 0.78f), BagColor(i + 2));
            n += 2;     // 袋身 + 可能有的系口（按最大数记，日志只是给个量级）
        }

        return n;
    }

    // ── ③ 袋子圆丘 ──
    /// 🔴 第 12 版整段重写：从「把袋子贴在数学曲面上」改成「一层一层往上码」。
    ///
    /// 上一版的浮空就是这么来的 —— 袋子被摆在一个椭球表面上（t=0.66 处球心 1.6 m 高），
    /// 而那张椭球在 8 版就被删了 ⇒ 袋子底下什么都没有，玩家平视就看见整排袋子悬在半空
    /// （用户截图：垃圾箱两侧的袋子离地半米，像被吊着）。
    ///
    /// 现在只有一条规则：**第 k 层袋子的底面，一定低于第 k-1 层袋子的顶面**，第 0 层压进路面。
    ///   · 竖直：袋厚 = dia × (0.42…0.62)，竖直半轴 0.21…0.31；层间球心距取 0.30 dia
    ///     ⇒ 上一层球心只比这一层高 0.30 dia，必然嵌进去，不可能悬空；
    ///   · 水平：每上一层圈半径 × 0.82，收成一座锥丘，边缘一路落到地面；
    ///   · 底层球心 y = ground + 0.16 dia ⇒ 底面埋进路面，与地面的交线是一条压实的边，不是一条缝。
    ///
    /// 顺带把「表面」量化了：每放一只袋子就往 `surf` 的对应半径带里记一个**实测顶面高度**，
    /// 后面的纸箱 / 木板 / 防雨布按它落位。上一版三处各算一条椭球公式，彼此对不上，
    /// 那次「箱子浮在袋子上方」也是同一个病根。
    static int BagHill(Transform p, Vector3 c, float ground, float R, float h, float[] surf)
    {
        int n = 0;

        const float Dia0 = 1.95f;     // 局部单位；×0.6 堆缩放 ≈ 1.17 m 一只袋子
        const float Step = 0.30f;     // 层间球心距（相对 dia）
        float rad   = R * 0.96f;
        float yBase = ground;
        int layer   = 0;

        while (yBase < ground + h * 0.94f && layer < 14)
        {
            float dia = Dia0 * Mathf.Lerp(1.00f, 0.78f, layer / 7f);

            // 一圈要几只才不漏：周长 / (袋宽 × 0.74)。0.74 是留给横向重叠的余量。
            int count = Mathf.Clamp(Mathf.RoundToInt(6.283f * rad / (dia * 0.74f)), 3, 26);
            for (int i = 0; i < count; i++)
            {
                float a  = (i + Random.Range(-0.28f, 0.28f)) / count * 6.283f;
                float rr = rad * Random.Range(0.93f, 1.05f);
                float d  = dia * Random.Range(0.90f, 1.10f);
                Vector3 at = new Vector3(c.x + Mathf.Cos(a) * rr, yBase + d * 0.16f, c.z + Mathf.Sin(a) * rr);
                Bag(p, at, d, BagColor(layer * 3 + i));
                Note(surf, c, at, d, R);
                n++;
            }

            // 圈内补几只：每层只摆一圈的话，半径大的层中间是空的（俯视直接看见地面）。
            int inner = Mathf.Max(0, Mathf.RoundToInt(rad / (dia * 1.55f)) - 1);
            for (int i = 0; i < inner; i++)
            {
                float a  = Random.value * 6.283f;
                float rr = rad * Random.Range(0f, 0.58f);
                float d  = dia * Random.Range(0.82f, 0.98f);
                Vector3 at = new Vector3(c.x + Mathf.Cos(a) * rr, yBase + d * 0.26f, c.z + Mathf.Sin(a) * rr);
                Bag(p, at, d, BagColor(i + 5));
                Note(surf, c, at, d, R);
                n++;
            }

            yBase += dia * Step;
            rad   *= 0.82f;
            layer++;
        }

        // 顶上一只：让最高点落在「一只袋子」上，而不是某一圈的运气。
        Vector3 top = new Vector3(c.x + Random.Range(-0.3f, 0.3f) * Dia0,
                                  yBase + Dia0 * 0.10f,
                                  c.z + Random.Range(-0.3f, 0.3f) * Dia0);
        Bag(p, top, Dia0 * 0.78f, BagColor(3));
        Note(surf, c, top, Dia0 * 0.78f, R);
        n++;

        // 堆脚散落几袋（坐在地上），把底圈和路面的交界打碎 —— 否则那条边是一个完整的圆。
        for (int i = 0; i < 6; i++)
        {
            float a  = Random.value * 6.283f;
            float rr = R * Random.Range(0.94f, 1.14f);
            float d  = Random.Range(0.95f, 1.45f);
            // 0.30：袋子竖直半轴 ≈ 0.26 d，取 0.30 让底面压进路面
            Bag(p, new Vector3(c.x + Mathf.Cos(a) * rr, ground + d * 0.30f, c.z + Mathf.Sin(a) * rr),
                d, BagColor(i + 3));
            n++;
        }

        return n;
    }

    const int Bands = 12;

    /// 半径 → 半径带。带上限取 1.05R：堆脚的散袋会落到 R 之外，别让它们越界。
    static int BandOf(float r, float R)
    {
        float t = Mathf.Clamp01(r / (R * 1.05f));
        return Mathf.Clamp((int)(t * Bands), 0, Bands - 1);
    }

    /// 记一只袋子的**实测顶面**。竖直半轴取上限 0.31 dia —— 宁可高估一厘米，
    /// 也不要低估：低估会让后面放上去的纸箱沉进袋子，高估只是压进去一点点。
    static void Note(float[] surf, Vector3 c, Vector3 at, float dia, float R)
    {
        float dx = at.x - c.x, dz = at.z - c.z;
        int b = BandOf(Mathf.Sqrt(dx * dx + dz * dz), R);
        float top = at.y + dia * 0.31f;
        if (top > surf[b]) surf[b] = top;
    }

    /// 半径 r 处的堆面高度。取本带和里一带的较大值：宁可踩在更高的那只袋子上，
    /// 也不要落在两只袋子中间的凹坑里 —— 凹坑正是「看起来悬空」的来源。
    static float SurfY(float[] surf, float r, float R)
    {
        int b = BandOf(r, R);
        float y = surf[b];
        if (b > 0 && surf[b - 1] > y) y = surf[b - 1];
        return y;
    }

    /// 一只垃圾袋：**揉皱过的**球 + 一个系口。
    ///
    /// 🔴 第 6 版之前这里用的是 `PrimitiveType.Sphere`，压扁之后每只都是同一个橄榄球，
    /// 一圈排开就是"一堆磨光的黑鹅卵石"（`nb_shots_before` 里那只黑石头的真正原因，
    /// 不是颜色 —— 是轮廓完全一样）。现在改用一张**顶点被揉过的共享网格**：
    /// 三个八度的正弦相乘当位移场，表面长出大鼓包 + 小褶子，剪影不再是一个椭圆。
    /// 网格只有一份（12 堆共用），每只袋子靠各自的缩放/旋转错开，没有额外开销。
    ///
    /// 压扁（y ≈ 0.42…0.62）仍然是刻意的：球形 24 个堆起来像气球，压扁才像"装了东西瘫着"。
    static void Bag(Transform p, Vector3 at, float dia, Material mat)
    {
        // 奶酪通道的净空里不落袋。判的是**球心**：中心在外、只把肚子压进来一点的那几只
        // 要留着，它们是通道壁的软边（全剔掉，洞壁就成了一圈整齐的球面）。
        if (_burrow.On && _burrow.Holds(p.TransformPoint(at))) return;

        Quaternion rot = Quaternion.Euler(Random.Range(-24f, 24f), Random.value * 360f, Random.Range(-24f, 24f));
        MeshPiece(p, BagMesh(), "Bag",
            at, new Vector3(dia * Random.Range(0.78f, 1.34f), dia * Random.Range(0.42f, 0.62f), dia * Random.Range(0.74f, 1.22f)),
            rot, mat);

        // 系口：袋子顶上那个揪揪。只在部分袋子上加，全加会太规律。
        if (Random.value < 0.30f)
            Piece(p, PrimitiveType.Sphere, "Knot",
                at + new Vector3(0f, dia * 0.24f, 0f), Vector3.one * dia * 0.19f,
                rot, mat);
    }

    /// 揉皱的袋子网格：把内建球（半径 0.5）的顶点按位置推出去/拉回来。
    /// 用**顶点坐标**当输入而不是顶点序号 —— UV 缝上有一批位置相同、序号不同的重复顶点，
    /// 按序号揉会在缝上撕开一道裂口。
    ///
    /// 两份：`gain = 1` 给袋子（褶子深），`gain ≈ 0.42` 给下面那层堆体（只求"别是个光滑的碟子"）。
    /// （8 版起堆体已经不用网格了，`gain` 参数留着 —— 它是唯一能调"揉得多狠"的旋钮。）
    static Mesh _bagMesh;
    static Mesh BagMesh() { return _bagMesh != null ? _bagMesh : (_bagMesh = Crumple(1.00f)); }

    static Mesh Crumple(float gain)
    {
        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter mf = tmp.GetComponent<MeshFilter>();
        Mesh m = Object.Instantiate(mf.sharedMesh);
        m.name = "Nb_Crumpled" + gain.ToString("F2");

        float a1 = 0.135f * gain, a2 = 0.060f * gain, a3 = 0.032f * gain;
        Vector3[] v = m.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 n = v[i].normalized;
            float f = Lobe(n, 3.1f, 1.7f) * a1        // 大鼓包
                    + Lobe(n, 5.3f, 4.1f) * a2        // 中褶
                    + Lobe(n, 8.9f, 2.3f) * a3;       // 小皱
            v[i] = n * (0.5f * (1f + f));
        }
        m.vertices = v;
        m.RecalculateNormals();
        m.RecalculateBounds();

        Nb.Kill(tmp);
        return m;
    }

    /// 三个不同频率的正弦相乘 = 一个连续、无方向性的鼓包场（±1）。
    /// 单用 `sin(n.x*k)` 会得到一片"搓衣板"，横着看是条纹。
    static float Lobe(Vector3 n, float k, float phase)
    {
        return Mathf.Sin(n.x * k + phase)
             * Mathf.Cos(n.y * k * 0.80f - phase)
             * Mathf.Sin(n.z * k * 1.10f + phase * 0.70f);
    }

    /// 袋子材质：**亮度分层 + 光泽分层**，两样都要。
    /// 夜灯下塑料袋唯一能被看见的特征就是那一点高光 —— 第 5 版里全体 0.06…0.14 的粗糙度，
    /// 每只袋子的明暗过渡一样平，挤在一起就并成了一个整体（`B_alley_west.png` 那只"黑石头"）。
    /// 现在黑袋子反而给最高的光泽（黑塑料本来就最反光），编织袋最哑，亮面/哑面交替排开。
    static Material BagColor(int i)
    {
        int k = ((i % 10) + 10) % 10;
        switch (k)
        {
            case 0:  return MatOf(BagC, 0.42f, 0f);    // 灰袋子，亮面
            case 1:  return MatOf(BagA, 0.50f, 0f);    // 黑袋子，最亮的高光
            case 2:  return MatOf(BagB, 0.26f, 0f);    // 旧褐袋子，半哑
            case 3:  return MatOf(BagD, 0.14f, 0f);    // 编织袋，哑
            case 4:  return MatOf(BagA, 0.46f, 0f);
            case 5:  return MatOf(BagE, 0.40f, 0f);    // 脏白袋：整堆里最亮的一只
            case 6:  return MatOf(BagC, 0.30f, 0f);
            case 7:  return MatOf(BagB, 0.48f, 0f);
            case 8:  return MatOf(BagF, 0.12f, 0f);    // 黄编织袋，哑
            default: return MatOf(BagA, 0.24f, 0f);
        }
    }

    // ── ④ 纸箱 ──
    static int Boxes(Transform p, Vector3 c, float ground, float R, float h, float[] surf)
    {
        int n = 0;
        int count = 8;

        for (int i = 0; i < count; i++)
        {
            float w = Random.Range(1.3f, 2.4f);
            float d = Random.Range(1.1f, 2.0f);
            float ht = Random.Range(0.60f, 1.10f);

            float a = Random.value * 6.283f;
            float r = R * Random.Range(0.35f, 1.02f);

            // 🔴 **一律按实测的袋子顶面落位**，不再分"一半在地上、一半架着"。
            // 上一版架着的那几只用的是椭球公式：椭球早删了，公式算出来的高度和实际袋子对不上，
            // 于是箱子就浮在袋子上方（用户截图里那块悬着的棕色板子）。
            //
            // 🔴 但"取一个点的高度"还不够：堆面是斜的，一只 1.3…2.4 宽的平底箱子，
            // 只在一个点上贴着坡，另一头必然翘在空气里（13 版 `B_alley_west.png` 中间那两只）。
            // 所以要沿**半径方向采两个点**，算出坡度，让箱子顺着坡躺下去。
            float radNear = Mathf.Max(0f, r - d * 0.5f);
            float radFar  = r + d * 0.5f;
            float sNear = SurfY(surf, radNear, R);
            float sFar  = SurfY(surf, radFar, R);
            float slope = Mathf.Atan2(sFar - sNear, Mathf.Max(0.05f, radFar - radNear)) * Mathf.Rad2Deg;

            // 高度取两端的**较低**那一头再抬起来一点：底面的低边埋进袋子，高边刚好贴住坡。
            // （取平均会让低边悬空，取高边会把整只箱子抬起来 —— 两种都试过，只有取低的不会飞。）
            float y = Mathf.Min(sNear, sFar) + ht * 0.30f;

            // 压在奶酪通道净空里的箱子不摆：它比袋子大得多，只要有一只横在洞里，
            // 就是一块挡在通道正中的硬边（没有碰撞体，人从它中间穿过去，最难看）。
            Vector3 atBox = new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
            if (_burrow.On && _burrow.Holds(p.TransformPoint(atBox), 0.55f)) continue;

            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Quaternion lay = Quaternion.LookRotation(radial, Vector3.up)
                           * Quaternion.Euler(-slope, 0f, 0f)          // 躺平在坡上（+Z 指向坡外）
                           * Quaternion.Euler(0f, Random.Range(-18f, 18f), 0f);   // 再歪一点，别都朝着坡

            Transform root = new GameObject("Carton").transform;
            root.SetParent(p, false);
            root.localPosition = new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
            root.localRotation = lay;

            Material card = MatOf(i % 2 == 0 ? Card : CardB, 0.05f, 0f);
            Piece(root, PrimitiveType.Cube, "Box", Vector3.zero, new Vector3(w, ht, d), Quaternion.identity, card);

            // 胶带：顶面一道窄条，让纸箱不是纯色方块
            if (i % 2 == 0)
                Piece(root, PrimitiveType.Cube, "Tape", new Vector3(0f, ht * 0.5f + 0.015f, 0f),
                    new Vector3(w * 0.16f, 0.03f, d * 0.98f), Quaternion.identity, MatOf(Tape, 0.10f, 0f));

            n += 2;
        }
        return n;
    }

    // ── ④b 斜插的木板 ──
    // 一堆袋子再怎么摆，天际线都是一串圆包。斜着捅几块板出去，剪影立刻"脏"起来 ——
    // 这是最便宜的"不像球"的办法，一块板两个三角面。
    static int Planks(Transform p, Vector3 c, float ground, float R, float h, float[] surf)
    {
        int n = 0;
        for (int i = 0; i < 5; i++)
        {
            float a = Random.value * 6.283f;
            float r = R * Random.Range(0.40f, 0.85f);
            // 板子一头插进堆里，基准点就在实测堆面上、再往下埋 0.22；
            // 翘起来的那一头靠**倾角**实现，不靠抬高基准点 —— 抬起来就是悬空。
            float surface = SurfY(surf, r, R);

            // 木板长 1.8~2.6 m，基准点在通道外一米也照样会有一头捅进洞里
            // （渲染里那根**竖在洞口正中的棕色柱子**就是它）。所以按大件余量剔。
            Vector3 atPlank = new Vector3(c.x + Mathf.Cos(a) * r, surface - 0.22f, c.z + Mathf.Sin(a) * r);
            if (_burrow.On && _burrow.Holds(p.TransformPoint(atPlank), 0.90f)) continue;

            Transform root = new GameObject("Plank").transform;
            root.SetParent(p, false);
            root.localPosition = atPlank;
            // 🔴 倾角从 -38°…-18° 改到 -58°…-34°、长度从 3.8 缩到 2.6：
            // 板子长 3.8、只压 18° 时，露出堆外的那一头离地一米多，横在空中像一块漂浮的板
            // （第 12 版 `D_near.png` 左侧那两根）。**竖一点、短一点**，读作"插在垃圾里的板"。
            root.localRotation = Quaternion.Euler(Random.Range(-58f, -34f), Random.value * 360f, Random.Range(-22f, 22f));

            Piece(root, PrimitiveType.Cube, "Board", Vector3.zero,
                new Vector3(Random.Range(1.8f, 2.6f), 0.11f, Random.Range(0.28f, 0.50f)),
                Quaternion.identity, MatOf(Wood, 0.06f, 0f));
            n++;
        }
        return n;
    }

    // ── ⑤ 防雨布 ──
    static int Tarp(Transform p, Vector3 c, float ground, float R, float h, float[] surf)
    {
        int n = 0;
        Material tarp = MatOf(TarpBlue, 0.30f, 0f);

        // 盖在堆的**斜坡**上，而不是横在堆顶。
        // 横在堆顶（原来写 0.42~0.55h）在渲染里看起来像一块悬空的灰色板子 ——
        // 它比圆丘的弧面平，两头必然翘在空气里。
        //
        // 🔴 13 版之前它还有一个更笨的问题：一张 R×1.05 见方的**硬板**（≈3 m）架在弧面上，
        // 中间贴着、两头悬空。布可以是软的，但渲染出来是一块平板 —— 所以现在
        // ① 缩到 0.62R×0.52R（约 1.1 m，像一块扯下来的布），② 用实测坡度决定倾角。
        float px = R * 0.34f, pz = -R * 0.20f;
        float r0 = Mathf.Sqrt(px * px + pz * pz);
        float len = R * 0.52f;
        float sNear = SurfY(surf, Mathf.Max(0f, r0 - len * 0.5f), R);
        float sFar  = SurfY(surf, r0 + len * 0.5f, R);
        float slope = Mathf.Atan2(sFar - sNear, Mathf.Max(0.05f, len)) * Mathf.Rad2Deg;

        Vector3 radial = new Vector3(px, 0f, pz).normalized;
        Quaternion lay = Quaternion.LookRotation(radial, Vector3.up)
                       * Quaternion.Euler(-slope, 0f, 0f)
                       * Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);

        Vector3 at = new Vector3(c.x + px, Mathf.Min(sNear, sFar) - 0.02f, c.z + pz);
        // 防雨布是 1.8×1.5 的一块硬板，正好压在堆心（也就是通道中段）上方。
        // 它的底面只要低过净空顶，洞里抬头就是一块蓝灰色的天花板 —— 剔掉。
        if (!_burrow.On || !_burrow.Holds(p.TransformPoint(at), 0.30f))
        {
            Piece(p, PrimitiveType.Cube, "Tarp", at,
                new Vector3(R * 0.62f, 0.07f, R * 0.52f), lay, tarp);
            n++;
        }

        // 地上一角没拉走的
        Vector3 at2 = new Vector3(c.x - R * 0.75f, ground + 0.04f, c.z - R * 0.85f);
        if (!_burrow.On || !_burrow.Holds(p.TransformPoint(at2), 0.20f))
        {
            Piece(p, PrimitiveType.Cube, "Tarp Scrap", at2,
                new Vector3(R * 0.55f, 0.06f, R * 0.45f),
                Quaternion.Euler(0f, Random.value * 360f, 0f), tarp);
            n++;
        }

        return n;
    }

    // ── ⑥ 木托盘（斜靠）──
    static int Pallet(Transform p, Vector3 foot)
    {
        // 🔴 托盘高 1.15、板面 2.6 长，斜靠起来正好横在 8 号堆的通道上 ——
        // 渲染里洞口正中那块**方方正正的暗墙**就是它（`Lean/Deck`，世界包围盒 1.63×1.04×0.82，
        // 一直顶到 x=93.9，而净空只到 94.0）。这种大件基准点差一米也会横穿通道，按 0.9 的余量剔。
        if (_burrow.On && _burrow.Holds(p.TransformPoint(foot), 0.90f)) return 0;

        // 🔴 抬升量必须和倾角配套算：板面 1.9 深、压 62° 时，下沿比中心低
        // (1.9/2)·sin62° ≈ 0.84，再加上底下那 3 根垫木，原来抬 1.15 就悬在半空 0.3 m。
        // 现在抬 0.78 ⇒ 下沿连同垫木一起压进路面（宁可埋一点，埋一点看不出来，浮着一眼就看出来）。
        Transform root = new GameObject("Pallet").transform;
        root.SetParent(p, false);
        root.localPosition = foot + new Vector3(0f, 0.78f, 0f);
        root.localRotation = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f);
        root.localScale = Vector3.one;

        // 整块倾斜：绕自身 X 轴倒向垃圾堆
        Transform tilt = new GameObject("Lean").transform;
        tilt.SetParent(root, false);
        tilt.localRotation = Quaternion.Euler(62f, 0f, 0f);

        Material wood = MatOf(Wood, 0.06f, 0f);
        Piece(tilt, PrimitiveType.Cube, "Deck", Vector3.zero, new Vector3(2.6f, 0.12f, 1.9f), Quaternion.identity, wood);

        int n = 1;
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 1.0f;
            Piece(tilt, PrimitiveType.Cube, "Runner " + i, new Vector3(x, -0.16f, 0f),
                new Vector3(0.34f, 0.20f, 1.9f), Quaternion.identity, wood);
            n++;
        }
        return n;
    }

    // ── ⑦ 散落杂物 ──
    static int Litter(Transform p, Vector3 c, float ground, float R)
    {
        int n = 0;

        // 易拉罐：一半躺着（绕 X 转 90°），一半立着
        for (int i = 0; i < 6; i++)
        {
            float a = Random.value * 6.283f;
            float r = R * Random.Range(0.92f, 1.22f);
            bool lying = Random.value < 0.6f;
            Vector3 at = new Vector3(c.x + Mathf.Cos(a) * r, ground + (lying ? 0.10f : 0.15f), c.z + Mathf.Sin(a) * r);
            Quaternion rot = lying
                ? Quaternion.Euler(90f, Random.value * 360f, 0f)
                : Quaternion.Euler(0f, Random.value * 360f, 0f);
            // 罐子按真实尺寸给：0.20 局部 = 0.12 m 高、0.12 m 直径（老鼠站着到它胸口）
            Piece(p, PrimitiveType.Cylinder, "Can " + i, at, new Vector3(0.20f, 0.30f, 0.20f), rot,
                MatOf(Alu, 0.55f, 0.65f));
            n++;
        }

        // 玻璃瓶：瓶身 + 瓶颈。两种玻璃色，给暗部一点彩
        for (int i = 0; i < 2; i++)
        {
            float a = Random.value * 6.283f;
            float r = R * Random.Range(0.95f, 1.20f);
            Vector3 at = new Vector3(c.x + Mathf.Cos(a) * r, ground + 0.06f, c.z + Mathf.Sin(a) * r);
            float yaw = Random.value * 360f;
            bool lying = Random.value < 0.55f;
            Quaternion rot = lying ? Quaternion.Euler(84f, yaw, 0f) : Quaternion.Euler(0f, yaw, 0f);
            Vector3 lift = lying ? new Vector3(0f, 0.10f, 0f) : new Vector3(0f, 0.28f, 0f);

            Transform root = new GameObject("Bottle " + i).transform;
            root.SetParent(p, false);
            root.localPosition = at + lift;
            root.localRotation = rot;

            Material glass = MatOf(i == 0 ? GlassG : GlassB, 0.45f, 0f);
            Piece(root, PrimitiveType.Cylinder, "Body", Vector3.zero, new Vector3(0.30f, 0.54f, 0.30f),
                Quaternion.identity, glass);
            Piece(root, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.35f, 0f), new Vector3(0.13f, 0.22f, 0.13f),
                Quaternion.identity, glass);
            n += 2;
        }

        // 纸片：薄板，随机倒角 —— 有它们，地面上才不会只有"干净的影子"
        for (int i = 0; i < 5; i++)
        {
            float a = Random.value * 6.283f;
            float r = R * Random.Range(0.85f, 1.28f);
            Piece(p, PrimitiveType.Cube, "Paper " + i,
                new Vector3(c.x + Mathf.Cos(a) * r, ground + Random.Range(0.02f, 0.05f), c.z + Mathf.Sin(a) * r),
                new Vector3(Random.Range(0.30f, 0.62f), 0.02f, Random.Range(0.35f, 0.70f)),
                Quaternion.Euler(Random.Range(-4f, 4f), Random.value * 360f, Random.Range(-4f, 4f)),
                MatOf(Paper, 0.03f, 0f));
            n++;
        }

        return n;
    }

    // ───────────────────────── 小工具 ─────────────────────────

    /// 造一个零件。**建完立刻把碰撞体关掉再销毁** —— 这些零件只负责看，
    /// 一旦留了碰撞体，猫的视线射线和玩家的移动就会撞在垃圾袋上（掩体判定已经由原来的灰方块负责了）。
    static GameObject Piece(Transform parent, PrimitiveType type, string name,
        Vector3 pos, Vector3 size, Quaternion rot, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;        // Destroy 是延迟到帧末的，先关掉才不会有这一帧的空窗
            Nb.Kill(col);
        }

        int layer = Nb.DecorLayer;
        if (layer >= 0) go.layer = layer;

        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = rot;
        // Unity 的圆柱高 2、球直径 1，调用方统一按"外接尺寸"给，这里换算
        t.localScale = (type == PrimitiveType.Cylinder)
            ? new Vector3(size.x, size.y * 0.5f, size.z)
            : size;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;

        return go;
    }

    static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

    /// 和 `Piece` 一样，只是几何体来自一张现成的网格（揉皱的袋子用）。
    /// 网格直径 1、自带 MeshRenderer，所以 `size` 的语义和球/方块那一支完全一致。
    static GameObject MeshPiece(Transform parent, Mesh mesh, string name,
        Vector3 pos, Vector3 size, Quaternion rot, Material mat)
    {
        GameObject go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        int layer = Nb.DecorLayer;
        if (layer >= 0) go.layer = layer;

        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = rot;
        t.localScale = size;

        return go;
    }

    /// 按 (颜色, 光滑度, 金属度) 缓存 —— 12 堆共用十来个材质，
    /// 每件都 new 一个材质的话 SRP Batcher 会直接失效。
    static Material MatOf(Color c, float smooth, float metal)
    {
        string key = ColorUtility.ToHtmlStringRGBA(c) + "|" + smooth.ToString("F2") + "|" + metal.ToString("F2");
        Material m;
        if (Mats.TryGetValue(key, out m) && m != null) return m;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        m = new Material(sh);
        m.name = "Nb_Rubbish_" + ColorUtility.ToHtmlStringRGB(c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);

        Mats[key] = m;
        return m;
    }
}
