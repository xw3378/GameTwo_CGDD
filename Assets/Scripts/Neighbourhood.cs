// Neighbourhood.cs — 「居民区」关卡的空间定义 + 搭场景用的几何小工具
//
// 布局不是拍脑袋定的：**先把 12 个楼栋的 BoxCollider 世界包络量出来，再从包络反推过道。**
// 这一版的数据来自换过模型之后的实测值 —— 真实模型是 16.9~17.4 宽、7.7~14.7 深，
// 而且整排在 z 上比白盒偏了 9.5 m。旧版按白盒的 16×14 网格算，于是过道铺装、奶酪、
// 猫全压在楼体里（猫甚至出生在楼里），这关一度根本走不通。
//
//   楼群本体    x 50.22…114.75   z −75.08…−0.88
//   纵向通道 A  x 68.80…73.90（中轴 71.35）
//   纵向通道 B  x 91.00…96.40（中轴 93.70）
//   横向通道 1  z −22.90…−15.30（中轴 −19.10）
//   横向通道 2  z −42.40…−36.10（中轴 −39.25）
//   横向通道 3  z −60.40…−55.70（中轴 −58.05）
//   围墙再往外推 3.6 m ⇒ 一圈环形通道（中轴 x 48.41 / 116.58，z 0.91 / −76.89）
//
// 奶酪、猫的路线、小地图上的方块全部读这组常量，所以三处永远对得上。
// 所有尺寸单位都是米，地面 y = 0。

using System.Collections.Generic;
using UnityEngine;

internal static class Nb
{
    // ───────────────────────── 范围 ─────────────────────────

    internal const float XMin = 46.6f;
    internal const float XMax = 118.4f;
    internal const float ZMin = -78.7f;
    internal const float ZMax = 2.7f;

    internal const float Width = XMax - XMin;     // 71.8
    internal const float Depth = ZMax - ZMin;     // 81.4

    // 楼群本体的实测包络：环形通道就夹在它和围墙之间
    internal const float BlockX0 = 50.22f;
    internal const float BlockX1 = 114.75f;
    internal const float BlockZ0 = -75.08f;
    internal const float BlockZ1 = -0.88f;

    // ───────────────────────── 楼栋网格 ─────────────────────────
    // 3 列 × 4 排。每栋尺寸不一样（换过模型之后就不再是统一的 16×14），
    // 所以宽度/进深按列、按排各存一个数，小地图用它画方块才不会画歪。

    internal const int Cols = 3;
    internal const int Rows = 4;
    internal const int Slots = Cols * Rows;                        // 12

    internal static readonly float[] ColX = { 59.47f, 82.38f, 105.62f };
    internal static readonly float[] RowZ = { -8.68f, -29.50f, -49.00f, -68.50f };
    internal static readonly float[] ColW = { 18.50f, 16.92f, 18.26f };
    internal static readonly float[] RowD = { 14.38f, 13.16f, 13.16f, 14.67f };

    internal static Vector3 SlotCenter(int i)
    {
        return new Vector3(ColX[i % Cols], 0f, RowZ[i / Cols]);
    }

    // ───────────────────────── 过道 ─────────────────────────

    internal const float VAx0 = 68.80f, VAx1 = 73.90f, VAcx = 71.35f;   // 纵向 A
    internal const float VBx0 = 91.00f, VBx1 = 96.40f, VBcx = 93.70f;   // 纵向 B

    internal const float H1z0 = -22.90f, H1z1 = -15.30f, H1cz = -19.10f;
    internal const float H2z0 = -42.40f, H2z1 = -36.10f, H2cz = -39.25f;
    internal const float H3z0 = -60.40f, H3z1 = -55.70f, H3cz = -58.05f;

    // 环形通道的中轴
    internal const float RingWx = 48.41f, RingEx = 116.58f;
    internal const float RingNz = 0.91f, RingSz = -76.89f;

    // ───────────────────────── 门与出生点 ─────────────────────────

    internal const float GateX0 = 68.50f;   // 前墙的门洞，正好对上纵向通道 A
    internal const float GateX1 = 74.20f;

    // 整片居民区的几何中心（围墙包围盒的中点）—— 出生点就钉在这里
    internal const float CenterX = (XMin + XMax) * 0.5f;   // 82.50
    internal const float CenterZ = (ZMin + ZMax) * 0.5f;   // -38.00

