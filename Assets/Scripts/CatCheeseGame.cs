// CatCheeseGame.cs — 猫吃奶酪玩法 v2（运行时自装配，不改场景文件、不改课程工具）
//
// 玩法：场景里放一个名为「CatMoveSpace」的空物体（Position 为中心、Scale 为尺寸的长方体，
//       即猫的移动空间）；猫在这个区域内巡逻，区域内自动摆放 3 块奶酪。
//       玩家（第一人称）收集完 3 块奶酪且不被猫抓到 → 胜利；被猫靠近（<1.2 m）→ 失败。
// 成败画面复用 UI_Screens.unity 里现成的 End Screen_Win / End Screen_Lose。
//
// 猫模型：Resources/GameProps/Cat walk.glb（需 glTFast 包导入，已装 6.9.1）
// 奶酪模型：Resources/GameProps/Cheese_0001_LOD0.fbx + 同目录 albedo 贴图
// 模型缺失时自动用占位方块代替，游戏仍然可玩。
//
// ⚠️ 尺寸与贴地一律走「网格实测」，绝不使用 Renderer.bounds：
//    猫是蒙皮模型，Renderer.bounds 由骨骼矩阵推出（会带上骨骼上的非单位缩放、还顺绑定姿势旋转），
//    实测偏差可达数百倍；而且实例化当帧它可能还没刷新。用它算缩放 → 猫变成巨物，算贴地 → 底面偏移错 ⇒ 悬在空中。
//    正确做法：MeshFilter/SkinnedMeshRenderer 的 sharedMesh.bounds 八个角 × renderer.localToWorldMatrix。

using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// 上一局结果：-1 无；0 被抓（失败）；1 集齐奶酪（成功）
internal static class CatCheeseResult
{
    internal static int Last = -1;
}

internal static class CatCheeseBootstrap
{
    internal const string SampleSceneName = "SampleScene";
    internal const string UiSceneName = "UI_Screens";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => Handle(scene);
        Scene active = SceneManager.GetActiveScene();
        if (active.isLoaded) Handle(active);
    }

    static void Handle(Scene scene)
    {
        // 居民区关卡接管后，旧的「猫吃奶酪」整套装配让位；把 Enabled 改回 false 就能复现旧玩法
        if (NbMode.Enabled) return;

        if (scene.name == SampleSceneName) CatCheeseSetup.Install();
        else if (scene.name == UiSceneName) ApplyEndScreen(scene);
    }

    /// 在 UI_Screens 里点亮对应的结算页（复用学生做好的页面）
    static void ApplyEndScreen(Scene scene)
    {
        int r = CatCheeseResult.Last;
        if (r < 0) return; // 正常从标题进入，不干预场景保存的默认状态
        SetActiveInScene(scene, "End Screen_Win", r == 1);
        SetActiveInScene(scene, "End Screen_Lose", r == 0);
        SetActiveInScene(scene, "Title", false);
        SetActiveInScene(scene, "Title Screen", false);
        Debug.LogFormat("[CatCheese] {0} 已切换到结算页：{1}",
            r == 1 ? "集齐 3 块奶酪，挑战成功！" : "被猫抓到，挑战失败！",
            r == 1 ? "End Screen_Win" : "End Screen_Lose");
    }

    static void SetActiveInScene(Scene scene, string objName, bool on)
    {
        GameObject go = FindInScene(scene, objName);
        if (go != null) go.SetActive(on);
        else Debug.LogWarning("[CatCheese] UI_Screens 里找不到名为「" + objName + "」的物体，请检查页面命名。");
    }

    /// GameObject.Find 找不到未激活物体，这里按场景遍历（含未激活）
    internal static GameObject FindInScene(Scene scene, string objName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objName) return root;
            Transform direct = root.transform.Find(objName);
            if (direct != null) return direct.gameObject;
        }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
                if (t.name == objName) return t.gameObject;
        }
        return null;
    }
}

internal static class CatCheeseSetup
{
    internal const string SpaceName = "CatMoveSpace";

