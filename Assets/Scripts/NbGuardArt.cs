// @role Loads the Mixamo guard model and hand-samples its walk animation onto the yard guard.
// @calls NbGuard

// NbGuardArt.cs — 把「会走路的真人」装到后院保安身上
//
// 模型：Assets/Resources/Guards/Walking.fbx
//   Mixamo 骨架（mixamorig:Hips / Spine / LeftArm …），110 根骨骼、11 块蒙皮网格、1976 个三角面，
//   带一个 32 帧的**原地**走路动画：Hips 的位移只有 9 cm 横向摆动，首尾帧完全相同，
//   没有前向位移 ⇒ 可以直接循环播，不会每个循环往前窜一段再被拉回来。
//
// 为什么必须放一份到 Resources 里：
//   保安是**运行时生成**的（场景里没有它们的 prefab 实例），而 AssetDatabase 只在编辑器里存在。
//   运行时加载资源的唯一入口是 Resources.Load，所以 Assets/walkinghuman/ 那份原件加载不到。
//   这里放的是副本，**故意不带 .meta** —— 带过去会让两个文件抢同一个 GUID，Unity 会把它判成
//   "同一个资源的两份拷贝"然后随机丢一个。
//
// 为什么不直接用 fbx 自带的材质：
//   fbx 里只有两个空材质（Material.001 / Black.），**没有贴图**。默认导入在 URP 下是洋红或纯白，
//   而这个角色在夜里应该是一块**比背景更黑**的剪影 —— 它靠手电的光锥被看见，不靠自身颜色。
//   所以材质在这里重做：制服一份、鞋/头发一份，两件共用（SRP Batcher 还能合批）。
//
// 已知的、只能靠 Play 验收的两件事（都留了日志）：
//   ① 朝向。按骨骼绑定姿势算出来是标准 Mixamo 朝向（左肩在 +X、脚朝 +Z），默认就当它面朝 +Z。
//      如果进 Play 看见保安**倒着走**，把 ModelYaw 改成 180 就行 —— 只影响外观，
//      手电和判定都挂在保安根节点上，与模型朝向无关。
//   ② 走路动画的播放方式。Animator 需要 AnimatorController 资产，而 controller **无法在运行时创建**；
//      Animation 组件只认 legacy 曲线，这个 fbx 是按 Generic 导入的。所以这里用
//      AnimationClip.SampleAnimation 手动按时间采样 —— 没有状态机、没有 controller、不改导入设置。
//      万一它在你的 Unity 版本里不生效，会退化成一个轻微的上下起伏（至少不是一块滑动的雕像），
//      并在 Console 里留下 "[GuardArt] 手动采样失败"。

using UnityEngine;

internal static class NbGuardArt
{
    const string Folder = "Guards";          // Resources/Guards
    const string Path   = "Guards/Walking";  // fbx 本体

    /// 保安身高（米）。**和旧版几何体一致** —— 手电高度、被照到的高度都是从它算的。
    internal const float Height = 1.80f;

    /// 模型自身朝向与保安 forward 的夹角（度）。
    /// 只在"看起来倒着走"时才需要改：0 = 模型面朝 +Z（Mixamo 的默认朝向），180 = 反了。
    internal const float ModelYaw = 0f;

    /// 手电在保安手里的局部位置。按身高比例给 —— 换身高不用再手算一遍。
    /// 只在**找不到手骨**时用；正常情况下 NbGuard 会拿模型右手的位置覆盖它。
    internal static Vector3 TorchLocal
    {
        get { return new Vector3(Height * 0.11f, Height * 0.80f, Height * 0.13f); }
    }

    // ───────────────────────── 手电本体 ─────────────────────────

