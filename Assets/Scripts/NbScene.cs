// @role Scene shell: selects the active scene (Street/Yard/Kitchen/Fridge), holds its bounds/spawn/targets/routes, hides the street original.
// @calls NbYard NbKitchen NbFridge NbBuilder

// NbScene.cs — 三个场景的公共外壳
//
// 为什么需要这一层：
//   原来 Nb 这个静态类把街区的几何（XMin/XMax、ColX/RowZ、CheeseSpots、CatRoutes）全写死了，
//   NbGame 直接读它。再加两个场景就没地方放数据 —— 于是把"当前场景"抽成一组静态字段，
//   由 NbScene.Select 在开机时写一次，NbGame / NbMap / HUD 只认这组字段，不认具体是哪个场景。
//
// 三个场景共用同一套玩法骨架（收齐 N 个目标、被 N 个威胁抓、10 关递进），但压力来源不同：
//   Street   猫巡逻 + 视野锥          —— 走廊本身有风险
//   Yard     保安 + 手电锥（会追人）  —— 掩体之间是空地，被照到就得跑
//   Kitchen  食物腐烂 + 灯管 + 黑猫   —— 走道窄到躲不开灯
//
// 还有一件容易被忽略的事：**原始 SampleScene 里那 12 栋楼、地面、围墙是街区专用的。**
// 后院和厨房必须把它们整体关掉，否则会在别人的场地上凭空出现一片居民区。
// 这件事由 HideOriginal 负责，切回街区时再打开 —— 只关不恢复会让第二次进街区变成空地。

using UnityEngine;
using UnityEngine.SceneManagement;

// Kitchen 保留在枚举里、但已经不挂在选择页上了：第三格换成了冰箱（Fridge = 3）。
// 之所以不删这个值 —— NbKitchen.cs 还在，NbLevels 里那张表也还在，
// 想换回去只要把选择页的 Order 改一格，别的一行都不用动。
internal enum NbSceneKind { Street = 0, Yard = 1, Kitchen = 2, Fridge = 3 }

internal static class NbScene
{
    internal static NbSceneKind Kind = NbSceneKind.Street;

    // ───────────────────────── 当前场景 ─────────────────────────

    internal static string Name  = "RESIDENTIAL BLOCK";
    internal static string Code  = "BLOCK";
    internal static string Blurb = "Twelve houses, five alleys, no light.";

    internal static float XMin = Nb.XMin;
    internal static float XMax = Nb.XMax;
    internal static float ZMin = Nb.ZMin;
    internal static float ZMax = Nb.ZMax;

    internal static Vector3    Start   = Nb.PlayerStart;
    internal static Vector3    Forward = Nb.PlayerForward;
    internal static Vector3[]  Targets = Nb.CheeseSpots;
    internal static Vector3[][] Routes  = Nb.CatRoutes;

    internal static int MaxTargets = 10;
    internal static int MaxRoutes  = 10;

    // 机制开关：NbGame 只问"这个场景要不要跑这套逻辑"，不问"你是哪个场景"。
    internal static bool UsesGuards { get { return Kind == NbSceneKind.Yard; } }
    internal static bool UsesRot    { get { return Kind == NbSceneKind.Kitchen; } }
    internal static bool UsesLamps  { get { return Kind == NbSceneKind.Kitchen; } }

    /// 冰箱：威胁是"人开门 + 3 秒预警"。开着这一项时 NbGame 会跑 DoorUpdate，
    /// 并且**跳过贴地**（目标点各自带着层高 y，贴地会把六层全压回 0）。
    internal static bool UsesDoor   { get { return Kind == NbSceneKind.Fridge; } }

    /// 目标点是否自带高度。只有冰箱是 true —— 别的场景全场 y=0，贴地只是保险。
    internal static bool KeepsHeight { get { return Kind == NbSceneKind.Fridge; } }

    internal static float Width   { get { return XMax - XMin; } }
    internal static float Depth   { get { return ZMax - ZMin; } }
    internal static float CenterX { get { return (XMin + XMax) * 0.5f; } }
    internal static float CenterZ { get { return (ZMin + ZMax) * 0.5f; } }

