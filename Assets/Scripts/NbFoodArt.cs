// NbFoodArt.cs — 冰箱里的商品装饰（用你自己导进来的三个食物包，纯装饰，不参与玩法）
//
// ───────────────────────── 这些东西是什么 ─────────────────────────
// 工程根目录下有三个素材包，原样放在 Assets/ 里 Unity 是 **加载不到** 的（运行时没有
// AssetDatabase）。所以这里用了一份「Resources 副本」：
//
//   Assets/Resources/Food/Grocery/SM_Food_Collection_01.fbx   41 件：罐头 / 香肠 / 麦片 / 蛋白粉 …
//   Assets/Resources/Food/Vintage/food.fbx                    10 件：番茄 / 玉米 / 牛奶 / 三文鱼 …
//   Assets/Resources/Food/Pastry/pastry.glb                    1 件：六个面包合成的一坨
//   Assets/Resources/Food/*/Tex/*.png                          对应的贴图（只拷了 base color）
//
// 原目录一个字没动。要换素材/加素材，改这里的表就行。
//
// ───────────────────────── 为什么要自己写材质 ─────────────────────────
// FBX 里记的贴图路径是作者机器上的绝对路径（C:\Users\pekar\Desktop\Blender\Food\food1.png），
// Unity 只能按**文件名**去项目里找，food1.png / rusted can.png 这些根本不存在 ⇒
// 材质会带一张空贴图进来；而这类 FBX 的材质描述在 URP 工程里还会被建成 Standard 材质
// ⇒ 屏幕上是一堆洋红色的东西（而编辑器里看不出任何报错）。
// 所以：贴图由本文件按表直接指定，材质自己 new 成 URP/Lit。
//
// 贴图归属不是我瞎猜的 —— 每张图都打开看过：
//   T_Food_Collection_01_C  = 罐头/香肠/麦片/蛋白粉/糖果（Food_Collection 那一组）
//   T_Bottles_Collection_01_C = 酒瓶/油瓶/啤酒/牛奶瓶/清洁剂（Bottles 那一组）
//   Vintage 那套是一物一图（food.png 是番茄/玉米/牛奶/三文鱼四格合集）
//
// ───────────────────────── 摆放原则 ─────────────────────────
// **只在三处摆**，因为这三处永远不会和掩体重叠：
//   ① 每层后壁的两排（9 个堆头 ×3 件）—— 一进门看到的那面"货墙"
//   ② 每层左右内壁（各 3 个堆头 ×2 件）—— 冰箱两侧本来就是放瓶子的地方
//   ③ 每层台阶两侧的平台（左右各 2 件）—— 爬楼梯时正好在视线高度上
// 掩体的 z 被夹在 [后壁 + 2.4, 前沿 − 2.0]，所以 ① ② 天然落在掩体范围之外；
// 每个坑位最后还会再查一次"离本层奶酪够不够远"（BlocksCheese）。
// 七层合计约 470 件，密度只由下面那五个常量决定。
//
// 大小由 `SizeMul` 一个系数统管（表里的 h 是**原始**高度，摆的时候才乘）。
// 改大小时堆内间距、抖动、避奶酪半径会跟着一起长 —— 只放大模型不放大间距，
// 一堆三件会插进彼此体内，看起来像"模型加载坏了"。
//
// 全部零件：**实心**（每件挂一个贴合模型局部包围盒的 BoxCollider），
// 玩家撞上去会被挡住，不会再从罐头中间穿过去。
//
// 但"实心"必须同时满足两条，否则这一关会当场坏掉 —— 两条都是踩过的坑：
//
//   ① **全部留在 Ignore Raycast 层。** `CatCheeseSetup.TryGroundY` 与猫的视线用的是
//      `Physics.DefaultRaycastLayers`（不含这一层）。留在这一层，地面探测就永远打在
//      地板上、不会打在罐头顶上 —— 否则奶酪会被"贴"到某个瓶盖上。
//   ② **门光判定 `IsHidden()` 必须把这一层排除掉。** 它是从门口朝玩家打 SphereCast
//      找遮挡物；474 件实心商品如果算数，站在一听可乐后面就等于"藏好了"，
//      整关只剩"随便往货架边一站"，难度直接归零。
//      所以那一处用的是 DefaultRaycastLayers 而不是 ~0（见 NeighbourhoodGame.IsHidden）。
//
// 反过来说：只要这两条还成立，商品的碰撞体就只影响"人走得过去吗"，
// 不影响"光打不打得到我"—— 而"人走不过去"正是穿模的反面。