    const float CatHeight = 0.5f;      // 猫归一化后的身高（米）
    const float CheeseHeight = 0.35f;  // 奶酪归一化后的高度（米）
    const float CatSpeed = 1.6f;       // 巡逻速度 m/s
    const float EdgeMargin = 0.6f;     // 巡逻点距区域边缘的留白
    internal static readonly int PropLayer = LayerMask.NameToLayer("Ignore Raycast"); // 生成物不挡地面射线
    // 生成物的层掩码：Ignore Raycast 这一层被 Physics.DefaultRaycastLayers 排除掉（地面探测必须看不见生成物），
    // 所以「猫绕开障碍」的扫掠要显式把这一层加回来，否则扫掠永远打不到奶酪。
    internal static readonly int PropMask = PropLayer < 0 ? 0 : (1 << PropLayer);

    internal static void Install()
    {
        CatCheeseResult.Last = -1; // 新的一局

        // —— 读取猫的移动空间（position+scale 长方体）——
        Scene scene = SceneManager.GetActiveScene();
        GameObject space = CatCheeseBootstrap.FindInScene(scene, SpaceName);
        if (space == null)
        {
            Debug.LogError("[CatCheese] 场景里没有找到名为「" + SpaceName + "」的空物体——请先创建它（Position=中心，Scale=尺寸），再进 Play。本次不生成玩法。");
            return;
        }
        Vector3 c = space.transform.position;
        Vector3 s = space.transform.lossyScale;
        Bounds area = new Bounds(c, s);
        if (area.size.x < 0.5f || area.size.z < 0.5f)
            Debug.LogWarningFormat("[CatCheese] 「{0}」的范围只有 {1:F1}×{2:F1} m（Scale 即尺寸），似乎太小——记得把 Scale 调成想要的区域大小。",
                SpaceName, area.size.x, area.size.z);
        Debug.LogFormat("[CatCheese] 已读取「{0}」：中心 ({1:F1}, {2:F1}, {3:F1})，尺寸 {4:F1}×{5:F1}×{6:F1} m。",
            SpaceName, c.x, c.y, c.z, s.x, s.y, s.z);

        // —— 猫 ——（先按骨骼对朝向，再缩放贴地：绕 Y 旋转不改变竖直范围）
        GameObject cat = SpawnModel("GameProps/Cat walk", new Color(0.16f, 0.12f, 0.10f), "Cat (auto)");
        OrientCatByBones(cat, Vector3.forward);
        Bounds catBox = FitAndGround(cat, CatHeight, area.center, "猫 (auto)");
        CatPatrol patrol = cat.AddComponent<CatPatrol>();
        patrol.Configure(area, EdgeMargin, CatSpeed);
        patrol.ConfigureBody(catBox, cat.transform.position.y); // 让猫会绕开奶酪，而不是穿过去
        PlayWalkAnimation(cat);

        // —— 3 块奶酪：分散在区域内的固定比例点 ——
        Vector3[] spots =
        {
            PointInArea(area, 0.20f, 0.20f),
            PointInArea(area, 0.80f, 0.50f),
            PointInArea(area, 0.50f, 0.85f),
        };
        Transform[] cheeses = new Transform[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            GameObject ch = SpawnModel("GameProps/Cheese_0001_LOD0", new Color(0.95f, 0.75f, 0.15f), "Cheese " + (i + 1) + " (auto)");
            WireCheeseTexture(ch);
            FitAndGround(ch, CheeseHeight, spots[i], "奶酪 " + (i + 1) + " (auto)");
            AddSolidBoxCollider(ch); // 实体碰撞体：必须在缩放贴地之后加，尺寸才对
            cheeses[i] = ch.transform;
        }

        CatCheeseGame game = new GameObject("CatCheeseGame (auto)").AddComponent<CatCheeseGame>();
        game.cat = cat.transform;
        game.cheeses = cheeses;
        game.hud = CheeseHud.Create(cheeses.Length);   // 右上角奶酪计数

        Debug.LogFormat("[CatCheese] 开局：猫（高 {0:F2} m）在区域内以 {1:F1} m/s 巡逻；{2} 块奶酪已摆放。集齐它们且别被猫抓到！",
            CatHeight, CatSpeed, cheeses.Length);
    }