    // 起点在居民区**正中间**，落在横向通道 2 的路面上（z −42.40…−36.10 之间）。
    // 左右两侧各约 11 m 就是纵向通道 A / B 的十字口，所以一睁眼就站在整个街区的十字轴上；
    // 朝东（+x）是因为东边 13 m 有盏亮着的路灯 + 两个花坛，画面里有参照物，
    // 比对着空过道更容易看出"我在动"。
    // y 取 0.10：H2 铺装顶面是 0.048，留 5 cm 让人自己落下去，别一开始就嵌在砖里。
    internal static readonly Vector3 PlayerStart = new Vector3(CenterX, 0.10f, CenterZ);
    internal static readonly Vector3 PlayerForward = new Vector3(1f, 0f, 0f);

    // ───────────────────────── 10 个奶酪点（全在过道上）─────────────────────────
    //
    // **数组顺序就是出场顺序**：第 n 关取前 (n+1) 个点，所以这里必须按「离出生点由近到远」排。
    // 顺序反过来会让第 1 关的两块奶酪落在街区最南端（40 m 外、还隔着两只猫），
    // 而第 9 关反而在家门口 —— 难度曲线会当场倒过来。
    // 距离基准是出生点 (82.50, -38.00)，平面直线距离写在每行末尾。

    internal static readonly Vector3[] CheeseSpots =
    {
        new Vector3(VBcx, 0f, -33.0f),   // 东巷，出生点正东 11 m            12.3 m
        new Vector3(VAcx, 0f, -45.5f),   // 西巷，斜后方                    13.4 m
        new Vector3(VAcx, 0f, -25.0f),   // 西巷，斜前方                    17.1 m
        new Vector3(82.40f, 0f, H3cz),   // 横向通道 3 正中（往南一条街）     20.1 m
        new Vector3(VBcx, 0f, -63.0f),   // 东巷最南端                      28.3 m
        new Vector3(VAcx, 0f, -66.0f),   // 西巷最南端                      30.1 m
        new Vector3(VBcx, 0f,  -8.0f),   // 东巷最北端（大门那头）           32.1 m
        new Vector3(55.50f, 0f, H1cz),   // 横向通道 1 西端                 32.9 m
        new Vector3(110.0f, 0f, H1cz),   // 横向通道 1 东端                 33.4 m
        new Vector3(VAcx, 0f,  -5.0f),   // 西巷最北端，贴着大门            34.5 m
    };

    // ───────────────────────── 10 条猫的巡逻路线 ─────────────────────────
    //
    // **路线是"按段切开"的，不是"一条通道一整只猫"。** 两条纵向通道各切成 3 段
    // （北 / 中 / 南），三条横向通道各 1 条，外加环形通道 1 条 ⇒ 正好 10 段。
    // 这样第 n 关放 n 只猫时，猫是从"最不碍事"的段往"最碍事"的段铺开的：
    //   · 第 1 关只有西巷中段一只 —— 你先在安全区里看清它是怎么来回走的
    //   · 第 5 关前五段铺满，五条通道各一只 —— 第一次出现"绕不开"
    //   · 第 10 关连 H2（你出生那条）都有猫，全场没有一条通道是空的
    // 旧版是 5 条整通道 + 环形，猫一多就只能两只叠在同一条路线上一起走 —— 看着像复制粘贴。
    //
    // 每段都是两点直线来回（StepRoute 到端点掉头），不需要额外寻路。

    internal static readonly Vector3[][] CatRoutes =
    {
        new[] { new Vector3(VAcx, 0f, -26.0f), new Vector3(VAcx, 0f, -50.0f) },   // 0 西巷·中段
        new[] { new Vector3(VBcx, 0f, -26.0f), new Vector3(VBcx, 0f, -50.0f) },   // 1 东巷·中段
        new[] { new Vector3(VAcx, 0f,  -3.0f), new Vector3(VAcx, 0f, -26.0f) },   // 2 西巷·北段（靠大门）
        new[] { new Vector3(VBcx, 0f,  -3.0f), new Vector3(VBcx, 0f, -26.0f) },   // 3 东巷·北段
        new[] { new Vector3(52.00f, 0f, H1cz), new Vector3(113.0f, 0f, H1cz) },   // 4 横 1
        new[] { new Vector3(52.00f, 0f, H3cz), new Vector3(113.0f, 0f, H3cz) },   // 5 横 3
        new[] { new Vector3(VAcx, 0f, -50.0f), new Vector3(VAcx, 0f, -74.0f) },   // 6 西巷·南段
        new[] { new Vector3(VBcx, 0f, -50.0f), new Vector3(VBcx, 0f, -74.0f) },   // 7 东巷·南段
        new[]                                                                    // 8 环形通道
        {
            new Vector3(RingWx, 0f, RingNz), new Vector3(RingWx, 0f, RingSz),
            new Vector3(RingEx, 0f, RingSz), new Vector3(RingEx, 0f, RingNz),
        },
        new[] { new Vector3(52.00f, 0f, H2cz), new Vector3(113.0f, 0f, H2cz) },   // 9 横 2（穿过出生点）
    };