    internal static bool Inside(Vector3 p, float margin)
    {
        return p.x > XMin + margin && p.x < XMax - margin &&
               p.z > ZMin + margin && p.z < ZMax - margin;
    }

    // ───────────────────────── 切换 ─────────────────────────

    internal static void Select(NbSceneKind k)
    {
        Kind = k;
        switch (k)
        {
            case NbSceneKind.Yard:    NbYard.Setup();    break;
            case NbSceneKind.Kitchen: NbKitchen.Setup(); break;
            case NbSceneKind.Fridge:  NbFridge.Setup();  break;
            default:                  SetupStreet();     break;
        }
        HideOriginal(k != NbSceneKind.Street);
    }

    static void SetupStreet()
    {
        Name  = "RESIDENTIAL BLOCK";
        Code  = "BLOCK";
        Blurb = "Twelve houses, five alleys, no light.";

        XMin = Nb.XMin; XMax = Nb.XMax;
        ZMin = Nb.ZMin; ZMax = Nb.ZMax;

        Start   = Nb.PlayerStart;
        Forward = Nb.PlayerForward;
        Targets = Nb.CheeseSpots;
        Routes  = Nb.CatRoutes;
        MaxTargets = Targets.Length;
        MaxRoutes  = Routes.Length;
    }

    // ───────────────────────── 搭场景 ─────────────────────────

    internal static void Build(Transform root)
    {
        switch (Kind)
        {
            case NbSceneKind.Yard:    NbYard.Build(root);    break;
            case NbSceneKind.Kitchen: NbKitchen.Build(root); break;
            case NbSceneKind.Fridge:  NbFridge.Build(root);  break;
            default:                  NbBuilder.Build(root); break;
        }
    }

    /// 原始场景里属于「居民街区」的根物体。换场景时整体关掉，切回来再打开。
    /// 'Plane' 也在里面 —— 它是街区那块 200×200 的大地面；后院/厨房有各自的地板，
    /// 留着它会把新场景的地面整个盖住（而且是亮灰色，一眼就穿帮）。
    static readonly string[] OriginalRoots =
    {
        "Street Models (auto)", "Street", "Plane",
        "===Residential Area===", "===Interior Hallway===", "CatMoveSpace",
    };

    internal static void HideOriginal(bool hide)
    {
        Scene s = SceneManager.GetActiveScene();
        if (!s.IsValid()) return;

        GameObject[] roots = s.GetRootGameObjects();
        int n = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject go = roots[i];
            if (go == null) continue;
            for (int j = 0; j < OriginalRoots.Length; j++)
            {
                if (go.name != OriginalRoots[j]) continue;
                if (go.activeSelf != !hide) n++;
                go.SetActive(!hide);
                break;
            }
        }
        Debug.LogFormat("[Nb] 原始街区物体 {0} 个：{1}。", n, hide ? "已关闭" : "已恢复");
    }
}

/// 场景进度。**自由选择**模式：三个场景都能进，各自记"最高打到第几关"。
/// 用 PlayerPrefs 而不是文件：这个工程没有存档系统，PlayerPrefs 在 Editor 里跨 Play 也能留住，
/// 而且不会因为路径/权限问题静默失败（写到一半失败比不存更糟）。
internal static class NbProgress
{
    const string Key = "nb.scene.best.";

    /// 返回该场景可以进入的最高关卡（1 起）。默认 1。
    internal static int Best(NbSceneKind k)
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(Key + (int)k, 1), 1, NbLevels.Count);
    }

    internal static void Reach(NbSceneKind k, int level)
    {
        if (level <= Best(k)) return;
        PlayerPrefs.SetInt(Key + (int)k, Mathf.Clamp(level, 1, NbLevels.Count));
        PlayerPrefs.Save();
    }

    internal static void ClearAll()
    {
        for (int i = 0; i < 4; i++) PlayerPrefs.DeleteKey(Key + i);
        PlayerPrefs.Save();
    }
}