    /// 区域内 (fx, fz) ∈ [0,1]² 的相对位置转世界坐标（y 用区域中部）
    static Vector3 PointInArea(Bounds area, float fx, float fz)
    {
        Vector3 e = area.extents;
        return new Vector3(area.center.x + (fx - 0.5f) * 2f * e.x, area.center.y, area.center.z + (fz - 0.5f) * 2f * e.z);
    }

    /// 从高空向下打射线，取「最低」的那个命中点当地面。
    /// 取最低而不是第一个：这样天花板/墙顶/玩家头顶都不会被误当地面（地面在所有遮挡物之下）。
    internal static bool TryGroundY(Vector3 p, out float groundY)
    {
        groundY = p.y;
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 300f, p.z), Vector3.down, _hits, 600f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool found = false;
        float lowest = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float h = _hits[i].point.y;
            if (h < lowest) { lowest = h; found = true; }
        }
        if (found) groundY = lowest;
        return found;
    }
    static readonly RaycastHit[] _hits = new RaycastHit[32];

    /// 网格实测世界包围盒（不用 Renderer.bounds）：
    /// MeshFilter/SkinnedMeshRenderer 的 sharedMesh.bounds 八个角 × renderer.localToWorldMatrix
    internal static bool TryMeasureWorldBounds(GameObject root, out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null) continue;
            Bounds lb;
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                if (smr.sharedMesh != null) lb = smr.sharedMesh.bounds;
                else lb = smr.localBounds;
            }
            else
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) lb = mf.sharedMesh.bounds;
                else lb = r.localBounds;
            }
            if (lb.size.sqrMagnitude <= 0f) continue;
            Matrix4x4 m = r.transform.localToWorldMatrix;
            Vector3 mn = lb.min, mx = lb.max;
            for (int k = 0; k < 8; k++)
            {
                Vector3 w = m.MultiplyPoint3x4(new Vector3((k & 1) == 0 ? mn.x : mx.x,
                                                           (k & 2) == 0 ? mn.y : mx.y,
                                                           (k & 4) == 0 ? mn.z : mx.z));
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                else b.Encapsulate(w);
            }
        }
        return any;
    }

    /// 按网格实测把模型归一化到目标高度，再把底面贴到地面。
    /// 返回**贴好之后的**世界包围盒（失败时返回零尺寸盒），调用方用它来加碰撞体 / 定扫掠参数。
    internal static Bounds FitAndGround(GameObject go, float targetHeight, Vector3 xz, string label)
    {
        Bounds raw;
        if (!TryMeasureWorldBounds(go, out raw) || raw.size.y <= 1e-6f)
        {
            Debug.LogWarning("[CatCheese] " + label + "：量不到网格尺寸，不改缩放、不贴地。");
            return new Bounds(go.transform.position, Vector3.zero);
        }

        float k = targetHeight / raw.size.y;
        go.transform.localScale = go.transform.localScale * k;

        Bounds fit;
        if (!TryMeasureWorldBounds(go, out fit))
        {
            Debug.LogWarning("[CatCheese] " + label + "：缩放后量不到包围盒，跳过贴地。");
            return new Bounds(go.transform.position, Vector3.zero);
        }

        float groundY;
        bool hit = TryGroundY(xz, out groundY);
        Vector3 before = go.transform.position;
        Vector3 pos = before;
        pos.x = xz.x;
        pos.z = xz.z;
        pos.y += groundY - fit.min.y; // 让模型底面正好落在 groundY
        go.transform.position = pos;

        // FootOffset = 根位置到模型底面的距离（与当前所在高度无关，故就算上面算错了它也是对的）
        GroundStick gs = go.AddComponent<GroundStick>();
        gs.FootOffset = pos.y - fit.min.y;

        Debug.LogFormat("[CatCheese] {0}：网格实测 {1:F3}×{2:F3}×{3:F3} m → ×{4:F5} 缩到高 {5:F2} m；底面贴到{6} y={7:F3}（根位置 y={8:F3}）",
            label, raw.size.x, raw.size.y, raw.size.z, k, targetHeight,
            hit ? "射线命中的地面" : "【射线没命中，用了原点高度】", groundY, pos.y);

        fit.center += pos - before; // fit 是在「搬动之前」量出来的，补上位移才是最终位置
        return fit;
    }

    /// 给生成物挂一个「贴合模型外形」的实体 BoxCollider —— 撞上去不会穿模。
    /// 尺寸取网格实测的世界包围盒，再换算回根节点本地空间，所以缩放/旋转都是对的。
    /// 层仍留在 Ignore Raycast：那层只对射线隐形（地面探测必须看不见生成物），物理碰撞照常生效。
    /// 返回挂上的那个碰撞体：调用方可能要拿它做 Physics.IgnoreCollision（比如让老鼠穿过奶酪）。
    internal static Collider AddSolidBoxCollider(GameObject go)
    {
        Bounds b;
        if (!TryMeasureWorldBounds(go, out b) || b.size.sqrMagnitude <= 0f)
        {
            Debug.LogWarning("[CatCheese] " + go.name + "：量不到包围盒，没加碰撞体。");
            return null;
        }

        BoxCollider bc = go.GetComponent<BoxCollider>();
        if (bc == null) bc = go.AddComponent<BoxCollider>(); // 占位方块自带一个，直接复用

        Transform t = go.transform;
        Vector3 mn = b.min, mx = b.max;
        Vector3 lmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 lmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int k = 0; k < 8; k++)
        {
            Vector3 w = new Vector3((k & 1) == 0 ? mn.x : mx.x,
                                    (k & 2) == 0 ? mn.y : mx.y,
                                    (k & 4) == 0 ? mn.z : mx.z);
            Vector3 l = t.InverseTransformPoint(w);
            lmin = Vector3.Min(lmin, l);
            lmax = Vector3.Max(lmax, l);
        }

        bc.isTrigger = false;
        bc.center = (lmin + lmax) * 0.5f;
        bc.size = lmax - lmin;
        Debug.LogFormat("[CatCheese] {0}：已加实体碰撞体（本地尺寸 {1:F3}×{2:F3}×{3:F3} m，中心 ({4:F3}, {5:F3}, {6:F3})）。",
            go.name, bc.size.x, bc.size.y, bc.size.z, bc.center.x, bc.center.y, bc.center.z);
        return bc;
    }

    /// 生成模型；资源缺失时退化为占位方块（游戏仍可玩）。缩放与贴地由 FitAndGround 负责。
    internal static GameObject SpawnModel(string resourcePath, Color fallbackColor, string goName)
    {
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        GameObject go = null;
        if (prefab != null)
        {
            go = Object.Instantiate(prefab);
            go.name = goName;
            Debug.Log("[CatCheese] 已从 Resources 生成「" + resourcePath + "」。");
        }
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = goName + " (占位)";
            // 保留自带的 BoxCollider：占位方块本来就是「这东西该有的体积」，
            // 之后 AddSolidBoxCollider 会按实测包围盒把它校正到正确尺寸。
            go.GetComponent<Renderer>().sharedMaterial = MakeUrpMat(fallbackColor);
            Debug.LogWarning("[CatCheese] Resources 里没有「" + resourcePath + "」（glTFast 可能还没解析完或模型未导入），已用占位方块代替。");
        }
        SetLayerRecursively(go, PropLayer); // 生成物不参与射线，避免猫/奶酪互相当地面
        return go;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    /// 奶酪贴图：FBX 导入不会自动连外部贴图，运行时手动接 albedo
    internal static void WireCheeseTexture(GameObject cheese)
    {
        Texture2D albedo = Resources.Load<Texture2D>("GameProps/Cheese_0001_albedo");
        if (albedo == null)
        {
            Debug.LogWarning("[CatCheese] 没找到奶酪 albedo 贴图（GameProps/Cheese_0001_albedo），奶酪用默认白模。");
            return;
        }
        foreach (Renderer r in cheese.GetComponentsInChildren<Renderer>())
        {
            Material m = r.material; // 实例化，避免改到共享材质
            m.mainTexture = albedo;
        }
    }

    /// 用骨骼自动判别猫的正面（脚尖方向最可靠，头-盆骨兜底），对齐到移动方向
    internal static void OrientCatByBones(GameObject cat, Vector3 moveDir)
    {
        Vector3 fwd = Vector3.zero;
        int found = 0;
        foreach (string side in new[] { "L", "R" })
        {
            Transform foot = FindBone(cat.transform, "cat_bip-" + side + "-Foot");
            Transform toe = FindBone(cat.transform, "cat_bip-" + side + "-Toe0");
            if (foot != null && toe != null)
            {
                Vector3 v = toe.position - foot.position;
                v.y = 0f;
                if (v.sqrMagnitude > 1e-8f) { fwd += v.normalized; found++; }
            }
        }
        if (found == 0)
        {
            Transform pelvis = FindBone(cat.transform, "cat_bip-Pelvis");
            Transform head = FindBone(cat.transform, "cat_bip-Head");
            if (pelvis != null && head != null)
            {
                Vector3 v = head.position - pelvis.position;
                v.y = 0f;
                if (v.sqrMagnitude > 1e-6f) { fwd = v.normalized; found = 1; }
            }
        }
        if (found > 0)
        {
            fwd = (fwd / found).normalized;
            Quaternion fix = Quaternion.FromToRotation(fwd, moveDir); // 两个水平向量之间 ⇒ 只会绕 Y 转
            cat.transform.rotation = fix * cat.transform.rotation;
            Debug.Log("[CatCheese] 已按骨骼自动对齐猫的朝向。");
        }
        else
        {
            cat.transform.rotation = Quaternion.LookRotation(moveDir);
            Debug.LogWarning("[CatCheese] 没找到猫的骨骼节点，按默认朝向放置（若猫横着走，运行后把 Cat (auto) 绕 Y 转 180° 即可）。");
        }
    }

    static Transform FindBone(Transform t, string exactName)
    {
        if (t.name == exactName) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform r = FindBone(t.GetChild(i), exactName);
            if (r != null) return r;
        }
        return null;
    }

    /// 播放走路动画：优先 legacy Animation 组件，否则用 PlayableGraph（无需 AnimatorController）
    internal static void PlayWalkAnimation(GameObject cat)
    {
        Animation legacy = cat.GetComponentInChildren<Animation>(true);
        if (legacy != null && legacy.clip != null)
        {
            legacy.wrapMode = WrapMode.Loop;
            if (!legacy.isPlaying) legacy.Play();
            Debug.Log("[CatCheese] 猫走路动画已播放（legacy Animation，clip=" + legacy.clip.name + "）。");
            return;
        }

        AnimationClip[] clips = Resources.LoadAll<AnimationClip>("GameProps/Cat walk");
        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning("[CatCheese] 没找到猫的动画片段（GLB 可能还没被 glTFast 导入），猫暂时只平移不播动画。");
            return;
        }
        AnimationClip clip = clips[0];
        foreach (AnimationClip c in clips)
            if (c.name.ToLower().Contains("walk")) { clip = c; break; }

        Animator animator = cat.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = cat.AddComponent<Animator>();
        animator.applyRootMotion = false; // 巡逻位移由 CatPatrol 驱动，不吃根运动

        PlayableGraph graph = PlayableGraph.Create("CatWalkGraph");
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "catWalk", animator);
        AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyPlayableIK(false);
        output.SetSourcePlayable(playable);
        graph.Play();
        cat.AddComponent<PlayableGraphKeeper>().graph = graph;
        Debug.Log("[CatCheese] 猫走路动画已播放（PlayableGraph，clip=" + clip.name + "）。");
    }

    static Material MakeUrpMat(Color c)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        m.color = c;
        return m;
    }
}