    // 场地容量 = 上面两张表的长度：奶酪点 10 个、路线 10 条。
    // 第 9、10 关会把奶酪点全用上，第 10 关会把路线全用上 —— 再加关卡就得先加这里的点。

    // ───────────────────────── 判断 ─────────────────────────

    internal static float FlatDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    internal static Vector3 Grounded(Vector3 p)
    {
        float y;
        if (CatCheeseSetup.TryGroundY(p, out y)) return new Vector3(p.x, y, p.z);
        return new Vector3(p.x, 0f, p.z);
    }

    // ───────────────────────── 几何工具 ─────────────────────────

    // 装饰物统一放 Ignore Raycast：只对射线隐形（地面探测/猫的视线不能被打扰），
    // 物理碰撞照常 —— 不过装饰物连碰撞体都直接删掉，免得把过道堵死。
    static int _decorLayer = -2;
    internal static int DecorLayer
    {
        get
        {
            if (_decorLayer == -2) _decorLayer = LayerMask.NameToLayer("Ignore Raycast");
            return _decorLayer;
        }
    }

    static readonly Dictionary<Color, Material> Mats = new Dictionary<Color, Material>();

    internal static Material Mat(Color c)
    {
        Material m;
        if (Mats.TryGetValue(c, out m) && m != null) return m;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh);
        m.name = "Nb_" + ColorUtility.ToHtmlStringRGB(c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        // FBX/URP 的材质球默认带贴图槽，这里没有贴图，把 _BaseMap 留空即可
        Mats[c] = m;
        return m;
    }

    /// 自发光材质（路灯玻璃用）：URP/Lit 得同时写 _EmissionColor 和打开 _EMISSION 关键字，
    /// 只设颜色不启关键字的话编辑器里看不出问题、屏幕上一片死黑。
    /// 不走 Mat 的缓存 —— 那个按颜色共享，改一份会把所有同色的东西一起点亮。
    internal static Material MatEmissive(Color c, float strength)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        Material m = new Material(sh);
        m.name = "Nb_Emissive_" + ColorUtility.ToHtmlStringRGB(c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    /// 方块。solid = true 时保留碰撞体并留在 Default 层（用来挡人、挡视线）。
    internal static GameObject Box(Transform parent, string name, Vector3 local, Vector3 size, Color color, bool solid = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        Style(go, color, solid);
        return go;
    }

    /// 圆柱（Unity 原始圆柱高 2、直径 1，所以 y 要除 2）。
    internal static GameObject Cyl(Transform parent, string name, Vector3 local, float diameter, float height, Color color, bool solid = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        Style(go, color, solid);
        return go;
    }

    /// 球（Unity 原始球直径 1）。
    internal static GameObject Ball(Transform parent, string name, Vector3 local, float diameter, Color color, bool solid = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = Vector3.one * diameter;
        Style(go, color, solid);
        return go;
    }

    static void Style(GameObject go, Color color, bool solid)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = Mat(color);

        if (solid)
        {
            go.layer = 0;
            return;
        }

        Collider c = go.GetComponent<Collider>();
        if (c != null) Kill(c);
        int layer = DecorLayer;
        if (layer >= 0) go.layer = layer;
    }

    /// 删物体/组件：编辑模式下 `Object.Destroy` 是**不生效**的（Unity 会往 Console 刷
    /// 「Destroy may not be called from edit mode!」而物体还在），所以烘焙场景时走 DestroyImmediate。
    /// 装饰物"删掉碰撞体"全靠这一句，走错分支就会给 160 个树/灯/花坛全留下碰撞体 —— 过道会被堵死。
    internal static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