    /// 造一支**看得见**的手电：握把 + 灯头 + 自发光镜片 + 一小盏点光。
    ///
    /// 为什么光靠一个 Light 不够：夜里只看得见地上那块光斑，看不见"谁在拿灯"。
    /// 玩家要读的是"那个人转向那边了" —— 手电本身得是个发光物体，视线才有落点。
    /// 所有部件都不带碰撞体、留在 Ignore Raycast 层：保安自己的判定射线是从镜片发出的，
    /// 留碰撞体的话"照到人"永远不会成立（这跟模型不带碰撞体是同一条理由）。
    internal static void BuildTorch(Transform lamp)
    {
        Color body = new Color(0.10f, 0.10f, 0.11f);
        Color grip = new Color(0.055f, 0.055f, 0.062f);

        // Unity 原始圆柱的轴是 Y，所以要绕 X 转 90° 才对着灯节点的 +Z（= 照射方向）。
        GameObject h = Nb.Cyl(lamp, "Torch Grip", new Vector3(0f, 0f, -0.09f), 0.052f, 0.18f, grip, false);
        h.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        GameObject head = Nb.Cyl(lamp, "Torch Head", new Vector3(0f, 0f, 0.025f), 0.084f, 0.07f, body, false);
        head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        GameObject lens = Nb.Ball(lamp, "Torch Lens", new Vector3(0f, 0f, 0.062f), 0.072f,
            new Color(1f, 0.95f, 0.80f), false);
        Renderer lr = lens.GetComponent<Renderer>();
        if (lr != null) lr.sharedMaterial = Nb.MatEmissive(new Color(1f, 0.94f, 0.78f), 3.4f);

        // 3 m 的点光：它的作用是把"手电在这儿"变成画面里最亮的那个小点，
        // 不是照亮院子（那是聚光灯的事，18 m）。
        GameObject glow = new GameObject("Torch Glow");
        glow.transform.SetParent(lamp, false);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.085f);
        Light pl = glow.AddComponent<Light>();
        pl.type = LightType.Point;
        pl.range = 3.2f;
        pl.intensity = 1.8f;
        pl.color = new Color(1f, 0.92f, 0.74f);
        pl.shadows = LightShadows.None;
    }

    /// 找模型的右手骨。Mixamo 叫 "mixamorig:RightHand"，别的骨架可能叫 Hand_R / hand_r。
    /// 找不到就返回 null —— 调用方退回固定的 TorchLocal，玩法不受影响。
    internal static Transform FindHand(GameObject model)
    {
        if (model == null) return null;

        Transform[] all = model.GetComponentsInChildren<Transform>(true);
        string[] keys = { "righthand", "right_hand", "hand_r", "handr" };

        // 先按**名字结尾**找：骨架里还有 RightHandIndex1…Thumb4，用"包含"会先命中
        // 手指骨（层级顺序只保证父在子前，不保证兄弟里的顺序），灯就跑到指节上去了。
        for (int k = 0; k < keys.Length; k++)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].name.ToLowerInvariant().EndsWith(keys[k], System.StringComparison.Ordinal))
                    return all[i];
            }
        }

        // 退一步才是"包含"，给那些带前缀/后缀的骨架用。
        for (int k = 0; k < keys.Length; k++)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].name.ToLowerInvariant().IndexOf(keys[k], System.StringComparison.Ordinal) >= 0)
                    return all[i];
            }
        }
        return null;
    }

    static bool _warned;     // "模型没导进来"只报一次，别每关刷屏
    static bool _reported;   // "模型已就位"只报一次（一关最多 4 个保安）

    // ───────────────────────── 对外 ─────────────────────────

    /// 往保安身上挂模型。成功返回模型根节点，失败返回 null（调用方退回几何体）。
    internal static GameObject Attach(Transform guard)
    {
        GameObject src = Resources.Load<GameObject>(Path);
        if (src == null)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[GuardArt] 读不到 Resources/" + Path + " —— 保安退回几何体。" +
                                 "确认 Unity 已经把 Assets/Resources/Guards/Walking.fbx 导入完" +
                                 "（切回编辑器让它 Refresh 一次）。");
            }
            return null;
        }

        GameObject go = Object.Instantiate(src);
        go.name = "Model";
        go.transform.SetParent(guard, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
        go.transform.localScale = Vector3.one;

        // ① 拆掉自带的两类东西
        //    · Animator：Generic 导入会带一个没有 controller 的 Animator，它和手动采样会抢同一批
        //      transform —— 两个都留着就是"有的骨头动、有的不动"，而且很难看出是哪边在动。
        //    · Collider：保安本来就不该挡人（1.2 m 的走道要靠从他身边挤过去），
        //      更要紧的是手电判定是从灯泡朝玩家打一条射线 —— 自己身上的碰撞体会先把这条射线吃掉，
        //      于是"照到人"永远不成立。
        Animator an = go.GetComponentInChildren<Animator>(true);
        if (an != null) Object.Destroy(an);
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) Object.Destroy(cols[i]);

        Fit(go, guard);
        Paint(go);
        AttachMotion(go);

        if (!_reported)
        {
            _reported = true;
            Debug.Log("[GuardArt] 保安模型就位：" + Path + "（身高 " + Height + " m，朝向偏转 " +
                      ModelYaw + "°）。");
        }
        return go;
    }

    // ───────────────────────── 尺寸与落地 ─────────────────────────

    /// 归一化到身高、水平居中、底面贴地。
    ///
    /// **不要用 Renderer.bounds 量** —— 蒙皮网格的 bounds 由根骨头推出来，会把骨头上的
    /// 非单位缩放一起带上（本项目的老鼠模型曾因此从 1.7 cm 报成 8.5 m，差 515 倍，且不报错）。
    /// TryMeasureWorldBounds 走的是 sharedMesh.bounds 的 8 个角点，是网格自己的量。
    static void Fit(GameObject go, Transform guard)
    {
        Bounds b;
        if (!CatCheeseSetup.TryMeasureWorldBounds(go, out b) || b.size.y < 1e-5f)
        {
            Debug.LogWarning("[GuardArt] 量不到模型网格尺寸 —— 不缩放、不贴地（会按 fbx 原比例站在原点）。");
            return;
        }

        float raw = b.size.y;
        float k = Height / raw;

        // 倍率夹一层：超出这个范围的"实测高度"只可能是量错了。硬缩会把模型缩没或者撑满整个院子，
        // 而两种结果都不会报错 —— 所以宁可放弃缩放，让它在画面里明显不对，也不要静默地错。
        if (k < 1e-3f || k > 1e3f)
        {
            Debug.LogWarningFormat("[GuardArt] 实测高 {0:F3} m，算出倍率 {1:F1} —— 不合理，已跳过缩放。", raw, k);
            return;
        }
        go.transform.localScale = go.transform.localScale * k;

        Transform p = go.transform.parent != null ? go.transform.parent : guard;

        // ② 水平居中：fbx 的原点常常不在两脚之间（这个模型 Hips 就在 x=6.8 / z=6.0 上），
        //    不居中的话保安会绕着"看不见的轴"转身，看起来像在绕着脚画圈。
        if (CatCheeseSetup.TryMeasureWorldBounds(go, out b))
            go.transform.position -= new Vector3(b.center.x - p.position.x, 0f, b.center.z - p.position.z);

        // ③ 贴地：包围盒底面压到保安脚下（保安根节点的 y 就是地面）
        if (CatCheeseSetup.TryMeasureWorldBounds(go, out b))
            go.transform.position += Vector3.up * (p.position.y - b.min.y);

        if (CatCheeseSetup.TryMeasureWorldBounds(go, out b))
            Debug.LogFormat("[GuardArt] 模型实测高 {0:F3} m，×{1:F4} → 现高 {2:F2} m，" +
                            "占地 {3:F2}×{4:F2} m，底面 y={5:F3}（地面 {6:F2}）。",
                raw, k, b.size.y, b.size.x, b.size.z, b.min.y, p.position.y);
    }

    // ───────────────────────── 材质 ─────────────────────────

    /// 夜里这个角色只该被"看成一块影子"：制服比背景更黑，鞋和头发再黑一档。
    static void Paint(GameObject go)
    {
        Material coat = Mat("GuardCoat", new Color(0.085f, 0.095f, 0.115f), 0.10f);
        Material boot = Mat("GuardBoot", new Color(0.040f, 0.042f, 0.048f), 0.05f);

        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Material[] ms = rs[i].sharedMaterials;
            if (ms.Length == 0) { rs[i].sharedMaterial = coat; }
            else
            {
                for (int k = 0; k < ms.Length; k++)
                {
                    string n = ms[k] != null ? ms[k].name.ToLower() : "";
                    ms[k] = n.Contains("black") ? boot : coat;
                }
                rs[i].sharedMaterials = ms;
            }

            // 蒙皮网格的包围盒不会跟着动画更新 ⇒ 默认（false）会在角色走到画面边缘时
            // 被视锥剔除掉，表现是"保安走到屏幕边上忽然消失"。
            SkinnedMeshRenderer smr = rs[i] as SkinnedMeshRenderer;
            if (smr != null) smr.updateWhenOffscreen = true;
        }
    }

    static Material Mat(string name, Color c, float smooth)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        Material m = new Material(sh);
        m.name = name;
        // URP/Lit 的主色是 _BaseColor；mainColor 走 [MainColor] 特性，换 Standard 也能设对。
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return m;
    }

    // ───────────────────────── 走路动画 ─────────────────────────

    static void AttachMotion(GameObject go)
    {
        AnimationClip[] all = Resources.LoadAll<AnimationClip>(Folder);
        AnimationClip clip = null;
        for (int i = 0; i < all.Length; i++)
        {
            // 这个 fbx 只有一个 take。换模型时"最长的那段 = 主循环"仍然是最稳的规则。
            if (all[i] != null && (clip == null || all[i].length > clip.length)) clip = all[i];
        }

        if (clip == null || clip.length <= 0.01f)
        {
            Debug.LogWarning("[GuardArt] Resources/" + Folder + " 里读不到可用的动画 —— 保安会走，但腿不动。");
            return;
        }

        NbGuardModel m = go.AddComponent<NbGuardModel>();
        m.Setup(clip);

        if (!_reported)
            Debug.LogFormat("[GuardArt] 走路动画：'{0}'，{1:F2} 秒，按时间手动采样（无 Animator）。",
                clip.name, clip.length);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 手动推进走路动画。
//
// 为什么不用 Animator / Animation 组件：
//   · Animator 必须挂一个 AnimatorController，而 controller **只能在编辑器里创建**，
//     运行时没有任何公开 API 能造一个出来；
//   · Animation 组件只播 legacy 曲线，这个 fbx 是按 Generic 导入的（模型自带走路动画，
//     通常就是这么导的）。改 `clip.legacy = true` 在运行时能不能生效，取决于版本 —— 赌不起。
//
// AnimationClip.SampleAnimation 是公开 API：按时间把曲线采样到整条层级上。没有状态机、
// 没有 controller、不动导入设置，代价是循环和相位都得自己管（也就是下面这十几行）。
// ─────────────────────────────────────────────────────────────────────────────

internal class NbGuardModel : MonoBehaviour
{
    AnimationClip _clip;
    float _t;               // 当前播放时间（秒）
    bool _sampling = true;  // 手动采样还能不能用
    float _phase;           // 兜底起伏的相位

    /// 播放倍率。走路动画本身是 1.6 m/s 左右的步频，而保安巡逻只有 0.95、追人是 2.55~3.45 ——
    /// 不变速的话，追人的时候腿还在慢悠悠迈，人却在地上滑，一眼就看得出"他在加速"是假的。
    internal float Rate = 1f;

    Vector3 _basePos;
    Quaternion _baseRot;
    Vector3 _baseScale;

    // 采样自检：SampleAnimation 在有的版本/导入设置下会**静默地什么都不做**——
    // 不报错、不抛异常，模型就那么以绑定姿势站着。那是最难查的一类失败（看起来像"动画没做好"），
    // 所以这里拿 Hips 当探针：0.6 秒内它一动没动，就在 Console 里说清楚。
    Transform _probe;
    Vector3 _probeLast;
    float _probeT;
    bool _probeDone;

    internal void Setup(AnimationClip clip)
    {
        _clip = clip;
        _basePos = transform.localPosition;
        _baseRot = transform.localRotation;
        _baseScale = transform.localScale;

        // 相位错开：几个保安同时迈同一条腿，一眼就能看出是同一个模型复制出来的。
        _t = Mathf.Repeat(GetInstanceID() * 0.37f, clip.length);

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name.IndexOf("Hips", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            _probe = all[i];
            _probeLast = _probe.position;
            break;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (_clip != null && _sampling)
        {
            _t = Mathf.Repeat(_t + dt * Mathf.Max(0.05f, Rate), _clip.length);
            try
            {
                _clip.SampleAnimation(gameObject, _t);
            }
            catch (System.Exception e)
            {
                // 只报一次；之后退回下面的起伏兜底。
                _sampling = false;
                Debug.LogWarning("[GuardArt] 手动采样失败（" + e.GetType().Name + "：" + e.Message +
                                 "）—— 保安改用地形起伏兜底，腿不会动。");
                Reset();
                return;
            }
            Probe(dt);
            return;
        }

        // 兜底：整体轻微上下起伏 + 一点点前倾。总比一块贴地滑动的雕像强。
        _phase += dt * 7f;
        transform.localPosition = _basePos + new Vector3(0f, Mathf.Abs(Mathf.Sin(_phase)) * 0.015f, 0f);
        transform.localRotation = _baseRot;
    }

    /// 看一眼 Hips 到底有没有被推动。只查一次，查完就沉默。
    void Probe(float dt)
    {
        if (_probeDone || _probe == null) return;

        _probeT += dt;
        if ((_probe.position - _probeLast).sqrMagnitude > 1e-7f)
        {
            _probeDone = true;                       // 动了 ⇒ 采样是有效的
            return;
        }
        _probeLast = _probe.position;

        if (_probeT > 0.6f)
        {
            _probeDone = true;
            Debug.LogWarning("[GuardArt] 手动采样跑完了，但 0.6 秒内 Hips 一点没动 —— " +
                             "这段动画在你的 Unity 版本里没被应用，保安会保持绑定姿势（多半是伸着手臂）。" +
                             "换法：把 fbx 的 Animation Type 改成 Humanoid 并挂 Animator + Controller。");
        }
    }

    /// 换关时把模型摆回初始姿势（采样失败的路径下用得到）。
    internal void Reset()
    {
        _t = 0f;
        transform.localPosition = _basePos;
        transform.localRotation = _baseRot;
        transform.localScale = _baseScale;
    }
}