/// 每帧把物体贴到脚下地面；FootOffset 由 CatCheeseSetup 用网格实测写入（根位置到模型底面）
internal class GroundStick : MonoBehaviour
{
    internal float FootOffset;
    bool _verified;

    void LateUpdate()
    {
        Vector3 p = transform.position;
        float groundY;
        if (CatCheeseSetup.TryGroundY(p, out groundY))
        {
            float target = groundY + FootOffset;
            if (Mathf.Abs(p.y - target) > 0.0005f)
                transform.position = new Vector3(p.x, target, p.z);
        }

        if (!_verified)
        {
            _verified = true;
            Bounds b;
            float g;
            if (CatCheeseSetup.TryMeasureWorldBounds(gameObject, out b) && CatCheeseSetup.TryGroundY(transform.position, out g))
            {
                float gap = b.min.y - g;
                if (Mathf.Abs(gap) > 0.03f)
                    Debug.LogWarningFormat("[CatCheese] 贴地复核偏差：「{0}」底面 y={1:F3}，地面 y={2:F3}，差 {3:F3} m —— 若看着仍悬空/陷地，把这一行发我。",
                        name, b.min.y, g, gap);
                else
                    Debug.LogFormat("[CatCheese] 贴地复核通过：「{0}」底面 y={1:F3}，地面 y={2:F3}。", name, b.min.y, g);
            }
        }
    }
}