using System.Collections.Generic;
using UnityEngine;

internal static class NbFoodArt
{
    // ───────────────────────── 商品表 ─────────────────────────

    struct Spec
    {
        internal string item;   // FBX 里的节点名（大小写/下划线/空格不敏感，前缀匹配）
        internal string tex;    // Resources 里的贴图路径，空 = 不贴图
        internal float h;       // 目标高度（米）—— **乘 SizeMul 之前**的原始值

        internal Spec(string i, string t, float h) { item = i; tex = t; this.h = h; }
    }

    const string GroceryPack = "Food/Grocery/SM_Food_Collection_01";
    const string VintagePack = "Food/Vintage/food";
    const string PastryPack  = "Food/Pastry/pastry";

    const string TexFood    = "Food/Grocery/Tex/T_Food_Collection_01_C";
    const string TexBottles = "Food/Grocery/Tex/T_Bottles_Collection_01_C";
    const string TexDir     = "Food/Vintage/Tex/";

    /// 41 件的杂货包。这里只挑**贴图归属能确定**的那几件（表里没写的一律不用）。
    static readonly Spec[] Grocery =
    {
        new Spec("SM_Canned_Food_05", TexFood,    0.17f),
        new Spec("SM_Canned_Food_07", TexFood,    0.17f),
        new Spec("SM_Canned_Food_09", TexFood,    0.15f),
        new Spec("SM_Canned_Food_11", TexFood,    0.18f),
        new Spec("SM_Canned_Food_13", TexFood,    0.16f),
        new Spec("SM_Sausages_01",    TexFood,    0.13f),
        new Spec("SM_Porridge_01",    TexFood,    0.21f),
        new Spec("SM_Protein_Jar_01", TexFood,    0.23f),
        new Spec("SM_Pasta_01",       TexFood,    0.20f),
        new Spec("SM_Candies_01",     TexFood,    0.14f),
        new Spec("SM_Wine_Bottle_01", TexBottles, 0.28f),
        new Spec("SM_Olive_Oil_01",   TexBottles, 0.24f),
        new Spec("SM_Beer_Bottle_01", TexBottles, 0.26f),
        new Spec("SM_Milk_Bottle_01", TexBottles, 0.25f),
    };

    /// 老式罐头皮那套：10 件，一物一图，最不用猜的一组。
    static readonly Spec[] Vintage =
    {
        new Spec("Tomatoes", TexDir + "food",       0.17f),
        new Spec("Corn",     TexDir + "food",       0.17f),
        new Spec("Milk",     TexDir + "food",       0.18f),
        new Spec("Salmon",   TexDir + "food",       0.16f),
        new Spec("Cake",     TexDir + "cake",       0.14f),
        new Spec("Cherry",   TexDir + "cherry",     0.13f),
        new Spec("Egg",      TexDir + "egg",        0.12f),
        new Spec("Cookie",   TexDir + "Cookie",     0.11f),
        new Spec("Raisins",  TexDir + "rasins",     0.12f),
        new Spec("Fish",     TexDir + "fishsticks", 0.15f),
    };

    // ───────────────────────── 摆放密度 ─────────────────────────
    //
    // 这几个数是**唯一该动的地方**。原来每层 12 件（后壁 8 + 台阶两侧 4），
    // 26 m 宽的后壁上摊 8 件 —— 那不是货架，那是"撒了点东西"。
    // 现在改成**成堆**：一个堆头 3 件肩并肩（间距 BackGap），堆头之间留空。
    // 挤成一块一块才像货架是满的；均匀摊开反而空。
    //
    // 想再密/再稀就只改这五个数，位置逻辑一行都不用碰。

    //   （模型尺寸见下面的 SizeMul）

