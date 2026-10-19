// StreetFromModels.cs —— 把场景里的白盒楼（Residental building / House）换成真实建筑模型
//
// 做法（编辑器侧，一次性）：
//   1. 找到每个白盒楼父物体，量出它子 Cube 的联合世界包围盒（再单独量一次「去掉地台平板」的本体盒）
//   2. 关掉白盒 Cube（可 Undo）
//   3. 按本体占地等比缩放模型、居中、贴地、随机朝向 0/90/180/270
//   4. 把 FBX 带入的 Standard 材质统一换成 URP/Lit（否则整条街是洋红色）
//
// 三个模型的实测尺寸（离线解 FBX 得到，不是猜的）：
//   21 West Main    3.26 x 6.76 占地 / 4.21 高 / 25844 面（含一块 10.48 m 见方的地台，量本体时排除）
//   abandoned       约 10 x 6 占地（原始顶点只有 0.1 单位，靠 Lcl Scaling 100 才到米级）
//   hara 工业仓库    19.25 x 6.81 占地 / 14.90 高 / 275 万面 —— 最多用 2 份
//
// 守卫：**不再自动建街**（会自动建街的版本会让「回滚」在下次打开编辑器时失效）。
//       建街用菜单 Tools/Street/Build From Models；回滚用 Tools/Street/Restore Whitebox。
//       自动执行只认一个标记文件 Assets/Editor/neighbourhood_once.flag（建完即删）；没有标记就什么都不做。

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StreetFromModels
{
    const string RootName = "Street Models (auto)";
    const int Seed = 20261017;          // 固定种子：同一份场景每次排出同样的街
    const int MaxIndustrial = 2;        // hara 275 万面，多了会卡

    static readonly string[] Paths =
    {
        "Assets/21-west-main-street/source/21 West Main Export.fbx",
        "Assets/abadoned-building-lowpoly/source/model.fbx",
        "Assets/old-industrial-storage-building/source/hara.fbx",
    };
    static readonly string[] Tags = { "west", "abandoned", "industrial" };

    // 0=主街店铺 1=废弃楼 2=工业仓库；12 栋住宅楼，工业只出现 2 次（275 万面）
    static readonly int[] Pick = { 0, 1, 0, 2, 1, 0, 1, 0, 2, 1, 0, 1 };

    static readonly Dictionary<Material, Material> s_fixed = new Dictionary<Material, Material>();

    // ───────────────────────── 一次性执行 ─────────────────────────
    //
    // 这里**不做自动建街、也不做自动回滚**。前两版都在这个位置踩过同一个坑：
    // 用 SessionState 做"只跑一次"的守卫，而 SessionState 每次重开编辑器都会清零，
    // 于是"我建好了"或"我回滚了"会在下次打开工程时被悄悄推翻。
    //
    // 现在只有一个自动入口：工程里存在 Assets/Editor/neighbourhood_once.flag 时，编译后建一次街，
    // 然后立刻删掉标记。没有标记就什么都不做 —— 这是"有状态的一次性"，不是"每个会话一次"。

    /// 标记文件放在**工程根目录**（不在 Assets 下）：放 Assets 里 Unity 每次都会把它当资源导入，
    /// 多出一堆 .meta 和导入日志。用 Application.dataPath 拼绝对路径，不依赖编辑器的工作目录。
    static string OnceFlagPath
    {
        get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../neighbourhood_once.flag")); }
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.delayCall += BuildOnceIfFlagged;
    }

    static void BuildOnceIfFlagged()
    {
        string flag = OnceFlagPath;
        if (!System.IO.File.Exists(flag)) return;
        if (Application.isPlaying)
        {
            Debug.LogWarning("[Street] 有一次性建街标记，但编辑器正在 Play 模式 —— 退出 Play 后会自动建。");
            return;
        }
        System.IO.File.Delete(flag);              // 先删标记：哪怕 Build 中途抛异常也只跑这一次
        Debug.Log("[Street] 检测到一次性标记，开始建街。");
        Build();
    }

    [MenuItem("Tools/Street/Build From Models (rebuild)")]
    public static void BuildMenu()
    {
        Build();
    }

    [MenuItem("Tools/Street/Restore Whitebox")]
    public static void RestoreMenu()
    {
        Restore();
    }

    /// 只修 12 个楼体碰撞体的 center/size，**不重建街**（不换模型、不动朝向/缩放）。
    /// 运行时 NbColliders.Normalize() 做的是同一件事，但那只在 Play 里有意义；
    /// 这个菜单改的是场景文件本身，Scene 视图里的绿框、以及以后任何一次保存都会是对的。
    [MenuItem("Tools/Street/Fix Collider Sizes (keep models)")]
    public static void FixCollidersMenu()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning("[Street] 场景里没有「" + RootName + "」，没什么可修的。");
            return;
        }

        int fixedN = 0;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform holder = root.transform.GetChild(i);
            Bounds local;
            if (!LocalBounds(holder, holder.gameObject, out local)) continue;

            BoxCollider bc = holder.GetComponent<BoxCollider>();
            if (bc == null) bc = Undo.AddComponent<BoxCollider>(holder.gameObject);
            Undo.RecordObject(bc, "Fix collider size");

            Vector3 old = bc.size;
            bc.isTrigger = false;
            bc.center = local.center;
            bc.size = local.size;

            Bounds w = WorldBoundsOf(holder, bc);
            Debug.LogFormat("[Street] {0}：碰撞体 {1:F1}x{2:F1}x{3:F1} → 世界 {4:F1}x{5:F1}x{6:F1}（原来 size 是 {7:F1}x{8:F1}x{9:F1}）",
                holder.name, bc.size.x, bc.size.y, bc.size.z, w.size.x, w.size.y, w.size.z, old.x, old.y, old.z);
            fixedN++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.LogFormat("[Street] 已修 {0} 个楼体碰撞体。场景尚未保存 —— Cmd+S 才会写进 .unity。", fixedN);
    }

    // ───────────────────────── 回滚 ─────────────────────────

    /// 把场景恢复到「换模型之前」：删掉模型根物体，把被关掉的白盒楼重新启用。
    /// 只操作本脚本自己造出来的东西，不碰任何原有几何。
    static void Restore()
    {
        GameObject root = GameObject.Find(RootName);
        int models = 0;
        if (root != null)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            models = Mathf.Max(0, all.Length - 1);      // 去掉根物体自己
            Undo.DestroyObjectImmediate(root);
        }

        int reactivated = 0;
        Transform[] every = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform t in every)
        {
            if (!IsWhiteboxName(t.name)) continue;
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
            {
                if (c.gameObject.activeSelf) continue;
                Undo.RecordObject(c.gameObject, "Restore whitebox");
                c.gameObject.SetActive(true);
                reactivated++;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.LogFormat("[Street] 已回滚 —— 删掉 {0} 个模型物体，重新启用 {1} 个白盒物体" +
                        "（12 栋住宅楼 × 3 个 Cube）。要再换一次走 Tools/Street/Build From Models。",
            models, reactivated);
    }

    static bool IsWhiteboxName(string n)
    {
        return n == "Residental building" || n.StartsWith("Residental building (")
            || n == "House" || n.StartsWith("House (");
    }

    // ───────────────────────── 主流程 ─────────────────────────

    public static void Build()
    {
        var models = LoadModels();
        if (models == null) return;

        List<Transform> targets = CollectTargets();
        if (targets.Count == 0)
        {
            Debug.LogWarning("[Street] 场景里没找到白盒楼（名字以 Residental building / House 开头）。");
            return;
        }

        // ⚠️ 顺序很关键：**先量完所有白盒的尺寸，再删旧模型**。
        // 上一次建街会把白盒 SetActive(false)，而 RenderBounds 用 activeInHierarchy 过滤 Renderer，
        // 直接量只会得到空包围盒。实测过一次的后果是：旧模型被 DestroyImmediate 删掉、
        // 新模型一个都没建 —— 场景里一栋楼都不剩。所以这里先临时恢复、量完、再动手。
        var fulls = new List<Bounds>();
        var bodies = new List<Bounds>();
        var keep = new List<Transform>();

        for (int i = 0; i < targets.Count; i++)
        {
            Transform t = targets[i];
            RestoreForMeasure(t);

            Bounds full = RenderBounds(t.gameObject);
            Bounds body = RenderBounds(t.gameObject, true);
            if (body.size.x < 0.5f || body.size.z < 0.5f || full.size.x < 0.5f)
            {
                Debug.LogWarning("[Street] 跳过 " + t.name + "：量不到有效尺寸。");
                continue;
            }
            fulls.Add(full);
            bodies.Add(body);
            keep.Add(t);
        }

        if (keep.Count == 0)
        {
            Debug.LogError("[Street] 一栋白盒都量不到，本次不建街（旧模型保持原样，场景没被破坏）。");
            return;
        }
        for (int i = 0; i < keep.Count; i++) HideWhiteBox(keep[i]);

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build street models");

        Random.InitState(Seed);
        s_fixed.Clear();

        int industrialUsed = 0;
        int built = 0;

        for (int i = 0; i < keep.Count; i++)
        {
            Transform t = keep[i];
            Bounds full = fulls[i];
            Bounds body = bodies[i];

            int which = Pick[i % Pick.Length];
            if (which == 2 && industrialUsed >= MaxIndustrial) which = 0;

            GameObject prefab = models[which];
            if (prefab == null)
            {
                Debug.LogError("[Street] 模型 " + Tags[which] + " 没有导入成功，跳过 " + t.name);
                continue;
            }
            if (which == 2) industrialUsed++;

            var holder = new GameObject(string.Format("{0}_{1:00}", Tags[which], i));
            holder.transform.SetParent(root.transform, true);
            holder.transform.position = new Vector3(body.center.x, body.min.y, body.center.z);
            holder.transform.localScale = Vector3.one;

            var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (inst == null)
            {
                Debug.LogError("[Street] 实例化失败：" + prefab.name);
                continue;
            }
            Undo.RegisterCreatedObjectUndo(inst, "Build street models");
            inst.transform.SetParent(holder.transform, false);
            inst.transform.localPosition = Vector3.zero;

            HideSlabs(inst);

            // 朝向：四个方向各试一次，取「塞得最满」的那个（模型长边对齐楼栋长边）。
            // 若只按随机 90° 转，19 m 进深的工业仓库会被 14 m 的地块逼成 4.7 m 宽的窄条。
            // 分数相同的方向通常有 2 个（正面/背面），在它们之间随机，保留「随机排列」的变化。
            float[] fit = new float[4];
            float bestFit = -1f;
            for (int a = 0; a < 4; a++)
            {
                holder.transform.rotation = Quaternion.Euler(0f, a * 90f, 0f);
                Bounds bb = RenderBounds(inst, true);
                fit[a] = Mathf.Min(body.size.x / Mathf.Max(0.001f, bb.size.x),
                                   body.size.z / Mathf.Max(0.001f, bb.size.z));
                if (fit[a] > bestFit) bestFit = fit[a];
            }
            var cand = new List<int>();
            for (int a = 0; a < 4; a++) if (fit[a] > bestFit * 0.98f) cand.Add(a);
            int pickYaw = cand[Random.Range(0, cand.Count)];
            holder.transform.rotation = Quaternion.Euler(0f, pickYaw * 90f, 0f);

            float k = fit[pickYaw] * 0.94f;               // 留 6% 缝，免得两栋贴在一起 z-fighting
            holder.transform.localScale = Vector3.one * k;

            // 居中 + 贴地（用世界坐标补差，避免受 holder 缩放影响）
            Bounds s1 = RenderBounds(inst, true);
            holder.transform.position += new Vector3(
                body.center.x - s1.center.x,
                body.min.y - s1.min.y,
                body.center.z - s1.center.z);

            FixMaterials(inst);
            AddBodyCollider(holder, inst, k);

            string meshInfo = CountRenderers(inst);
            Debug.LogFormat("[Street] {0} <- {1}  白盒 {2:F1}x{3:F1}x{4:F1}  倍率 {5:F2}  模型高 {6:F1} m  {7}",
                t.name, Tags[which], full.size.x, full.size.y, full.size.z, k,
                RenderBounds(inst, true).size.y, meshInfo);
            built++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.LogFormat("[Street] 完成：{0} 栋楼换成了真实模型（工业仓库用了 {1} 份）。" +
                        "场景尚未保存 —— 满意后按 Cmd+S；不满意按 Cmd+Z 或重新打开场景。", built, industrialUsed);
    }

    // ───────────────────────── 收集 / 隐藏 ─────────────────────────

    static List<Transform> CollectTargets()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var list = new List<Transform>();
        foreach (Transform t in all)
        {
            string n = t.name;
            bool isRes = n == "Residental building" || n.StartsWith("Residental building (");
            if (!isRes) continue;
            // 只换 12 栋住宅楼。4 个 House 是 10x4x3 的小棚屋，塞进这些模型会缩成 1.7 m 高的玩具，
            // 所以保持白盒 —— 要一起换就把下面这行注释掉，并把 CollectTargets 放回 isHouse 分支。
            if (t.GetComponentInChildren<Renderer>(true) == null) continue;
            list.Add(t);
        }
        // 按 x 再 z 排序，保证每次排出来的顺序一致（配合固定种子 => 可复现）
        list.Sort((a, b) =>
        {
            int c = a.position.x.CompareTo(b.position.x);
            return c != 0 ? c : a.position.z.CompareTo(b.position.z);
        });
        return list;
    }

    /// 临时把白盒恢复成可见 —— 只为了能量到尺寸（Renderer 会把 inactive 的物体过滤掉）。
    /// 量完由调用方统一 HideWhiteBox 关回去。
    static void RestoreForMeasure(Transform t)
    {
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
        {
            if (c.gameObject.activeSelf) continue;
            c.gameObject.SetActive(true);
        }
    }

    static void HideWhiteBox(Transform t)
    {
        Renderer self = t.GetComponent<Renderer>();
        if (self != null)
        {
            Undo.RecordObject(self.gameObject, "Hide whitebox");
            self.gameObject.SetActive(false);
        }
        foreach (Transform child in t)
        {
            if (child.GetComponent<Renderer>() == null) continue;
            Undo.RecordObject(child.gameObject, "Hide whitebox");
            child.gameObject.SetActive(false);
        }
    }

    // ───────────────────────── 尺寸 ─────────────────────────

    /// skipFlat = true 时排除「地台平板」：几乎没厚度、但水平方向铺得很大的网格。
    /// 21 West Main 自带一块 10.48 m 见方、y 恒为 0 的板，不排除的话会把整栋楼缩成一个小盒子。
    static Bounds RenderBounds(GameObject go, bool skipFlat = false)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in rs)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds rb = r.bounds;
            if (rb.size.x <= 0f && rb.size.y <= 0f && rb.size.z <= 0f) continue;
            if (skipFlat && rb.size.y < 0.15f && rb.size.x * rb.size.z > 4f) continue;
            if (!any) { b = rb; any = true; }
            else b.Encapsulate(rb);
        }
        return b;
    }

    static string CountRenderers(GameObject go)
    {
        int n = 0, verts = 0;
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            n++;
            verts += mf.sharedMesh.vertexCount;
        }
        return string.Format("({0} 个网格 / {1} 顶点)", n, verts);
    }

    // ───────────────────────── 地台 ─────────────────────────

    /// 21 West Main 自带一块 10.48 m 见方、厚度为 0 的地台平板。
    /// 它会被算进整体包围盒，把整栋楼缩成一个盒子；就算排除掉，它自己也会随倍率放大到 28 m 压住街道。
    /// 判据：厚度 < 15 cm，且水平面积 > 建筑本体面积的 2.5 倍 —— 屋顶面积和本体相当，不会被误伤。
    static void HideSlabs(GameObject inst)
    {
        Bounds body = RenderBounds(inst, true);
        float bodyArea = Mathf.Max(0.01f, body.size.x * body.size.z);
        int hidden = 0;
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            Bounds b = r.bounds;
            if (b.size.y > 0.15f) continue;
            float area = b.size.x * b.size.z;
            if (area < bodyArea * 2.5f) continue;
            Undo.RecordObject(r, "Hide ground slab");
            r.enabled = false;
            hidden++;
        }
        if (hidden > 0)
            Debug.LogFormat("[Street]   {0}: 关掉了 {1} 块地台平板（本体 {2:F1}x{3:F1}）",
                inst.name, hidden, body.size.x, body.size.z);
    }

    // ───────────────────────── 碰撞体 ─────────────────────────

    /// 给模型补一个贴合本体的 BoxCollider。
    ///
    /// **必须做**：FBX 导入默认不生成碰撞体，而原来白盒 Cube 的 BoxCollider 在白盒被 SetActive(false)
    /// 之后也跟着失效 —— 不补的话玩家能直接穿过楼，猫的视线也能穿墙（本关的追逐全靠视线遮挡）。
    ///
    /// 用 Box 而不是 MeshCollider：工业仓库有 165 万顶点，MeshCollider 会把编辑器卡死。
    ///
    /// ⚠️ 尺寸必须在 **holder 的本地空间** 里量。旧版拿 Renderer.bounds（世界 AABB）除以 k 塞进
    /// BoxCollider.size —— 可 size 是本地空间的量，而 holder 还挂着 90° 偏航，于是存进去的盒子
    /// 会被**再转一次**：世界尺寸 16.9×7.7 的楼落成 7.7×16.9 的碰撞体。
    /// 表现是「楼的侧面能走进去（穿模）+ 过道里凭空多一道隐形墙」，而日志一切正常。
    static void AddBodyCollider(GameObject holder, GameObject inst, float k)
    {
        Bounds local;
        if (!LocalBounds(holder.transform, inst, out local)) return;

        BoxCollider bc = holder.GetComponent<BoxCollider>();
        if (bc == null) bc = Undo.AddComponent<BoxCollider>(holder);

        bc.isTrigger = false;
        bc.center = local.center;
        bc.size = local.size;

        // 世界 AABB 只用来打日志对账 —— 真正存进 size 的是上面那行本地尺寸
        Bounds w = WorldBoundsOf(holder.transform, bc);
        Debug.LogFormat("[Street]   {0}: 楼体碰撞体 本地 {1:F1} x {2:F1} x {3:F1}，世界 {4:F1} x {5:F1} x {6:F1} m（缩放 {7:F2}）",
            holder.name, bc.size.x, bc.size.y, bc.size.z, w.size.x, w.size.y, w.size.z, k);
    }

    /// 在 holder 的本地空间里求「所有网格」的 AABB。
    /// 每个网格用 sharedMesh.bounds 的 8 个角 × (holder.worldToLocalMatrix × renderer.localToWorldMatrix)：
    /// 旋转和缩放都在矩阵里抵消，得到的才是能给 BoxCollider 用的本地尺寸。
    static bool LocalBounds(Transform holder, GameObject inst, out Bounds local)
    {
        local = new Bounds();
        bool any = false;
        Matrix4x4 toLocal = holder.worldToLocalMatrix;

        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

            Bounds lb;
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                if (smr.sharedMesh == null) continue;
                lb = smr.sharedMesh.bounds;
            }
            else
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                lb = mf.sharedMesh.bounds;
            }
            if (lb.size.sqrMagnitude <= 0f) continue;

            Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
            Vector3 mn = lb.min, mx = lb.max;
            for (int c = 0; c < 8; c++)
            {
                Vector3 p = m.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? mn.x : mx.x,
                                                           (c & 2) == 0 ? mn.y : mx.y,
                                                           (c & 4) == 0 ? mn.z : mx.z));
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }
        return any;
    }

    static Bounds WorldBoundsOf(Transform holder, BoxCollider bc)
    {
        Vector3 c = bc.center, e = bc.size * 0.5f;
        Bounds b = new Bounds();
        bool any = false;
        for (int i = 0; i < 8; i++)
        {
            Vector3 lp = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                         (i & 2) == 0 ? -e.y : e.y,
                                         (i & 4) == 0 ? -e.z : e.z);
            Vector3 wp = holder.TransformPoint(lp);
            if (!any) { b = new Bounds(wp, Vector3.zero); any = true; }
            else b.Encapsulate(wp);
        }
        return b;
    }

    // ───────────────────────── 材质 ─────────────────────────

    static void FixMaterials(GameObject go)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material fixedMat = FixMaterial(mats[i]);
                if (!ReferenceEquals(fixedMat, mats[i]))
                {
                    mats[i] = fixedMat;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static Material FixMaterial(Material src)
    {
        if (src == null) return null;
        Material cached;
        if (s_fixed.TryGetValue(src, out cached) && cached != null) return cached;

        // 已经是 URP 材质就原样用
        if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline"))
        {
            s_fixed[src] = src;
            return src;
        }

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogWarning("[Street] 找不到 URP/Lit，材质保持原样（可能是洋红色）。");
            s_fixed[src] = src;
            return src;
        }

        Material m = new Material(lit);
        m.name = src.name + " (URP)";

        Texture tex = null;
        if (src.HasProperty("_MainTex")) tex = src.mainTexture;
        if (tex == null && src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
        if (tex != null)
        {
            m.SetTexture("_BaseMap", tex);
            if (src.HasProperty("_MainTex"))
            {
                m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
                m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
            }
        }

        Color col = Color.white;
        if (src.HasProperty("_Color")) col = src.GetColor("_Color");
        else if (src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
        m.SetColor("_BaseColor", col);

        Texture bump = null;
        if (src.HasProperty("_BumpMap")) bump = src.GetTexture("_BumpMap");
        if (bump != null)
        {
            m.SetTexture("_BumpMap", bump);
            m.EnableKeyword("_NORMALMAP");
        }

        if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", Mathf.Clamp01(src.GetFloat("_Metallic")));
        if (src.HasProperty("_Glossiness")) m.SetFloat("_Smoothness", Mathf.Clamp01(src.GetFloat("_Glossiness")));

        s_fixed[src] = m;
        return m;
    }

    // ───────────────────────── 载入 ─────────────────────────

    static GameObject[] LoadModels()
    {
        var arr = new GameObject[Paths.Length];
        for (int i = 0; i < Paths.Length; i++)
        {
            if (!System.IO.File.Exists(Paths[i]))
            {
                Debug.LogError("[Street] 找不到模型文件：" + Paths[i]);
                return null;
            }
            // 新解压出来的 fbx 可能还没被 Unity 导入
            AssetDatabase.ImportAsset(Paths[i], ImportAssetOptions.ForceUpdate);
            arr[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Paths[i]);
            if (arr[i] == null)
            {
                Debug.LogError("[Street] 导入失败：" + Paths[i]);
                return null;
            }
            Debug.Log("[Street] 已载入 " + Tags[i] + " : " + Paths[i]);
        }
        return arr;
    }
}