/// 猫在区域内随机取点巡逻：走到一个点、停顿一下、再走向下一个点，转身平滑。
/// 走位时扫掠前方，遇到奶酪这类实体障碍就沿其表面滑过去 —— 是「绕开」，不是「穿过去」。
internal class CatPatrol : MonoBehaviour
{
    Bounds _area;
    float _margin, _speed, _wait;
    Vector3 _target;
    bool _hasTarget;
    float _dodge;               // 连续「绕行」了多久（用来判断是不是绕不出去了）

    // —— 避障扫掠参数（由 ConfigureBody 按模型实测写入）——
    int _propMask;              // 生成物所在层的掩码
    float _castY = 0.25f;       // 扫掠球心相对根位置的高度
    float _castR = 0.12f;       // 扫掠半径 ≈ 半身宽
    float _look = 0.55f;        // 前视距离 ≈ 半身长 + 余量

    public void Configure(Bounds area, float margin, float speed)
    {
        _area = area; _margin = margin; _speed = speed;
    }

    /// 由 CatCheeseSetup 在「缩放 + 贴地」完成后调用：把猫的实测体型换算成扫掠参数。
    public void ConfigureBody(Bounds worldBox, float rootY)
    {
        if (worldBox.size.sqrMagnitude <= 0f)
        {
            Debug.LogWarning("[CatCheese] 猫：量不到体型，本次不做避障扫掠（会从奶酪里穿过去）。");
            return;
        }
        float halfW = Mathf.Min(worldBox.size.x, worldBox.size.z) * 0.5f; // 半身宽
        float halfL = Mathf.Max(worldBox.size.x, worldBox.size.z) * 0.5f; // 半身长
        _castR = Mathf.Clamp(halfW + 0.05f, 0.09f, 0.40f); // +5 cm 余量，免得身体边缘蹭到奶酪
        _look = Mathf.Clamp(halfL + 0.30f, 0.35f, 1.50f);
        _castY = (worldBox.min.y + worldBox.max.y) * 0.5f - rootY;
        _propMask = CatCheeseSetup.PropMask;
        Debug.LogFormat("[CatCheese] 猫的避障扫掠：半径 {0:F3} m、前视 {1:F3} m、球心高 {2:F3} m、层掩码 {3}。",
            _castR, _look, _castY, _propMask);
    }