    /// 商品统一放大系数。**只改这一个数就能整体变大变小。**
    ///
    /// 原始尺寸是按"真冰箱"定的（0.11~0.28 m），但这是一只 30 cm 高的老鼠在
    /// 24×20 m 的巨型冰箱里跑 —— 一件 17 cm 的罐头在 15 m 外只有几个像素，
    /// 走进去看也像"地上撒了点东西"。放 1.5× 之后最矮的 0.17、最高的瓶子 0.42。
    ///
    /// **上限是 1.8，不是随便填的**：层内净空 = LayerH 0.56 − 板厚 0.05 = 0.51 m，
    /// 最高的商品（酒瓶 0.28）乘 1.8 就是 0.50 —— 再大就顶穿上层搁板了。
    internal const float SizeMul = 1.5f;

    const int   BackCols = 8;      // 后壁一排几个堆头
    const int   BackRows = 2;      // 后壁几排（沿 z 往里，靠后壁那两排）
    const int   BackPer  = 3;      // 一个堆头几件
    // 堆内间距必须跟着 SizeMul 走：模型放大 1.5 倍、间距不动的话，
    // 一个堆头里的三件会插进彼此的身体里（0.34 的间距装 0.4 宽的蛋糕）。
    const float BackGap  = 0.34f * SizeMul;   // 堆头内相邻两件的间距（米）
    const float BackSpan = NbFridge.InnerW - 3.0f;   // 后壁一排铺开的宽度（x），以 CX 为中心，两侧各留 1.5 m 给侧壁货带
    const float BackRowZ = 0.62f * SizeMul;   // 后壁两排之间的距离

    const int   SideRuns = 6;      // 每侧壁几个堆头（沿 z 从后壁一直铺到房间前沿）
    const int   SidePer  = 2;      // 侧壁一个堆头几件
    const float SideGap  = 0.34f * SizeMul;
    const float SideStep = 2.3f;   // 侧壁两个堆头之间的距离（够大，不跟 SizeMul 走）

    /// 侧壁第一堆离后壁内面的距离。
    /// 后壁第二排的中心在 0.55 + BackRowZ（=1.48），再加半件深 ≈ 0.21 ⇒ 占到 1.69。
    /// 原来写 0.90，两侧第一堆正好塞进后壁两排之间 —— 放大 1.5 倍之后，
    /// 四个角上后壁最外一件和侧壁第一件会互相插进去（离线复算里唯一的 6 处重叠）。
    const float SideZ0 = 2.10f;

    // ───────────────────────── 对外入口 ─────────────────────────

    /// 往冰箱里摆装饰。返回摆成功的件数（0 = 三个包一个都没加载上，已退回几何体）。
    internal static int DecorateFridge(Transform root)
    {
        _mats.Clear();
        _colliders = 0;

        Dictionary<string, GameObject> tpl = new Dictionary<string, GameObject>();
        Harvest(GroceryPack, Grocery, tpl);
        Harvest(VintagePack, Vintage, tpl);
        HarvestPastry(tpl);

        if (tpl.Count == 0)
            Debug.LogWarning("[FoodArt] 三个食物包一个都没加载上（Resources/Food 里没有可用模型）——" +
                             "架子会退回 NbFood 的几何体食物。检查 Unity 是否已把 .fbx/.glb 导入完。");

        string[] keys = new string[tpl.Count];
        tpl.Keys.CopyTo(keys, 0);
        System.Array.Sort(keys, string.CompareOrdinal);

        int placed = 0, miss = 0, skip = 0;

        for (int n = 0; n <= NbFridge.Layers; n++)
        {
            float y = NbFridge.Top(n);

            // ① 后壁两排。这是"这一层看着是满的"最主要的来源 ——
            //    玩家从门口往里看，看到的就是后壁那片货墙。
            for (int row = 0; row < BackRows; row++)
            {
                float z = NbFridge.ZMin + NbFridge.WallT + 0.55f + row * BackRowZ;
                for (int c = 0; c < BackCols; c++)
                {
                    float t = (BackCols <= 1) ? 0.5f : c / (float)(BackCols - 1);
                    float x = NbFridge.CX + (t - 0.5f) * BackSpan;
                    Cluster(root, tpl, keys, new Vector3(x, y, z), BackPer, BackGap,
                        n * 131 + row * 29 + c * 7, n, ref placed, ref miss, ref skip);
                }
            }

            // ② 两侧壁：从后壁一直铺到房间前沿。
            //
            //    这一条是"堆满"的关键，不是后壁。后壁那两排离门有 15~19 m，
            //    玩家站在门口往里看，中间十几米是空地板 + 六个掩体，只有尽头一堵货墙 ——
            //    看起来还是空的。侧壁铺满之后，从门口望进去是"两条货带夹着中间过道"，
            //    这才是货架是满的那种观感，而且不挡路（东西全贴在内壁上）。
            //
            //    高层的房间浅，摆不下的 z 由 InRoom 判掉，不写死每层数量。
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0) ? NbFridge.XMin + NbFridge.WallT + 0.55f
                                      : NbFridge.XMax - NbFridge.WallT - 0.55f;
                for (int c = 0; c < SideRuns; c++)
                {
                    float z = NbFridge.ZMin + NbFridge.WallT + SideZ0 + c * SideStep;
                    if (!InRoom(n, z, 0.45f)) { skip++; continue; }
                    Cluster(root, tpl, keys, new Vector3(x, y, z), SidePer, SideGap,
                        n * 71 + side * 23 + c * 5, n, ref placed, ref miss, ref skip);
                }
            }