    void Update()
    {
        if (_wait > 0f)
        {
            _wait -= Time.deltaTime;
            return;
        }
        if (!_hasTarget) PickNext();

        Vector3 pos = transform.position;
        Vector3 to = _target - pos;
        to.y = 0f;
        float step = _speed * Time.deltaTime;
        if (to.sqrMagnitude <= step * step)
        {
            transform.position = new Vector3(_target.x, pos.y, _target.z);
            _hasTarget = false;
            _dodge = 0f;
            _wait = Random.Range(0.4f, 1.2f); // 到点后张望一下再走
            return;
        }

        Vector3 dir = to.normalized;
        Vector3 steer = Avoid(dir);

        // 老是在绕行（方向被改掉超过 ~11°）而且绕了 2.5 s 还没绕到 → 换个目标点，
        // 否则目标点在障碍背后时会贴着奶酪原地打转。
        _dodge = Vector3.Dot(steer, dir) < 0.98f ? _dodge + Time.deltaTime : 0f;
        if (_dodge > 2.5f)
        {
            _dodge = 0f;
            _hasTarget = false;
            return;
        }

        transform.position = pos + steer * step;
        Quaternion look = Quaternion.LookRotation(steer);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 8f * Time.deltaTime);
    }

    /// 前方球扫掠：撞到障碍就把前进方向投影到障碍表面 → 沿切线滑过去（经典 collide-and-slide）。
    Vector3 Avoid(Vector3 dir)
    {
        if (_propMask == 0) return dir;

        RaycastHit h;
        if (!Physics.SphereCast(transform.position + Vector3.up * _castY, _castR, dir,
                                out h, _look, _propMask, QueryTriggerInteraction.Ignore))
            return dir;
        if (h.collider == null || h.collider.transform.root == transform.root) return dir; // 撞到自己的部件，忽略

        Vector3 n = h.normal;
        n.y = 0f;
        if (n.sqrMagnitude > 1e-4f)
        {
            Vector3 slide = Vector3.ProjectOnPlane(dir, n.normalized);
            slide.y = 0f;
            if (slide.sqrMagnitude > 1e-4f) return slide.normalized;
        }
        // 法线太竖直（扫到顶面）→ 没有可用切线，挑一条固定的侧向绕开
        return new Vector3(-dir.z, 0f, dir.x).normalized;
    }

    void PickNext()
    {
        Vector3 min = _area.min + Vector3.one * _margin;
        Vector3 max = _area.max - Vector3.one * _margin;
        if (max.x < min.x) { float m = (min.x + max.x) * 0.5f; min.x = max.x = m; }
        if (max.z < min.z) { float m = (min.z + max.z) * 0.5f; min.z = max.z = m; }

        _target = new Vector3(Random.Range(min.x, max.x), transform.position.y, Random.Range(min.z, max.z));
        // 目标点别落在奶酪里（否则会贴着它原地打转），试几次换一个
        for (int i = 0; i < 8 && Blocked(_target); i++)
            _target = new Vector3(Random.Range(min.x, max.x), transform.position.y, Random.Range(min.z, max.z));
        _hasTarget = true;
    }

    /// 这个点是不是压在障碍上
    bool Blocked(Vector3 p)
    {
        if (_propMask == 0) return false;
        Vector3 o = p + Vector3.up * _castY;
        int n = Physics.OverlapSphereNonAlloc(o, _castR + 0.15f, _overlap, _propMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (_overlap[i] != null && _overlap[i].transform.root != transform.root) return true;
        return false;
    }
    static readonly Collider[] _overlap = new Collider[16];
}

/// 保住 PlayableGraph 的引用并在销毁时释放
internal class PlayableGraphKeeper : MonoBehaviour
{
    internal PlayableGraph graph;

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}

/// 胜负判定：收集 3 块奶酪、被猫抓则失败
internal class CatCheeseGame : MonoBehaviour
{
    internal Transform cat;
    internal Transform[] cheeses;
    internal CheeseHud hud;          // 右上角计数（可能为 null，判空再用）

    [Header("吃到时的反馈")]
    public bool popOnEat = true;         // 被咬掉的小动画（先鼓一下再缩没）
    public float eatPopDuration = 0.22f;

    [Header("判定半径（觉得猫太凶 / 吃不到奶酪就调这里）")]
    public float catchRadius = 1.2f;   // 与猫的水平距离小于此值 → 被抓
    public float eatRadius = 0.9f;     // 与奶酪的水平距离小于此值 → 吃到

    const float CatTopY = 0.45f;     // 猫背高度（归一化 0.5 m 身高留一点余量）
    const float PlayerHalf = 0.9f;   // 玩家胶囊中心到脚底
    const float SameLevelY = 1.6f;   // 只有和奶酪高度差在这么以内才算同一层

    bool _done;
    bool _winPending;
    bool _spawnReported;
    bool[] _eaten;                   // 已经吃掉的那些（动画期间也算，避免重复计数）
    int _collected;