            // ③ 台阶两侧的平台：单件。上下楼梯时它们正好在视线高度，
            //    是"我在往上爬"这件事唯一的参照物 —— 少了它，六层灰板走起来一模一样。
            //
            //    x 不能写死：楼梯宽度和冰箱宽度都会变，写死就把商品摆到台阶上
            //    （或者推进侧壁里）。按"两侧各自的可用宽度"取两个比例位置。
            float wingL = NbFridge.StairX0 - NbFridge.Px(0f);
            float wingR = NbFridge.Px(1f) - NbFridge.StairX1;
            float[] lx =
            {
                NbFridge.Px(0f) + wingL * 0.40f, NbFridge.Px(0f) + wingL * 0.81f,
                NbFridge.StairX1 + wingR * 0.19f, NbFridge.StairX1 + wingR * 0.60f,
            };
            float fz = PlatformZ(n);
            for (int k = 0; k < lx.Length; k++)
                Slot(root, tpl, keys, new Vector3(lx[k], y, fz + (k % 2) * 0.45f),
                    n * 23 + k * 7 + 3, n * 13 + k, n, ref placed, ref miss, ref skip);
        }

        foreach (KeyValuePair<string, GameObject> kv in tpl)
            if (kv.Value != null) Nb.Kill(kv.Value);

        Debug.LogFormat("[FoodArt] 冰箱装饰完成：{0} 件（{1} 种模型 + 几何体兜底），贴在 {2} 层；" +
                        "碰撞体 {3} 个，取不到模型 {4} 次，给奶酪/浅房间让位 {5} 次，材质 {6} 份。",
            placed, tpl.Count, NbFridge.Layers + 1, _colliders, miss, skip, _mats.Count);
        return placed;
    }

    /// 一个"堆头"：把 count 件挤在 at 附近，肩并肩。
    static void Cluster(Transform root, Dictionary<string, GameObject> tpl, string[] keys,
                        Vector3 at, int count, float gap, int seed, int layer,
                        ref int placed, ref int miss, ref int skip)
    {
        for (int i = 0; i < count; i++)
        {
            float x = at.x + (i - (count - 1) * 0.5f) * gap;
            float z = at.z + Jit(seed + i * 11, 0.08f * SizeMul);
            Slot(root, tpl, keys, new Vector3(x, at.y, z),
                seed * 13 + i * 5, seed + i * 3, layer, ref placed, ref miss, ref skip);
        }
    }

    /// 一个坑位：先看会不会挡住本层的奶酪，再放。
    static void Slot(Transform root, Dictionary<string, GameObject> tpl, string[] keys,
                     Vector3 at, int pickSeed, int yawSeed, int layer,
                     ref int placed, ref int miss, ref int skip)
    {
        if (BlocksCheese(layer, at.x, at.z)) { skip++; return; }
        Put(root, tpl, keys, pickSeed, at, yawSeed, ref placed, ref miss);
    }

    /// 这个坑位离**本层的奶酪**太近吗。
    ///
    /// 奶酪是全场唯一该被一眼看见的东西。奶酪总高 0.34、商品 0.17~0.42（已含 SizeMul），
    /// 前面挡一瓶酒是真挡，不是"看着有点挤"。留出的距离**跟着 SizeMul 一起长** ——
    /// 商品放大之后，还按旧的 0.60 m 留身位，等于让一件更大的东西贴得更近。
    static bool BlocksCheese(int layer, float x, float z)
    {
        Vector3[] s = NbFridge.Spots;
        float top = NbFridge.Top(layer);
        float clear = 0.60f * SizeMul;
        for (int i = 0; i < s.Length; i++)
        {
            if (Mathf.Abs(s[i].y - top) > 0.30f) continue;      // 不是这一层的
            float dx = s[i].x - x, dz = s[i].z - z;
            if (dx * dx + dz * dz < clear * clear) return true;
        }
        return false;
    }

    /// z 是否落在第 layer 层的**房间里**（不含门前那条缩进带 —— 那是楼梯）。
    static bool InRoom(int layer, float z, float margin)
    {
        return z <= NbFridge.FrontZ(layer) - margin &&
               z >= NbFridge.ZMin + NbFridge.WallT + margin;
    }

    /// 台阶两侧平台所在的 z。n < Layers 时站在"上一层板往里缩"形成的那条平台上
    /// （头顶是空的）；顶层没有上一层，就退进房间里靠前沿一点。
    static float PlatformZ(int n)
    {
        if (n < NbFridge.Layers) return NbFridge.FrontZ(n + 1) + 0.55f;
        return NbFridge.FrontZ(n) - 0.85f;
    }

    /// 确定性抖动。不用 Random —— 每次进关的摆放必须一模一样，
    /// 否则调参时看到的画面每次都不同，改了什么根本说不清。
    static float Jit(int seed, float amp)
    {
        int h = seed * 1103515245 + 12345;
        h = (h >> 9) & 0x7fff;
        return (h / 32767f * 2f - 1f) * amp;
    }

    /// 在一个坑位上放一件：模型优先，拿不到就用几何体食物兜底。
    /// **兜底不是洁癖**：素材包没导入完（或者被移走）时，冰箱里至少还有东西，
    /// 而不是空货架 —— 空货架不会报错，只会让人以为"食物模型没生效"。
    static void Put(Transform root, Dictionary<string, GameObject> tpl, string[] keys,
                    int pickSeed, Vector3 at, int yawSeed, ref int placed, ref int miss)
    {
        Spec s;
        if (Pick(keys, pickSeed, out s) && Place(root, tpl, s, at, yawSeed))
        {
            placed++;
            return;
        }

        miss++;
        GameObject geo = NbFood.Spawn(root, at, yawSeed, 0.24f * SizeMul, "Food (geo)");
        AddCollider(geo);          // 兜底的几何体食物也一样要实心，否则只有它们能穿过去
        placed++;
    }

    // ───────────────────────── 模板 ─────────────────────────

    /// 把一个包实例化一次，把表里点到的节点摘出来做成"模板"。
    ///
    /// 为什么要摘出来：一个父节点只能挂一个位置，而同一个罐头要点 4 次。
    /// 摘出来之后每个位置再 Instantiate 一份模板（网格是共享的，不额外占内存）。
    ///
    /// `SetParent(holder, true)` 保留**世界**变换 —— 只清 localPosition。
    /// 不能直接 Reset 旋转：Blender 导出的 FBX 往往把 -90° X 的转正挂在父节点上，
    /// 连旋转一起清掉的话，整排瓶子会躺在地上（而且不报错）。
    static void Harvest(string path, Spec[] list, Dictionary<string, GameObject> tpl)
    {
        GameObject pack = Resources.Load<GameObject>(path);
        if (pack == null)
        {
            Debug.LogWarning("[FoodArt] 加载不到 Resources/" + path + " —— 这一包的零件全部跳过。");
            return;
        }

        GameObject tmp = Object.Instantiate(pack);
        tmp.name = path + " (harvest)";

        int got = 0;
        for (int i = 0; i < list.Length; i++)
        {
            Transform c = FindDeep(tmp.transform, list[i].item);
            if (c == null)
            {
                Debug.LogWarning("[FoodArt] " + path + " 里找不到「" + list[i].item + "」。");
                continue;
            }

            GameObject holder = new GameObject("tpl " + list[i].item);
            c.SetParent(holder.transform, true);
            c.localPosition = Vector3.zero;

            Paint(holder, list[i].tex);       // 材质先换掉，后面 Instantiate 就不用再换
            SetLayer(holder, Nb.DecorLayer);

            tpl[list[i].item] = holder;
            got++;
        }

        Nb.Kill(tmp);
        Debug.LogFormat("[FoodArt] {0}：41/10 件里取了 {1} 件。", path, got);
    }

    /// 面包那个 glb 只有**一个**网格（六个面包并成一体），没有可按名字挑的零件，
    /// 所以整份当一件用。它自己的材质是 glTFast 建的、贴图内嵌，不动它。
    static void HarvestPastry(Dictionary<string, GameObject> tpl)
    {
        GameObject pack = Resources.Load<GameObject>(PastryPack);
        if (pack == null)
        {
            Debug.LogWarning("[FoodArt] 加载不到 Resources/" + PastryPack + " —— 跳过面包。");
            return;
        }

        GameObject go = Object.Instantiate(pack);
        go.name = "tpl pastry";
        SetLayer(go, Nb.DecorLayer);
        tpl["pastry"] = go;
        Debug.Log("[FoodArt] 面包包已加载（单网格，六个面包作一件）。");
    }

    // ───────────────────────── 摆放 ─────────────────────────

    /// 按"货架索引"轮转挑一件。用固定的算术而不是 Random ——
    /// 每次进关的摆放必须一模一样，否则调参时看到的画面每次都不同。
    static bool Pick(string[] keys, int seed, out Spec spec)
    {
        spec = new Spec();
        if (keys.Length == 0) return false;

        int k = ((seed * 7 + 3) % keys.Length + keys.Length) % keys.Length;
        string key = keys[k];

        for (int i = 0; i < Grocery.Length; i++)
            if (Grocery[i].item == key) { spec = Grocery[i]; return true; }
        for (int i = 0; i < Vintage.Length; i++)
            if (Vintage[i].item == key) { spec = Vintage[i]; return true; }

        // 面包：统一按 0.20 高
        spec = new Spec(key, null, 0.20f);
        return true;
    }

    static bool Place(Transform root, Dictionary<string, GameObject> tpl, Spec s, Vector3 at, int seed)
    {
        GameObject src;
        if (!tpl.TryGetValue(s.item, out src) || src == null) return false;

        GameObject go = Object.Instantiate(src);
        go.name = "Food " + s.item;
        go.transform.SetParent(root, false);

        // 稍微转个角度：一排商品全朝一个方向会像贴图拼的；转一下就有"随手摆"的感觉
        float yaw = ((seed * 37 + 11) % 360);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // 这里**不能**复用 CatCheeseSetup.FitAndGround：它靠向下打射线找地面，
        // 而且取的是射线打到的**最低**那个面 ⇒ 在冰箱里会一路穿到最底层的地板，
        // 七层的装饰全掉到 y=0。这里的高度是调用方给的，不用量。
        //
        // 归一化到目标高度（再乘统一放大系数），最后把**包围盒底面**落到 at.y、
        // 中心对到 at.xz。缩放之后必须重新量一次包围盒 —— 模型的原点常常不在
        // 底面中心，拿缩放前的包围盒去对位会整体偏移，越大的模型偏得越多。
        Bounds b;
        if (CatCheeseSetup.TryMeasureWorldBounds(go, out b) && b.size.y > 1e-5f)
            go.transform.localScale *= s.h * SizeMul / b.size.y;

        if (CatCheeseSetup.TryMeasureWorldBounds(go, out b))
        {
            Vector3 p = go.transform.position;
            p.x += at.x - b.center.x;
            p.z += at.z - b.center.z;
            p.y += at.y - b.min.y;
            go.transform.position = p;
        }
        else
        {
            go.transform.position = at;
        }

        // **位置定完之后才挂碰撞体。** 反过来的话，包围盒是量的旧比例，
        // 碰撞体会跟模型错开一截（越大的模型偏得越多），而且看不出来。
        AddCollider(go);
        return true;
    }

    // ───────────────────────── 材质 ─────────────────────────

    static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

    static void Paint(GameObject go, string texPath)
    {
        Material m = MatFor(texPath);
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
            if (rs[i] != null) rs[i].sharedMaterial = m;
    }

    /// 同一个贴图共用一份材质 ⇒ URP 的 SRP Batcher 还能合批。
    /// 每件商品一份新材质的话，几十件就是几十个 draw call。
    static Material MatFor(string texPath)
    {
        string key = texPath == null ? "" : texPath;
        Material m;
        if (_mats.TryGetValue(key, out m) && m != null) return m;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        Texture2D tex = string.IsNullOrEmpty(texPath) ? null : Resources.Load<Texture2D>(texPath);
        if (!string.IsNullOrEmpty(texPath) && tex == null)
            Debug.LogWarning("[FoodArt] 找不到贴图 Resources/" + texPath + " —— 这件商品会是纯白。");

        m = new Material(sh);
        m.name = "FoodArt_" + (string.IsNullOrEmpty(texPath) ? "flat" : texPath);
        // URP/Lit 的主贴图槽是 _BaseMap、主色是 _BaseColor；mainTexture/color 这两个属性
        // 走的是 [MainTexture]/[MainColor] 特性，换 Standard 也照样能设对。
        m.mainTexture = tex;
        m.color = Color.white;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);

        _mats[key] = m;
        return m;
    }

    // ───────────────────────── 碰撞体 ─────────────────────────

    static int _colliders;

    /// 给一件商品挂上贴合它自身形状的 BoxCollider。
    ///
    /// 为什么是 BoxCollider 而不是 MeshCollider：一件商品一进物理宽相，网格碰撞体就是
    /// 一整个三角面集合；而这些模型是低模罐头/盒子/瓶子，盒子碰撞体在体感上没区别。
    /// 四百多个网格碰撞体的代价是每帧的宽相与中相开销，而它们一秒钟都不会动。
    ///
    /// 为什么不能直接用 `Renderer.bounds`：那是**世界**轴对齐盒。商品都带随机 yaw，
    /// 斜 45° 时世界 AABB 会比真实体积大出约 41% —— 人会隔着一拳的距离被挡在空气里。
    /// 这里把网格包围盒的 8 个角点换回**物体局部坐标**再求 AABB，
    /// 碰撞体于是挂在会转的那个节点上、跟着模型一起转，尺寸是量出来的不是猜的。
    static void AddCollider(GameObject go)
    {
        if (go == null) return;
        if (go.GetComponent<Collider>() != null) return;   // 模板自带碰撞体就别叠一个

        Transform root = go.transform;
        Matrix4x4 toLocal = root.worldToLocalMatrix;      // 含缩放 ⇒ 得到的是"未缩放局部"单位
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;

        MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < mfs.Length; i++)
        {
            Mesh mesh = mfs[i].sharedMesh;
            if (mesh == null) continue;

            Bounds mb = mesh.bounds;
            Matrix4x4 m = toLocal * mfs[i].transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? mb.min.x : mb.max.x,
                    (c & 2) == 0 ? mb.min.y : mb.max.y,
                    (c & 4) == 0 ? mb.min.z : mb.max.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }

        if (!any) return;

        // 太薄的轴给一个下限：贴图级别的薄片在球扫掠下会被穿过去。
        Vector3 size = b.size;
        size.x = Mathf.Max(size.x, 0.02f);
        size.y = Mathf.Max(size.y, 0.02f);
        size.z = Mathf.Max(size.z, 0.02f);

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = b.center;
        bc.size = size;
        bc.isTrigger = false;
        _colliders++;
    }

    // ───────────────────────── 小工具 ─────────────────────────

    static Transform FindDeep(Transform t, string want)
    {
        if (t == null) return null;
        string w = Norm(want);
        if (w.Length > 0 && Norm(t.name).StartsWith(w)) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform r = FindDeep(t.GetChild(i), want);
            if (r != null) return r;
        }
        return null;
    }

    /// 去掉下划线/空格/点、转小写：FBX 里叫 "rusted can"，贴图叫 "rusted_can.png"，
    /// 直接比字符串永远对不上。
    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '_' || c == ' ' || c == '.') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    static void SetLayer(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayer(go.transform.GetChild(i).gameObject, layer);
    }
}