    void Update()
    {
        if (_done) return;

        Vector3 playerCenter;
        if (!TryGetPlayerCenter(out playerCenter)) return;
        float feetY = playerCenter.y - PlayerHalf;

        // —— 开局自检：出生点离猫多远（上次 Play 一块奶酪都没吃到就说明可能一出生就在猫旁边）——
        if (!_spawnReported)
        {
            _spawnReported = true;
            if (cat != null)
            {
                float d = HorizontalDist(playerCenter, cat.position);
                Debug.LogFormat("[CatCheese] 出生点距猫 {0:F2} m（抓取半径 {1:F2} m，吃奶酪半径 {2:F2} m）——{3}",
                    d, catchRadius, eatRadius,
                    d <= catchRadius ? "★一出生就在抓取范围内！把玩家出生点挪远，或把 CatMoveSpace 挪开★" : "安全");
            }
        }

        // —— 被抓判定（跳到猫背上方不算）——
        if (cat != null)
        {
            Vector3 catPos = cat.position;
            bool jumpedOver = feetY > catPos.y + CatTopY;
            if (!jumpedOver && HorizontalDist(playerCenter, catPos + Vector3.up * 0.25f) < catchRadius)
            {
                Lose();
                return;
            }
        }

        // —— 吃奶酪判定（逐块收集：靠近就消失，右上角计数 +1）——
        if (cheeses != null)
        {
            if (_eaten == null) _eaten = new bool[cheeses.Length];

            for (int i = 0; i < cheeses.Length; i++)
            {
                if (_eaten[i]) continue;            // 已经吃掉的别再判定（正在播动画的那块也算）
                Transform ch = cheeses[i];
                if (ch == null) continue;
                bool sameLevel = Mathf.Abs(feetY - ch.position.y) < SameLevelY;
                if (sameLevel && HorizontalDist(playerCenter, ch.position) < eatRadius)
                {
                    Eat(i, ch);
                }

            }

            // 集齐了：等「被咬掉」的动画播完再切结算页，否则最后一块刚碰到就被切走、看不到它消失
            if (_collected >= cheeses.Length && !_winPending)
            {
                _winPending = true;
                StartCoroutine(WinAfterDelay(popOnEat ? eatPopDuration + 0.12f : 0f));
            }
        }
    }

    /// 吃掉一块：立刻计入数量并更新右上角，然后播「被咬掉」的小动画再隐藏
    void Eat(int index, Transform ch)
    {
        _eaten[index] = true;      // 先标记，动画期间不会被重复判定
        _collected++;
        if (hud != null) hud.SetCollected(_collected);
        Debug.LogFormat("[CatCheese] 吃到第 {0}/{1} 块奶酪！", _collected, cheeses.Length);

        if (popOnEat && ch.gameObject.activeInHierarchy) StartCoroutine(EatPop(ch));
        else ch.gameObject.SetActive(false);
    }

    /// 被咬掉的小动画：先微微鼓一下再缩没，然后隐藏。
    /// 只动缩放不动位置 —— 奶酪身上有 GroundStick 在 LateUpdate 里每帧贴地，改位置会被它顶回去。
    IEnumerator EatPop(Transform ch)
    {
        float d = Mathf.Max(eatPopDuration, 0.01f);
        Vector3 s0 = ch.localScale;
        float t = 0f;
        while (t < d)
        {
            if (ch == null) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / d);
            float scale = (1f + 0.4f * Mathf.Sin(k * Mathf.PI)) * (1f - k);   // 1 → 略鼓 → 0
            ch.localScale = s0 * Mathf.Max(scale, 0.0001f);
            yield return null;
        }
        if (ch != null) ch.gameObject.SetActive(false);
    }

    IEnumerator WinAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Win();
    }

    bool TryGetPlayerCenter(out Vector3 center)
    {
        CharacterController cc = FindFirstObjectByType<CharacterController>();
        if (cc != null)
        {
            center = cc.transform.position + Vector3.up * PlayerHalf;
            return true;
        }
        Camera cam = Camera.main;
        if (cam != null)
        {
            center = cam.transform.position;
            return true;
        }
        center = Vector3.zero;
        return false;
    }

    static float HorizontalDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void Win()
    {
        if (_done) return;
        _done = true;
        CatCheeseResult.Last = 1;
        Debug.Log("[CatCheese] 集齐所有奶酪 —— 挑战成功！");
        CatCheeseEndScreen.Show(true);   // 直接在游戏画面上弹「You Win」
    }

    void Lose()
    {
        if (_done) return;
        _done = true;
        CatCheeseResult.Last = 0;
        Debug.Log("[CatCheese] 被猫抓到了 —— 挑战失败。");
        CatCheeseEndScreen.Show(false);  // 直接在游戏画面上弹「Game Failed」
    }
}
