// NeighbourhoodGame.cs — 居民区关卡：10 关，猫和奶酪一起变多，被抓一次 -10 血
//
// 装配方式沿用本工程的老规矩：运行时自己 new 出所有物体，场景文件一行不改。
//   ① 读你在场景里摆好的出生点（旧 FirstPersonController 的位置，只要在围墙以内）
//   ② 搭环境（NbBuilder）
//   ③ NbPlayer.Spawn 造一个**全新的**玩家（自带身体和相机；旧的那个整个关掉）
//   ④ 按 NbLevels 的表装载第 1 关：摆 N 块奶酪、放 M 只猫
//   ⑤ 建 HUD（含生命条）与方形小地图
//
// 关卡之间的差别只有四个数（奶酪数 / 猫数 / 猫速 / 视野），全部在 NbLevels.cs 里；
// 这里负责的是**换关**：清掉上一关的物体、按新数字重摆一遍、血补满、人回出生点。
// 血被咬光就停在失败面板上，按 R 重打本关；集满就是过关，按 R 进下一关。
//
// 玩家不再是"场景里那个 FirstPersonController + 运行时补的组件"，而是 NbPlayer.cs 里
// 自己 new 出来的一个物体 —— 旧的任何一处被拖错/改坏都不会再传导到这一关。
//
// 猫的模型/奶酪模型都复用 Resources/GameProps 里现成的资源，以及 CatCheeseSetup 里
// 已经调通过的「按网格实测归一化 + 贴地 + 按骨骼定朝向」那套工具。
//
// 旧玩法（猫吃奶酪）由 CatCheeseGame.cs 里的 `if (NbMode.Enabled) return;` 让位，
// 把 Enabled 改成 false 就能完整回到旧关卡。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

internal static class NbMode
{
    // 必须是 static readonly 而不是 const —— const 会让旧脚本里 `if (Enabled) return;`
    // 后面的代码被判成不可达（CS0162），整个工程编不过。
    internal static readonly bool Enabled = true;
}

internal class NbGame : MonoBehaviour
{
    internal static NbGame I;

    internal Transform Root;
    internal Transform Player;       // 新玩家的身体（原点在脚底）
    internal NbPlayer Rig;           // 新玩家本体：相机、眼高、俯仰都在它身上

    internal int Collected;
    internal bool Finished;
    internal bool Won;
    internal bool Failed;                                    // 血被咬光（和 Won 互斥）

    // ── 关卡与生命 ──
    internal int Level = 1;                                  // 当前关卡 1…NbLevels.Count
    internal int Hp;                                         // 生命值 0…MaxHp
    internal int MaxHp = NbLevels.StartHp;                    // 100：一关最多被咬 10 口
    internal int CheeseNeeded = 1;                            // 本关要带回的奶酪数（= 场上奶酪数）
    internal int CatCount;                                    // 本关猫数（HUD 显示用）

    internal float Invuln;          // 被抓后的无敌时间
    internal float Flash;           // 红闪强度 1 → 0
    internal string Toast = "";
    internal float ToastT;
    internal float Speed;                                  // 实测水平速度 m/s（读数条与小地图共用）
    internal Vector3 Heading = new Vector3(0f, 0f, -1f);   // 小地图箭头：身体朝向（静止时也会变）

    internal readonly List<NbCheese> Cheeses = new List<NbCheese>();
    internal readonly List<NbCat> Cats = new List<NbCat>();
    internal readonly List<NbCheese> Held = new List<NbCheese>();
    internal readonly List<NbGuard> Guards = new List<NbGuard>();

    // ── 后院：曝光值 ──
    // 0…100。被手电照到时按 cfg.Exposure 累积，脱离后按 32/s 回落。满 100 = 被抓一次。
    // 用"累积 + 回落"而不是参考图里那句 "eliminate after fixed amount of time"：
    // 后者在 1.2 m 走道 + 18 m 光锥下会变成"路过就死"，而累积式允许玩家"快速冲过光斑"。
    // 这是有意偏离，不是漏做。
    internal float Exposure;

    /// 现在有没有保安在追你。HUD 拿它决定要不要亮那行 "SPOTTED / SHIFT TO RUN"。
    internal bool Chased;

    /// 这个时间点之前不要弹追击/脱身的提示（刚被撞的那两秒）。
    float _quietUntil;

    // ── 厨房：新鲜度 ──
    // RotLeft 是**全场共用的**一根条：进关开始跑，到 0 时所有还没捡的食物一起烂掉。
    // 共用而不是每块一个计时，是因为玩家在同一屏里要读的是"我还有多久"，不是十个数字。
    internal float RotLeft;
    internal float RotTotal;
    internal bool Rotted;

    // ── 冰箱：门 ──
    // 门是这一关唯一会"动"的东西，所以它得有三个可读状态：关门 / 预警（3 s）/ 开着。
    // **只在开门后 0.45 秒判一次**，不是持续照射 —— 玩家要么已经躲好，要么没有；
    // 中途再冲过去不算数。持续判定会把"擦着门缝跑"这种走位全判死，
    // 而这一关的层高只有 0.51 m，玩家根本没有绕的余地。
    internal const float DoorWarnTime = 3.0f;    // 预警时长：写死，不随关卡变（见 NbLevels.Fridge）
    internal float DoorT;                        // 距下次开门
    float _doorGap;                              // 本关的间隔（MeterUpdate 要拿它算比例）
    float _doorOpenFor;                          // 本关开门时长
    float _doorOpenLeft;                         // 门还开着多久
    bool  _doorHit;                              // 这一次开门是否已经判过
    bool  _doorWarned;                           // 这一次预警是否已经播报过
    float _doorVisual;                           // 门的视觉开合 0…1（平滑，不跟状态机硬跳）

    Vector3 _lastPos;

    // 移动看门狗：只在"按着键却一步没动"时报警一次。
    // 上一轮"满屏纯白 + 无法移动"排查时最缺的就是这个 —— 光看日志根本分不清是没输入、被卡住，
    // 还是画面全白导致看不出在动。
    float _holdT = -1f;
    Vector3 _holdPos;
    bool _movedOnce;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!NbMode.Enabled) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "SampleScene") return;
        if (I != null) return;
        new GameObject("Neighbourhood Game (auto)").AddComponent<NbGame>();
    }

    void Awake()
    {
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Start()
    {
        StartCoroutine(Boot());
    }

    IEnumerator Boot()
    {
        yield return null;          // 让标题页等其它 Bootstrap 先跑完一帧

        // 把"当前场景"写进 NbScene 那组字段（第一次是街区，之后由场景选择页改 Kind）。
        // 这一步必须在 Build / ResolveSpawn 之前 —— 它们全都从这里读边界和出生点。
        NbScene.Select(NbScene.Kind);

        // 出生点：优先用你已经在场景里摆好的那个位置（只要它落在本场景的场地以内）
        Vector3 spawn;
        float spawnYaw;
        ResolveSpawn(out spawn, out spawnYaw);

        Root = new GameObject("World " + NbScene.Code).transform;
        NbScene.Build(Root);

        // 冰箱：把你自己导进来的三个食物包摆进去当商品（纯装饰、无碰撞体）。
        // 必须在 Build 之后 —— 每个位置都是按 NbFridge 的层高 / 前沿算出来的。
        if (NbScene.UsesDoor) NbFoodArt.DecorateFridge(Root);

        // 12 栋楼是编辑器脚本换的模型，碰撞体在那个脚本里算错过一次（转置），
        // 这里按网格本地包围盒重算一遍。后院/厨房没有这 12 栋楼，跑它只会白扫一遍场景。
        if (NbScene.Kind == NbSceneKind.Street) NbColliders.Normalize();

        // 造玩家。这一句之后：场景里那个 FirstPersonController 被整个关掉，
        // 场上只剩一台相机 —— 就是玩家的眼睛。身体和相机都是新造的，不复用场景里的任何东西。
        Rig = NbPlayer.Spawn(spawn, spawnYaw);
        Player = Rig.transform;
        // 冰箱是**走楼梯**上去的（每层 4 级、每级 0.14 m），所以台阶上限要从 0.08 抬到 0.20。
        // 为什么给到 0.20 而不是刚好 0.14：CharacterController 上台阶靠的是"先抬高再前推"，
        // 留 0.06 的余量才不会在帧率抖动的那一帧被判成"前面是墙"（表现是偶发地被台阶顶住）。
        // 冰箱里唯一比 0.20 矮的竖直面就是台阶本身，所以抬高了也不会多爬上去什么。
        // 其它场景必须**显式**设回 0.08 —— 不设的话会带着 0.20 在街上"爬"上路缘和奶酪，
        // 而 0.20 这个数看不出任何异常，只是人会莫名其妙地蹬上花坛。
        Rig.SetStepUp(NbScene.UsesDoor ? 0.20f : 0.08f);
        _home = spawn;
        _homeRot = Quaternion.Euler(0f, spawnYaw, 0f);
        Heading = _homeRot * Vector3.forward;
        _lastPos = spawn;

        // 场景里三盏平行光叠出约 3.1 倍辐照，而楼体是没有贴图的白材质 ⇒ 不重排布光就满屏纯白
        NbLighting.Apply(Rig.Cam, NbScene.Kind);

        // 门光必须在 Apply **之后**造：Apply 会把场上最亮的平行光当主光、其余全部关掉，
        // 而门光平时 intensity = 0 —— 它先存在的话会被判成"无关的平行光"直接 disabled，
        // 之后怎么抬强度都不亮。道理写在 NbFridge.CreateDoorLight 的注释里。
        if (NbScene.UsesDoor) NbFridge.CreateDoorLight(Root);

        // HUD / 小地图先建好，再装载第 1 关 —— 反过来的话，装载时那些 "HideEnd" 之类的刷新
        // 会静默打空（NbHud.I 还是 null），失败/过关面板的初始状态就没人负责了。
        if (NbHud.I == null) NbHud.Create();
        if (NbMap.I == null) NbMap.Create();

        LoadLevel(1);

        // 冰箱：先讲规则再放人。它同时会冻结玩家和**门**（DoorUpdate 里第一句就 return）——
        // 只冻玩家不冻门的话，玩家读完规则的第一秒就是门开的时候。
        if (NbScene.UsesDoor) NbBriefing.Show();

        Debug.LogFormat("[Nb] 开局：玩家出生在 ({0:F1}, {1:F1})，共 {2} 关。", _home.x, _home.z, NbLevels.Count);

        StartCoroutine(SelfCheck());
    }

    // ───────────────────────── 自检 ─────────────────────────
    // 只回答一个问题：标题页收起之后，相机到底站在哪、眼前有没有东西。
    // 上面那行「玩家出生在 (...)」打印的是常量 Nb.PlayerStart，是「打算」不是「结果」——
    // 玩家被别的东西挪走时它照样报得漂亮，所以必须回读 Player.position 本身。

    IEnumerator SelfCheck()
    {
        // 不再用 `while (Time.timeScale < 0.01f)` 等标题页 —— 标题页万一一直没关，
        // 那一行会让**所有诊断一起消失**，从日志上看到的就只是"什么都没有发生"。
        // 现在按真实时间等，并且把"世界还冻着"这件事本身报出来。
        yield return new WaitForSecondsRealtime(0.6f);
        if (Player == null || Rig == null) yield break;

        if (CatCheeseTitle.Showing)
            Debug.LogWarning("[Nb] 自检：标题页还挡在前面（世界冻在 timeScale=0）。点画面 / 按任意键，或等 12 秒自动开始。");

        Vector3 drift = Player.position - _home;
        drift.y = 0f;
        Debug.LogFormat("[Nb] 自检·玩家实际位置 ({0:F2}, {1:F2}, {2:F2})，离出生点 {3:F2} m",
            Player.position.x, Player.position.y, Player.position.z, drift.magnitude);

        // 以前这里会把玩家强行拽回出生点 —— 那等于「在 Play 里把他挪到想看的地方」直接做不到，
        // 而且会把「开机时其实有东西推了他」这条线索一起抹掉。现在只报告，不动手。
        if (drift.magnitude > 5f)
            Debug.LogWarningFormat("[Nb] 玩家离出生点 {0:F1} m —— 是自己挪的就忽略；不是的话，说明开机时被什么东西推了。",
                drift.magnitude);

        Camera cam = Rig != null ? Rig.Cam : null;
        if (cam == null)
        {
            Debug.LogError("[Nb] 玩家相机为空 —— 画面必然是空的。");
            yield break;
        }

        Debug.LogFormat("[Nb] 自检·相机 位置 ({0:F2}, {1:F2}, {2:F2})，朝向 ({3:F2}, {4:F2}, {5:F2})，启用 {6}，清屏 {7}，FOV {8}，掩码 0x{9:X}",
            cam.transform.position.x, cam.transform.position.y, cam.transform.position.z,
            cam.transform.forward.x, cam.transform.forward.y, cam.transform.forward.z,
            cam.enabled, cam.clearFlags, cam.fieldOfView, cam.cullingMask);

        Collider[] nearby = Physics.OverlapSphere(cam.transform.position, 0.08f, ~0, QueryTriggerInteraction.Ignore);
        Debug.LogFormat("[Nb] 自检·相机位置被 {0} 个碰撞体占着{1}", nearby.Length,
            nearby.Length > 0 ? "：" + string.Join(", ", System.Array.ConvertAll(nearby, c => c.name)) : "（正常，没卡在墙里）");

        // 输入链：键盘设备在不在、移动脚本开着没、世界冻着没 —— 这三样任何一样不对，
        // 表现都是"走不动"，但修法完全不同，所以先分开报。
        Debug.LogFormat("[Nb] 自检·输入 键盘={0} 鼠标={1} 光标={2} 可操作={3} timeScale={4:F2} 重力={5:F2} " +
                        "CC.enabled={6} 眼高={7:F2} Pitch={8:F0}° 实测速度={9:F2}",
            Keyboard.current != null, Mouse.current != null, Cursor.lockState,
            Rig != null && Rig.AcceptInput, Time.timeScale, Physics.gravity.y,
            Rig != null && Rig.Cc != null && Rig.Cc.enabled,
            Rig != null && Rig.Cam != null ? Rig.Cam.transform.position.y - Rig.transform.position.y : -1f,
            Rig != null ? Rig.Pitch : 0f, Speed);

        Renderer[] rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int within = 0;
        Renderer closest = null;
        float best = float.MaxValue;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            float d = (rends[i].bounds.center - cam.transform.position).sqrMagnitude;
            if (d < 1600f) within++;
            if (d < best) { best = d; closest = rends[i]; }
        }
        Debug.LogFormat("[Nb] 自检·渲染器 全场 {0} 个，相机 40 m 内 {1} 个；最近「{2}」{3:F1} m",
            rends.Length, within, closest != null ? closest.name : "无",
            closest != null ? Mathf.Sqrt(best) : -1f);

        // 灯光合计：这一项 >1 时，白材质（换模型之后楼体就是白材质）会整片裁到 255 —— 满屏纯白。
        // 所以它不是"美术参数"，是"能不能看见"的判据。
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        int dirN = 0;
        float dirSum = 0f;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null || lights[i].type != LightType.Directional) continue;
            dirN++;
            dirSum += lights[i].intensity;
        }
        Debug.LogFormat("[Nb] 自检·灯光 平行光 {0} 盏、强度合计 {1:F2}（>1 会把白墙推到纯白）；" +
                        "环境光 {2:F2} 均匀色，雾 {3}，相机清屏 {4}",
            dirN, dirSum, RenderSettings.ambientLight.grayscale, RenderSettings.fog, cam.clearFlags);
        if (dirSum > 1.3f)
            Debug.LogWarning("[Nb] 平行光强度合计大于 1.3 —— 白材质会被裁到纯白，检查 NbLighting 有没有跑。");

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas cv = canvases[i];
            Debug.LogFormat("[Nb] 自检·Canvas「{0}」order={1} mode={2} 子物体 {3} 个",
                cv.name, cv.sortingOrder, cv.renderMode, cv.transform.childCount);
        }

        if (within == 0)
            Debug.LogError("[Nb] 相机 40 m 内一个渲染器都没有 —— 相机不在这片街区里，画面空白跟材质无关。");

        ProbeCamera();
    }

    /// 判据：**镜头正前方到底是什么**。
    /// 这里不能用 Physics.Raycast —— Nb.Style 把装饰物（过道、树、灯、围墙压顶）的碰撞体全删了，
    /// 射线会直接穿过去，报"前面什么都没有"，而我们明明贴着一面墙。
    /// 所以改用 Renderer.bounds.IntersectRay：不问物理，直接问"这条视线上有哪些东西的包围盒"。
    void ProbeCamera()
    {
        Camera cam = Rig != null ? Rig.Cam : null;
        if (cam == null) return;
        Vector3 p = cam.transform.position;
        Ray ray = new Ray(p, cam.transform.forward);

        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        Renderer f1 = null, f2 = null, f3 = null;
        float d1 = float.MaxValue, d2 = float.MaxValue, d3 = float.MaxValue;
        int inside = 0;
        string insideNames = "";

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled) continue;
            if (Player != null && r.transform.IsChildOf(Player)) continue;

            Bounds b = r.bounds;
            if (b.Contains(p))
            {
                inside++;
                if (insideNames.Length < 160)
                    insideNames += (insideNames.Length > 0 ? ", " : "") + r.name;
            }

            float hit;
            if (!b.IntersectRay(ray, out hit)) continue;
            if (hit < d1) { d3 = d2; f3 = f2; d2 = d1; f2 = f1; d1 = hit; f1 = r; }
            else if (hit < d2) { d3 = d2; f3 = f2; d2 = hit; f2 = r; }
            else if (hit < d3) { d3 = hit; f3 = r; }
        }

        Debug.LogFormat("[Nb] 自检·正前方最靠前的三样东西：{0} | {1} | {2}",
            f1 != null ? string.Format("{0:F2} m「{1}」包围盒尺寸({2:F1},{3:F1},{4:F1})",
                d1, f1.name, f1.bounds.size.x, f1.bounds.size.y, f1.bounds.size.z) : "无",
            f2 != null ? string.Format("{0:F2} m「{1}」", d2, f2.name) : "无",
            f3 != null ? string.Format("{0:F2} m「{1}」", d3, f3.name) : "无");

        Debug.LogFormat("[Nb] 自检·相机被 {0} 个渲染器的包围盒包着{1}", inside,
            inside > 0 ? "：" + insideNames : "（正常）");

        // 再补一条**物理射线**版本。上面那套用的是 `Renderer.bounds.IntersectRay`，
        // 而它对"射线起点就在盒子内部"是直接返回 false 的 —— 也就是说，**镜头正贴着一面墙时它报"无"**。
        // 这个盲区让上一轮的"整屏一块平涂色"被误判成"前方 14 m 才有东西"。用射线+碰撞体再问一遍。
        if (cam != null)
        {
            RaycastHit[] hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 60f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var sb = new List<string>();
            for (int i = 0; i < hits.Length && sb.Count < 3; i++)
            {
                if (hits[i].collider == null) continue;
                if (Player != null && hits[i].collider.transform.root == Player.root) continue;
                sb.Add(string.Format("{0:F2} m「{1}」", hits[i].distance, hits[i].collider.name));
            }
            Debug.LogFormat("[Nb] 自检·视线上的碰撞体（60 m 内）：{0}",
                sb.Count == 0 ? "一个都没有 —— 前面是空的" : string.Join(" | ", sb.ToArray()));
        }

        if (f1 != null && d1 < 1.0f)
            Debug.LogErrorFormat("[Nb] 镜头 1 m 内就顶着「{0}」—— 除 HUD 外整屏都会是它，跟走不走得动无关。", f1.name);
        if (inside > 0)
            Debug.LogErrorFormat("[Nb] 相机站在 {0} 个物体的包围盒内部（{1}）—— 看到的是它们的背面，整屏一片平涂色。", inside, insideNames);
    }

    // ───────────────────────── 出生点 ─────────────────────────

    /// 本局真正的出生点（脚底坐标）：开机时定一次，之后重开都回这里。
    /// **不能写成 `= Nb.PlayerStart`** —— 字段初始化器在类型加载时跑，那时 NbScene 还没 Select，
    /// 读到的永远是街区坐标，换到后院就会被送回街区正中。Boot 里赋值。
    Vector3 _home = Vector3.zero;
    Quaternion _homeRot = Quaternion.identity;

    /// 出生点对猫来说是一块「安全区」：玩家可以把它摆在任意位置，而纵向通道 A 那只猫的
    /// 巡逻线正好从街区正中穿过 —— 人只要站那儿，就会被反复扑。
    internal Vector3 Home { get { return _home; } }

    /// 决定出生点：**优先用你在场景里摆好的那个位置**（只要它落在围墙以内）。
    ///
    /// 这里读的是场景里那个旧玩家 `FirstPersonController` 的 Transform —— 它马上就会被
    /// NbPlayer.Spawn 整个关掉，但"你摆在哪、朝哪"这个信息必须先取出来用掉。
    void ResolveSpawn(out Vector3 feet, out float yaw)
    {
        Transform old = NbPlayer.FindScenePlayer();
        if (old != null)
        {
            Vector3 p = old.position;
            // 判据换成**当前场景**的边界。换到后院/厨房之后，这个位置几乎必然落在场外，
            // 于是自动退回 NbScene.Start —— 不用为每个场景单独写一份出生点解析。
            if (NbScene.Inside(p, 1f))
            {
                feet = new Vector3(p.x, 0.12f, p.z);
                yaw = old.eulerAngles.y;
                Debug.LogFormat("[Nb] 出生点用你在场景里摆的位置 ({0:F2}, {1:F2})，朝向 {2:F0}°。",
                    feet.x, feet.z, yaw);
                return;
            }
            Debug.LogFormat("[Nb] 场景里的旧玩家在 ({0:F2}, {1:F2}, {2:F2})，不在本场景场地内 ⇒ 出生点改用 {3} 的设定位置 ({4:F1}, {5:F1})。",
                p.x, p.y, p.z, NbScene.Code, NbScene.Start.x, NbScene.Start.z);
        }
        else
        {
            Debug.LogFormat("[Nb] 场景里找不到 FirstPersonController —— 出生点用 {0} 的设定位置。", NbScene.Code);
        }

        feet = NbScene.Start;
        yaw = Quaternion.LookRotation(NbScene.Forward, Vector3.up).eulerAngles.y;
    }

    internal void PlacePlayer() { PlacePlayer(_home, _homeRot); }

    /// 归位：走 NbPlayer.Teleport（关-改-开 CharacterController，否则位置会被它吃掉），
    /// 同时俯仰归零、贴地。旧版那一整套 DetachPlayer / EnsureStanding / HideOwnBody 全部作废了 ——
    /// 新玩家是自己 new 出来的根物体、缩放 1、身上只有 CharacterController，没有"被拖错"的余地。
    internal void PlacePlayer(Vector3 at, Quaternion rot)
    {
        if (Rig == null) return;
        Rig.Teleport(at, rot.eulerAngles.y);
        _lastPos = Rig.transform.position;
        Heading = rot * Vector3.forward;
    }

    // ───────────────────────── 关卡装载 ─────────────────────────

    /// 装载第 n 关：清掉上一关的奶酪和猫，按 NbLevels 的表重新摆一遍，血补满，人回出生点。
    ///
    /// 没有"场景重载"这一步是刻意的 —— 关卡之间只有四个数字不同，重建 12 栋楼和 160 个装饰
    /// 既慢又会把玩家已经熟悉的地标重排一遍。这里只换**会变的东西**：奶酪、猫、计数、血。
    internal void LoadLevel(int n)
    {
        Level = Mathf.Clamp(n, 1, NbLevels.Count);
        NbLevelCfg cfg = NbLevels.Get(Level);

        ClearProps();

        Hp = MaxHp;
        Failed = false;
        Finished = false;
        Won = false;
        Collected = 0;
        Invuln = 3.0f;                  // 开场缓冲：先让你看清这一关的街道，再让猫动起来
        Flash = 0f;
        Toast = "LEVEL " + Level;
        ToastT = 2.6f;

        SpawnCheese(cfg.Cheese);
        SpawnCats(cfg);
        SpawnGuards(cfg);

        Exposure = 0f;
        Chased = false;
        _quietUntil = 0f;
        RotTotal = Mathf.Max(1f, cfg.Rot);
        RotLeft  = RotTotal;
        Rotted   = false;

        // 冰箱的门从头开始计时：每关都给你完整的第一段安全时间，
        // 否则"上一关刚好开着门"会带进新一关，玩家一进来就被照。
        _doorGap     = Mathf.Max(4f, cfg.DoorGap);
        _doorOpenFor = Mathf.Max(0.8f, cfg.DoorOpen);
        DoorT        = _doorGap;
        _doorOpenLeft = 0f;
        _doorHit      = false;
        _doorWarned   = false;
        _doorVisual   = 0f;
        if (NbScene.UsesDoor) NbFridge.SetDoor(0f);

        if (Rig != null)
        {
            PlacePlayer();              // 回出生点：每关都从头开始，不吃上一关的站位便宜
            Rig.AcceptInput = true;
            Rig.LockCursor();
        }
        if (NbHud.I != null) NbHud.I.HideEnd();

        Debug.LogFormat("[Nb] 第 {0}/{1} 关：{2} 块奶酪（全要带回）、{3} 只猫；" +
                        "猫速 巡逻 {4:F2} / 追击 {5:F2}，视野 {6:F0} m；生命 {7}/{8}。",
            Level, NbLevels.Count, CheeseNeeded, CatCount, cfg.Patrol, cfg.Chase, cfg.Sight, Hp, MaxHp);
    }

    /// 删掉上一关的奶酪、光晕、猫。用 Nb.Kill：编辑模式下 Destroy 是不生效的
    /// （会刷一条 "Destroy may not be called from edit mode!" 而物体还在）。
    void ClearProps()
    {
        for (int i = 0; i < Cheeses.Count; i++)
        {
            NbCheese c = Cheeses[i];
            if (c == null) continue;
            if (c.Halo != null) Nb.Kill(c.Halo.gameObject);   // 光晕挂在 Root 下，不是奶酪的子物体
            Nb.Kill(c.gameObject);
        }
        Cheeses.Clear();
        Held.Clear();

        for (int i = 0; i < Cats.Count; i++)
            if (Cats[i] != null) Nb.Kill(Cats[i].gameObject);
        Cats.Clear();

        for (int i = 0; i < Guards.Count; i++)
            if (Guards[i] != null) Nb.Kill(Guards[i].gameObject);
        Guards.Clear();
    }

    // ───────────────────────── 奶酪 ─────────────────────────

    /// 只摆前 count 个奶酪点。Nb.CheeseSpots 已按"离出生点由近到远"排好序，
    /// 所以前几关的奶酪就在家门口，第 9、10 关才要横穿整个街区。
    void SpawnCheese(int count)
    {
        // 街区 / 后院：场上摆几块就要求带回几块（"全部带回"）。
        // 厨房：摆满容量，只要求带回 count 块 —— 多出来的几块是**腐烂的缓冲**。
        // 少了这个缓冲，第 10 关只要有一条走道被猫堵住就必死，那不叫难度，那叫死局。
        int n = NbScene.UsesRot
            ? Mathf.Clamp(NbScene.MaxTargets, 1, NbScene.Targets.Length)
            : Mathf.Clamp(count, 1, NbScene.Targets.Length);

        CheeseNeeded = NbScene.UsesRot ? Mathf.Clamp(count, 1, n) : n;

        for (int i = 0; i < n; i++)
        {
            Vector3 spot = NbScene.Targets[i];

            // 冰箱的每个目标点**自带层高**（y 就是那一层板的顶面）；其它场景全场 y=0，
            // 贴地只是保险（铺装层之间有 3~4 cm 高差）。
            // 这里必须是 KeepsHeight 而不是"总是 Grounded" —— 后者会把六层的点全压回 y=0，
            // 整关塌成一层，而且不报错。
            Vector3 at = NbScene.KeepsHeight ? spot : Nb.Grounded(spot);

            GameObject go;

            // **四个场景的目标物统一是奶酪。** 冰箱以前摆的是几何体瓜果蔬菜，
            // 现在那批东西只当装饰（NbFoodArt 装真模型），目标回归成"把奶酪带回去"。
            go = CatCheeseSetup.SpawnModel("GameProps/Cheese_0001_LOD0",
                new Color(0.95f, 0.78f, 0.20f), "Cheese " + (i + 1));
            CatCheeseSetup.WireCheeseTexture(go);

            if (NbScene.UsesDoor)
            {
                // 冰箱：y 由奶酪点自己带着（那一层的板顶），所以只做归一化 + 坐实，
                // **不能**走 FitAndGround —— 它向下打射线取"最低"那个面，
                // 六层的奶酪会被一路穿到最底层的地板上，整关塌成一层而且不报错。
                // 也不挂碰撞体：冰箱里没有猫，而 0.34 高的盒子在 0.51 净空的层里
                // 只会把人从奶酪旁边顶开（这是当年街区改用 IgnoreCollision 的同一个理由）。
                Seat(go, 0.34f, at);
            }
            else
            {
                CatCheeseSetup.FitAndGround(go, 0.34f, at, "奶酪 " + (i + 1));
                // 实体碰撞体：必须在缩放/贴地之后加，尺寸才对得上模型。
                // 奶酪有 0.34 高，而玩家现在只有 0.30 高、台阶上限 8 cm —— 不豁免的话
                // 老鼠会被一块奶酪硬生生挡在过道里（而且离得够近就自动捡走了，根本不该撞上）。
                // 真正需要这个碰撞体的是猫：NbCat.Steer 靠它"看见"奶酪并绕开。
                Collider chCol = CatCheeseSetup.AddSolidBoxCollider(go);
                if (chCol != null && Rig != null && Rig.Cc != null)
                    Physics.IgnoreCollision(Rig.Cc, chCol, true);
            }

            // 脚下的光晕单独放在世界空间：食物/奶酪根节点的缩放被改过，
            // 挂在它下面会让光晕跟着一起缩放、还随它一起转。
            // y 跟着所在层走 —— 冰箱里如果还写死 0.055，六层的光晕会全掉到底层地板上。
            GameObject halo = Nb.Cyl(Root, "Cheese Halo " + (i + 1),
                new Vector3(at.x, at.y + 0.014f, at.z), 1.15f, 0.012f, new Color(1f, 0.86f, 0.32f));

            NbCheese c = go.AddComponent<NbCheese>();
            c.Index = i;
            c.Halo = halo.transform;
            Cheeses.Add(c);
        }
    }

    /// 把模型"坐到"一个**已知高度**上：按目标高度归一化 → 底面落在 at.y → 水平中心对到 at.xz。
    /// 和 CatCheeseSetup.FitAndGround 的唯一区别是**不找地面**，y 由调用方给。
    /// 冰箱必须这样：FitAndGround 向下打射线并取**最低**那个命中面（它当初是为了避开天花板，
    /// 在"一整块地面"的街区里是对的），放进六层的冰箱就会把每块奶酪都穿到最底层的地板上 ——
    /// 而且一条警告都不会打。
    static void Seat(GameObject go, float height, Vector3 at)
    {
        Bounds b;
        if (CatCheeseSetup.TryMeasureWorldBounds(go, out b) && b.size.y > 1e-5f)
            go.transform.localScale *= height / b.size.y;

        if (!CatCheeseSetup.TryMeasureWorldBounds(go, out b))
        {
            go.transform.position = at;
            return;
        }

        Vector3 p = go.transform.position;
        p.x += at.x - b.center.x;
        p.z += at.z - b.center.z;
        p.y += at.y - b.min.y;
        go.transform.position = p;
    }

    // ───────────────────────── 猫 ─────────────────────────

    /// 猫的高度。玩家是 0.16 m 眼高（NbPlayer.EyeHeight）的老鼠，猫必须够大才成立：
    /// 0.95 m 高、体长约 1.8 m —— 站在 5.1 m 宽的过道里就是一辆堵路的车，
    /// 抬头看它的时候，头和肩膀会顶到画面外。这个数字和眼高是一套比例，别单独改。
    internal const float CatHeight = 0.95f;

    /// 摆前 cfg.Cats 只猫，速度/视野全部按这一关的表给。
    /// 路线是**按段切开**的（见 Nb.CatRoutes），前 n 条互不重叠，所以猫多起来也不会叠在一起走。
    void SpawnCats(NbLevelCfg cfg)
    {
        // 下限从 1 改成 0：冰箱那一关**一只猫都没有**（用户明确要求），
        // 而 Clamp(x, 1, …) 会把 0 悄悄变成 1 —— 一只凭空出现的猫站在冰箱里，还不报错。
        int n = Mathf.Clamp(cfg.Cats, 0, Mathf.Max(0, NbScene.Routes.Length));
        CatCount = n;

        if (n <= 0)
        {
            Debug.Log("[Nb] 本场景没有猫。");
            return;
        }

        for (int i = 0; i < n; i++)
        {
            Vector3[] route = NbScene.Routes[i];
            GameObject cat = CatCheeseSetup.SpawnModel("GameProps/Cat walk",
                new Color(0.16f, 0.12f, 0.10f), "Cat " + (i + 1) + " (auto)");
            CatCheeseSetup.OrientCatByBones(cat, Vector3.forward);
            Bounds box = CatCheeseSetup.FitAndGround(cat, CatHeight, Nb.Grounded(route[0]), "猫 " + (i + 1));
            CatCheeseSetup.PlayWalkAnimation(cat);

            NbCat c = cat.AddComponent<NbCat>();
            c.Route = route;
            c.Speed = cfg.Patrol;
            c.ChaseSpeed = cfg.Chase;
            c.SightDist = cfg.Sight;
            // 扫掠/视线/抓人距离全部按**实测体型**给：猫从 0.5 m 长到 0.95 m 高以后，
            // 还沿用写死的 0.17 半径 = 猫的鼻子能捅进墙里、爪子却有半个身子够不到人。
            c.ConfigureBody(box, CatHeight);

            // 后院最后一只改成**埋伏**：蹲在路线上不动、也不巡逻，玩家进 2.5 m 才起身。
            // 参考里的 "Cat ambush: some cats will hide in the dark and sneakily attack players
            // when they are not paying attention." —— 它惩罚的是"低头看小地图"的那两秒。
            c.Ambush = NbScene.UsesGuards && i == n - 1;

            Cats.Add(c);
        }
    }

    /// 保安的**地盘**段号（巡逻区和猫共用 NbScene.Routes 这套路线）。
    /// 现在保安不再沿线段来回走，而是拿这条线段当轴线、在两侧 ±7 m 的带子里随机游走
    /// （见 NbGuard.PickWaypoint）—— 所以这里挑的是"谁守哪一带"，不是"他走哪条线"。
    ///
    /// 选段的理由没变：1 号沿空地北缘、5 号横穿空地南缘、2 号直穿空地中央 ——
    /// 三条正好把"必须横穿的那块空地"封成三层；3 号是西侧纵线，第 4 关起加进来，
    /// 让西半边也有灯在走（前三条都压在中轴和东西横线上，西侧一直是个稳定的暗角）。
    ///
    /// **顺序是有约束的：2 号必须排在最后。** 它的起点 (72, −46) 离玩家出生点 (72, −54)
    /// 只有 8 m，而手电射程 18 m、中途没有遮挡 —— 它一旦上场，玩家一出生就站在光里。
    /// （游走本来也不许进出生点 8 m 内，但那只是兜底，不是可以松掉这条约束的理由。）
    /// 所以让它在第 8 关（4 人）才出现，前七关都不在。这条约束别在调顺序时弄丢。
    static readonly int[] GuardLanes = { 1, 5, 3, 2 };

    void SpawnGuards(NbLevelCfg cfg)
    {
        int n = Mathf.Clamp(cfg.Guards, 0, GuardLanes.Length);

        // 追人速度：2.55 → 3.45。**上限必须低于玩家跑步 3.91** —— 那是这一关唯一的解，
        // 也是整个游戏对玩家唯一的承诺（按住 Shift 一定跑得掉）。
        float chase = Mathf.Lerp(2.55f, 3.45f, (Level - 1) / (float)Mathf.Max(1, NbLevels.Count - 1));

        for (int i = 0; i < n; i++)
        {
            int lane = GuardLanes[i] % NbScene.Routes.Length;
            NbGuard g = NbGuard.Spawn(Root, NbScene.Routes[lane], 0.95f, cfg.Sweep, cfg.Exposure, chase);
            Guards.Add(g);
        }

        if (n > 0)
        {
            // 路线号按**实际上场人数**拼：写死 3 个下标的话，队伍一扩就会漏报第 4 条，
            // 而日志是排查"为什么某条路线上没人"的唯一线索。
            string lanes = "";
            for (int i = 0; i < n; i++) lanes += (i == 0 ? "" : "/") + GuardLanes[i];

            Debug.LogFormat("[Nb] 保安 {0} 名（地盘 {1}），手电 18 m、半角 11°、扫灯节奏 {2:F2}；" +
                            "在自己的地盘里随机游走（航点 5~16 m、到点站住 1~3 秒环顾），" +
                            "曝光 {3:F0}/s ⇒ 站在光里 {4:F1} 秒被抓；被照到会以 {5:F2} m/s 追人" +
                            "（玩家跑步 3.91）。",
                n, lanes, cfg.Sweep, cfg.Exposure, 100f / Mathf.Max(1f, cfg.Exposure), chase);
        }
    }

    // ───────────────────────── 每帧 ─────────────────────────

    void Update()
    {
        var kb = Keyboard.current;

        // 先把"能不能动"的三个开关钉一遍，再谈玩法（标题页期间会自动跳过）
        EnsurePlayable();

        if (Finished)
        {
            // 过关 / 失败都停在这里等一个 R：过关进下一关，失败重打本关，通关之后从第 1 关重来。
            // 不自动跳关是刻意的 —— 被抓死和过关都用同一块面板，自动推进会让"我刚才是不是死了"
            // 变成一个需要回想的问题。
            if (kb != null && kb.rKey.wasPressedThisFrame) NextOrRetry();
            return;
        }
        if (Player == null) return;

        if (Invuln > 0f) Invuln -= Time.deltaTime;
        if (Flash > 0f) Flash = Mathf.Max(0f, Flash - Time.deltaTime * 1.6f);
        if (ToastT > 0f) ToastT -= Time.deltaTime;

        TrackHeading();
        CollectCheck();
        ThreatUpdate();
        MoveWatchdog(kb);
    }

    // ───────────────────────── 场景专属威胁 ─────────────────────────

    void ThreatUpdate()
    {
        if (NbScene.UsesGuards) GuardUpdate();
        if (NbScene.UsesRot)    RotUpdate();
        if (NbScene.UsesDoor)   DoorUpdate();
        MeterUpdate();
    }

    /// 底部那根条每帧刷一次。颜色本身就是读数：
    /// 曝光 金 → 红（越红越接近被抓），新鲜度 绿 → 红（越红越快烂完）。
    void MeterUpdate()
    {
        NbHud hud = NbHud.I;
        if (hud == null) return;

        if (NbScene.UsesGuards)
        {
            float t = Mathf.Clamp01(Exposure / 100f);
            hud.SetMeter("EXPOSURE", t,
                Color.Lerp(new Color(1f, 0.84f, 0.35f), new Color(1f, 0.30f, 0.22f), t));
        }
        else if (NbScene.UsesRot)
        {
            float t = RotTotal > 0f ? Mathf.Clamp01(RotLeft / RotTotal) : 0f;
            hud.SetMeter("FRESH", t,
                Color.Lerp(new Color(0.92f, 0.25f, 0.20f), new Color(0.55f, 0.82f, 0.42f), t));
        }
        else if (NbScene.UsesDoor)
        {
            // 三种读数：门正开着 / 3 秒预警（条在倒数）/ 平时（条是"距下次开门"的比例）。
            // 预警和平时分开，是因为这两件事对玩家的含义完全不同 ——
            // 平时那条是"我还能跑多久"，预警那条是"我还能跑几步"。
            if (_doorOpenLeft > 0f)
            {
                hud.SetMeter("DOOR  OPEN  -  STAY  DOWN", 1f, new Color(1f, 0.95f, 0.78f));
            }
            else if (DoorT <= DoorWarnTime)
            {
                float t = Mathf.Clamp01(DoorT / DoorWarnTime);
                hud.SetMeter("DOOR  IN  " + Mathf.CeilToInt(Mathf.Max(0f, DoorT)) + "  -  HIDE", t,
                    Color.Lerp(new Color(1f, 0.26f, 0.18f), new Color(1f, 0.88f, 0.42f), t));
            }
            else
            {
                float t = _doorGap > 0.1f ? Mathf.Clamp01(DoorT / _doorGap) : 0f;
                hud.SetMeter("NEXT  DOOR  " + Mathf.CeilToInt(DoorT), t,
                    new Color(0.56f, 0.80f, 1f));
            }
        }
        else
        {
            hud.SetMeter(null, 0f, Color.white);
        }
    }

    /// 后院：把这一帧"有没有人看见我"折成一个曝光值。
    ///
    /// 为什么是累积而不是"进光就死"：光锥 18 m 长、保安还在扫，玩家经常会**擦着光斑边缘**跑过去。
    /// 一刀切的判定会把这种走位全部判死，逼人只敢贴墙 —— 而贴墙正是保安最喜欢的行为。
    /// 累积 + 每秒 32 的回落，等于告诉玩家：冲过去可以，但别停。
    void GuardUpdate()
    {
        if (Finished) return;

        float rate = 0f;
        bool chasing = false;
        for (int i = 0; i < Guards.Count; i++)
        {
            NbGuard g = Guards[i];
            if (g == null) continue;
            if (g.Chasing) chasing = true;
            // 被抓后的 3 秒无敌里，光也不该继续烧血
            if (g.Sees && Invuln <= 0f) rate += g.Damage;
        }

        // 从"没人发现"变成"被追"的那一帧要把话说明白。
        // 玩家第一次的本能是继续走（2.30 m/s），而追他的人比走路快 ——
        // 所以这行提示不是装饰，是机制的一部分：SHIFT 是唯一的解。
        bool was = Chased;
        if (chasing != was)
        {
            if (chasing)
            {
                if (Time.time >= _quietUntil)
                {
                    Toast = "SPOTTED  -  SHIFT  TO  RUN";
                    ToastT = 2.2f;
                    Debug.Log("[Nb] 被手电照到：保安开始追击。跑到 5 m 之外的掩体后面才能脱身。");
                }
            }
            else if (Time.time >= _quietUntil)
            {
                Toast = "YOU LOST HIM";
                ToastT = 1.6f;
                Debug.Log("[Nb] 甩掉了保安：光被掩体挡住且距离 5 m 以上。");
            }
        }
        Chased = chasing;

        if (rate > 0f)
        {
            Exposure = Mathf.Min(100f, Exposure + rate * Time.deltaTime);
            if (Exposure >= 100f) BustedByGuard();
        }
        else
        {
            Exposure = Mathf.Max(0f, Exposure - 32f * Time.deltaTime);
        }
    }

    /// 被追到（保安撞上你）。和"站在光里被烧满"分开结算：
    ///   · 被撞：立刻 -10，所有人回路线起点 —— 否则他会在原地继续顶着你，无敌一过又抓一次；
    ///   · 烧满：也是 -10，但那是"你站着不动"的结果，人不用被搬走。
    /// 玩家留在原地（不传送）：院子太大，传送会让人彻底失去方位感。
    internal void CaughtByGuard(NbGuard g)
    {
        if (Finished || Invuln > 0f) return;

        Invuln = 3.0f;
        Flash = 1f;
        Exposure = 0f;
        Hp = Mathf.Max(0, Hp - NbLevels.BiteDamage);

        // 接下来两秒别再弹 "YOU LOST HIM" —— 人刚被撞，说"甩掉了"是在骗人。
        _quietUntil = Time.time + 2.5f;

        Toast = "CAUGHT  BY  THE  PATROL";
        ToastT = 2.4f;

        if (Rig != null) Rig.Nudge(g != null ? g.transform.position : Player.position - Player.forward, 1.8f);
        for (int i = 0; i < Guards.Count; i++)
            if (Guards[i] != null) Guards[i].ResetToStart();

        Debug.LogFormat("[Nb] 被保安追到：生命 {0}/{1}，玩家在 ({2:F1}, {3:F1})，{4} 名保安已回路线起点。",
            Hp, MaxHp, Player.position.x, Player.position.z, Guards.Count);

        if (Hp <= 0) { Hp = 0; Fail(); }
    }

    void BustedByGuard()
    {
        Exposure = 0f;
        Invuln = 3.0f;
        Flash = 1f;
        Hp -= NbLevels.BiteDamage;

        Toast = "SPOTTED!  MINUS 10 HP";
        ToastT = 2.4f;

        // 保安回路线起点、玩家留在原地：院子太大，传送会让人彻底失去方位感，
        // 而且"我明明还站在这儿"比"我被搬走了"更容易读懂刚刚发生了什么。
        for (int i = 0; i < Guards.Count; i++)
            if (Guards[i] != null) Guards[i].ResetToStart();

        Debug.LogFormat("[Nb] 被保安手电抓到：生命 {0}/{1}，玩家在 ({2:F1}, {3:F1})，已把 {4} 名保安送回路线起点。",
            Hp, MaxHp, Player.position.x, Player.position.z, Guards.Count);

        if (Hp <= 0) { Hp = 0; Fail(); }
    }

    /// 厨房：一根全场共用的新鲜度条。归零时还没捡的全部烂掉 ——
    /// 已经捡到手的不受影响（那是"吃掉了"，不是"还摆着"）。
    void RotUpdate()
    {
        if (Finished || RotLeft <= 0f) return;

        RotLeft -= Time.deltaTime;
        if (RotLeft > 0f) return;

        RotLeft = 0f;
        Rotted = true;

        for (int i = 0; i < Cheeses.Count; i++)
        {
            NbCheese c = Cheeses[i];
            if (c == null || c.Taken) continue;
            if (c.Halo != null) c.Halo.gameObject.SetActive(false);
            c.gameObject.SetActive(false);
            c.Rotted = true;
        }

        Toast = "IT ALL SPOILED";
        ToastT = 2.6f;

        // 需求没凑够 = 这一关已经不可能完成，直接判失败。
        // 让它"继续走但永远赢不了"是更糟的处理：玩家会把剩下的十分钟花在找不存在的东西上。
        if (Collected < CheeseNeeded)
        {
            Debug.LogFormat("[Nb] 食物腐烂：{0}/{1}，不够 {2} 块 ⇒ 第 {3} 关失败。",
                Collected, Cheeses.Count, CheeseNeeded, Level);
            Fail();
        }
        else
        {
            Debug.LogFormat("[Nb] 食物腐烂，但已经够了（{0}/{1}）。", Collected, CheeseNeeded);
        }
    }

    // ───────────────────────── 冰箱：门 ─────────────────────────

    /// 门的三个状态：关门（倒计时）/ 预警（3 秒，门缝漏光）/ 开着（光灌进来，判一次）。
    ///
    /// 为什么只在开门 0.45 秒之后判**一次**，而不是持续照射：
    /// 这一关层高只有 0.51 m、掩体就摆在旁边，玩家要么已经在掩体后面、要么还在空地上，
    /// 中间并不存在"擦着光边跑过去"这种走位。持续判定只会做出一件事 ——
    /// 把"我明明躲好了"判成没躲好（掩体只挡住了身体的一部分高度，射线却总有擦边的角度）。
    void DoorUpdate()
    {
        if (Finished || Player == null) return;

        // 教学面板还开着的时候门不走。玩家在读规则、倒计时在跑，读完规则先挨一下 ——
        // 这不是"难"，这是骗人。
        if (NbBriefing.Showing) return;

        float dt = Time.deltaTime;

        if (_doorOpenLeft > 0f)
        {
            _doorOpenLeft -= dt;

            if (!_doorHit && _doorOpenLeft <= _doorOpenFor - 0.45f)
            {
                _doorHit = true;
                if (Invuln <= 0f && !IsHidden()) BurnInDoorLight();
            }

            if (_doorOpenLeft <= 0f)
            {
                _doorOpenLeft = 0f;
                // **必须在这里把间隔重新装上。** 少了这一句，DoorT 还是 0，
                // 下一帧立刻又满足 "DoorT <= 0" ⇒ 门关上的同一帧就又开了，
                // 表现是"门再也关不上"，而且不报错、HUD 的倒计时一直显示 0。
                DoorT = _doorGap;
                _doorWarned = false;

                // 关门那一刻如果玩家跑到门外的厨房地板上，把他收回来。
                // 不收的话他就被封在冰箱外面了 —— 食物全在里面，而"下一次开门"要等 8~15 秒，
                // 这一段他只能对着门站着，而画面上没有任何东西告诉他这是暂时的。
                if (Player.position.z > NbFridge.ZMax - 0.30f)
                {
                    Rig.Teleport(new Vector3(Player.position.x, Player.position.y, NbFridge.ZMax - 1.3f), Rig.Yaw);
                    Debug.Log("[Nb] 关门时玩家在门外，已收回冰箱内。");
                }
            }
        }
        else
        {
            DoorT -= dt;

            if (!_doorWarned && DoorT <= DoorWarnTime)
            {
                _doorWarned = true;
                Toast = "THE DOOR IS OPENING";
                ToastT = 1.7f;
                Debug.LogFormat("[Nb] 预警：{0:F1} 秒后冰箱门打开 —— 现在要躲到掩体后面。", DoorT);
            }

            if (DoorT <= 0f)
            {
                DoorT = 0f;
                _doorOpenLeft = _doorOpenFor;
                _doorHit = false;
                Debug.LogFormat("[Nb] 冰箱门打开（{0:F1} s）。玩家在 ({1:F1}, {2:F1}, y={3:F2})，{4}。",
                    _doorOpenFor, Player.position.x, Player.position.z, Player.position.y,
                    IsHidden() ? "藏在掩体后面" : "暴露在空地上");
            }
        }

        // 视觉：开着 → 全开；预警 → 门缝里一条光，随着倒计时一下一下地跳；
        // 平时 → 关死。
        // 预警的 0.11 不是随手写的：门光最大 1.9，0.11 ⇒ 约 0.21 的强度，
        // 刚好让玩家**脚下的地面亮起来**（平行光有 −0.47 的 y 分量），
        // 人不用回头也知道背后那扇门在动。再低就只有屏幕底部那根条在提示了。
        // 不能跟状态机硬跳 —— 否则关门那一帧门板会"啪"地瞬移回原位。
        float target = _doorOpenLeft > 0f ? 1f
                     : DoorT <= DoorWarnTime ? 0.11f + 0.05f * Mathf.Sin(Time.time * 14f)
                     : 0f;
        _doorVisual = Mathf.MoveTowards(_doorVisual, target, dt * (target > 0.5f ? 9f : 3.2f));
        NbFridge.SetDoor(_doorVisual);
    }

    /// 门开着时，玩家是不是被东西挡住了。
    ///
    /// 从门口内侧、**玩家所在那一层的高度**朝玩家打一条粗射线，看中间有没有碰撞体。
    /// 三个必须排掉的东西：
    ///   · 搁板（名字以 "Shelf" 开头）—— 它是水平的，光从门平着进来撞不到它，
    ///     但射线会擦到板的边缘。不排掉的话，玩家只要站在板后面（哪怕板在脚下）就永远安全。
    ///   · 商品/装饰（Ignore Raycast 层）—— 见下面的掩码注释，这一条最要命。
    ///   · 玩家自己。
    bool IsHidden()
    {
        if (Player == null) return false;

        Vector3 p = Player.position;
        Vector3 src = new Vector3(NbFridge.CX, p.y + 0.13f, NbFridge.ZMax - 0.25f);
        Vector3 dst = new Vector3(p.x, p.y + 0.15f, p.z);
        Vector3 d = dst - src;
        float dist = d.magnitude;

        if (dist < 0.40f) return false;      // 贴着门口 = 门一开一定被看见

        // **掩码不能写 ~0。** 商品现在是实心的（每件一个 BoxCollider，见 NbFoodArt.AddCollider），
        // 它们留在 Ignore Raycast 层，而 `~0` 是"所有层都算"—— 全在的话，冰箱里 470 多件
        // 商品随便哪一件挡在门口和你之间，你就"藏好了"。门一开，整个货架区都是安全区，
        // 这一关就只剩"走到架子边上站着"。
        // DefaultRaycastLayers = 除 Ignore Raycast 以外的所有层 ⇒ 商品不算遮挡，掩体/楼梯/墙照算。
        RaycastHit[] hits = Physics.SphereCastAll(src, 0.05f, d / dist, dist,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (c.transform.IsChildOf(Player)) continue;
            string n = c.name;
            // 结构件不算掩体：搁板（水平光撞不到，但粗射线会擦边）、楼梯（实心方块）。
            // 楼梯这条**必须排除**：它每层都是 0.56 m 高的实心块，正好横在门口与玩家之间，
            // 不排除的话每层中间那 8 m 会变成永久安全区，"躲掩体"这一关就没了。
            if (!string.IsNullOrEmpty(n) && (n.StartsWith("Shelf") || n.StartsWith("Step"))) continue;
            if (hits[i].distance < dist - 0.10f) return true;
        }
        return false;
    }

    /// 被门光照到：-10 血、红闪、2.6 秒无敌。
    /// **不传送、不弹开** —— 玩家是自己没躲好，位置本身没问题；
    /// 把他搬走反而会让他分不清"我是被门照到，还是又被抓了一次"。
    void BurnInDoorLight()
    {
        Invuln = 2.6f;
        Flash = 1f;
        Hp = Mathf.Max(0, Hp - NbLevels.BiteDamage);

        Toast = "THE DOOR OPENED ON YOU!  MINUS 10 HP";
        ToastT = 2.4f;

        Debug.LogFormat("[Nb] 被门光抓到：生命 -{0} → {1}/{2}，玩家在 ({3:F1}, {4:F1}, y={5:F2})。",
            NbLevels.BiteDamage, Hp, MaxHp, Player.position.x, Player.position.z, Player.position.y);

        if (Hp <= 0) Fail();
    }

    // ───────────────────────── 「为什么动不了」的三个开关 ─────────────────────────

    /// 这一关最容易被误诊的一种故障：世界被冻在 timeScale=0，或者可操作状态 / CharacterController
    /// 被关掉了。三者都不报错、画面照常，唯一的症状就是"按了没反应"。
    /// 这里每帧钉一遍，任一项不对就自己修回来，并留下一条能查的日志。
    void EnsurePlayable()
    {
        // 标题页 / 场景选择页期间本来就该冻着。
        // **这里必须把选择页也算进去**：这个方法是"每帧把开关钉回可操作"，
        // 少列一个遮罩页，玩家就会在选择卡片底下走来走去 —— 而且完全看不到，
        // 因为选择页是不透明的。这种"看不见的移动"比卡住更难查。
        if (CatCheeseTitle.Showing || NbSceneSelect.Showing || NbBriefing.Showing) return;

        if (Time.timeScale < 0.01f)
        {
            Time.timeScale = 1f;
            Debug.LogWarning("[Nb] 世界被冻在 timeScale=0，但标题页已经不在了 —— 已自动放开。");
        }
        if (Finished) return;
        if (Rig == null) return;

        if (!Rig.AcceptInput)
        {
            // 新玩家的移动读的是 unscaledDeltaTime，timeScale=0 也拦得住它，
            // 所以"能不能动"完完全全只由这一个开关决定 —— 它掉了就什么都不动。
            Rig.AcceptInput = true;
            Debug.LogWarning("[Nb] 玩家被设成不可操作（标题页没交还？），已自动恢复。");
        }
        if (Rig.Cc != null && !Rig.Cc.enabled)
        {
            Rig.Cc.enabled = true;
            Debug.LogWarning("[Nb] CharacterController 被关掉了，已自动打开。");
        }
    }

    /// 判据：按着 WASD 持续 0.8 秒、位移 < 8 cm ⇒ 不是"没按键"，是走不动。
    /// 计时必须用 unscaledDeltaTime —— 如果 timeScale 被冻在 0，用 deltaTime 的话这个看门狗自己也不会触发。
    void MoveWatchdog(Keyboard kb)
    {
        bool any = kb != null && (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed ||
                                  kb.upArrowKey.isPressed || kb.downArrowKey.isPressed ||
                                  kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed);
        if (!any)
        {
            _holdT = -1f;
            return;
        }

        if (_holdT < 0f)
        {
            _holdT = 0f;
            _holdPos = Player.position;
            return;
        }

        _holdT += Time.unscaledDeltaTime;
        if (_holdT < 0.8f) return;

        _holdT = -1f;
        float moved = Nb.FlatDist(Player.position, _holdPos);
        if (moved >= 0.08f)
        {
            if (!_movedOnce)
            {
                _movedOnce = true;
                Debug.LogFormat("[Nb] 玩家已经能动：0.8 秒走出 {0:F2} m（输入 → CharacterController 这条链是通的）。", moved);
            }
            return;
        }

        CharacterController cc = Player.GetComponent<CharacterController>();

        // 顺手量一下"身上贴着谁" —— 这是「输入通了但一步没动」最常见的原因，
        // 而且它自己是不会报错的：被自己的子物体顶住、站在别人的盒子里，都只是"不动"。
        float r = cc != null ? cc.radius : 0.3f;
        float h = cc != null ? Mathf.Max(cc.height, r * 2f) : 1.8f;
        Vector3 mid = Player.position + Vector3.up * (h * 0.5f);
        Collider[] hits = Physics.OverlapCapsule(mid + Vector3.up * (h * 0.5f - r),
                                                mid - Vector3.up * (h * 0.5f - r),
                                                r * 0.95f, ~0, QueryTriggerInteraction.Ignore);
        var names = new List<string>();
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;
            if (hits[i].transform.root == Player.root) continue;   // 自己身上的一律不算
            names.Add(hits[i].name);
        }

        Debug.LogWarningFormat(
            "[Nb] 按着移动键 0.8 秒，玩家只挪了 {0:F3} m —— 不是输入没进来。" +
            "timeScale={1:F2}，可操作={2}，cc.enabled={3}，光标={4}，位置 ({5:F2},{6:F2},{7:F2})，" +
            "身上贴着 {8} 个碰撞体{9}",
            moved, Time.timeScale, Rig != null && Rig.AcceptInput, cc != null && cc.enabled,
            Cursor.lockState, Player.position.x, Player.position.y, Player.position.z,
            names.Count, names.Count > 0 ? "（" + string.Join(", ", names.ToArray()) + "）" : "");
    }

    /// 小地图箭头 = **身体朝向**，不是「上一帧往哪走」。
    ///
    /// 旧版取真实位移方向：站着不动、只转鼠标时位移恒为 0，箭头就冻在最后一次
    /// 移动的方向上 —— 明明面朝北，小地图上却指着东；而玩家看小地图时第一个
    /// 问题恰恰是"我面朝哪"。位移方向只在**移动时**才有意义，作为朝向来源是错的。
    ///
    /// 现在直接取 `Rig.Facing`（由 Yaw 得来，转身/静立转鼠标都会变）。
    /// 会不会因此"侧走时箭头不横过来"？会 —— 但这也正是小地图的标准语义：
    /// 箭头回答"我朝哪"，画面本身回答"我在往哪挪"。
    void TrackHeading()
    {
        Vector3 d = Player.position - _lastPos;
        d.y = 0f;
        float dist = d.magnitude;
        // 速度单独记一份：读数条上直接显示"你这一帧真的动了多少"。
        // 它比任何日志都直接 —— 画面均匀时人眼分不出"没动"和"看不出在动"，但 v 会。
        // 速度直接用玩家自己算的那一份（它按 unscaledDeltaTime 算）：
        // 世界被冻在 timeScale=0 时，用 Time.deltaTime 算出来永远是 0 —— 那正好是
        // 「明明在走，读数条却显示 0」的来源，也是这一轮反复误判的推手。
        Speed = Rig != null ? Rig.Speed : (Time.deltaTime > 0.0001f ? dist / Time.deltaTime : 0f);

        if (Rig != null)
        {
            Vector3 f = Rig.Facing;
            if (f.sqrMagnitude > 1e-6f) Heading = f;
        }
        _lastPos = Player.position;
    }

    void CollectCheck()
    {
        for (int i = 0; i < Cheeses.Count; i++)
        {
            NbCheese c = Cheeses[i];
            if (c == null || c.Taken || c.Rotted) continue;
            // 捡取半径按**老鼠的体型**给：0.62 m ≈ 6 个身位。
            // 原来 1.15 m 是给成年人（1.8 m 高）用的，对 0.3 m 的老鼠等于"离着两米就吸走了"，
            // 奶酪的 halo 直径正好 1.15 —— 现在必须踩进光晕里才算捡到。
            if (Nb.FlatDist(c.transform.position, Player.position) > 0.62f) continue;

            // 冰箱是多层的，而 FlatDist 只算平面距离 —— 不卡高度的话，
            // 站在第 4 层能把**正下方**第 1 层的食物一起捡走（平面距离 0）。
            // 0.45 是刻意的：它小于层高 0.56（隔层一定捡不到），又大于同层内的任何高差。
            if (NbScene.KeepsHeight &&
                Mathf.Abs(c.transform.position.y - Player.position.y) > 0.45f) continue;

            c.Taken = true;
            if (c.Halo != null) c.Halo.gameObject.SetActive(false);
            c.gameObject.SetActive(false);
            Held.Add(c);

            Collected++;
            Toast = Collected >= CheeseNeeded ? "THAT IS ENOUGH" : (CheeseNeeded - Collected) + " MORE TO GO";
            ToastT = 1.3f;

            if (Collected >= CheeseNeeded) Win();
        }
    }

    // ───────────────────────── 被抓 / 胜负 ─────────────────────────

    internal void Caught(NbCat cat)
    {
        if (Finished || Invuln > 0f) return;

        Invuln = 3.5f;
        Flash = 1f;

        // 每被咬一口 -10。生命条是这一关真正的资源：咬满 10 口就整关重来（LoadLevel 会把血补满）。
        // 掉一块奶酪的老规矩也保留 —— 两种惩罚指向不同：血说"别被追上"，奶酪说"被追上要再跑一趟"。
        Hp = Mathf.Max(0, Hp - NbLevels.BiteDamage);

        if (Held.Count > 0)
        {
            // 掉一块在"被抓的地方"，重新变得可捡 —— 不销毁物体，计数和场景永远对得上
            NbCheese c = Held[Held.Count - 1];
            Held.RemoveAt(Held.Count - 1);
            Collected--;

            // 掉在**身后** 0.9 m，不是身前。理由很硬：这一关是"全部带回"，
            // 一块落进楼体里的奶酪 = 这一关永远打不通（而身前 0.9 m 正好是撞墙时最容易被推进去的那一格）。
            // 身后是你一秒钟前刚走过的地面，一定站得住；同时它在 0.62 m 捡取半径之外，
            // 所以不是"立刻又捡回来"—— 你得转身，而猫就在旁边。
            Vector3 drop = Nb.Grounded(Player.position - Player.forward * 0.9f);
            c.transform.position = drop + Vector3.up * 0.05f;
            if (c.Halo != null)
            {
                c.Halo.position = new Vector3(drop.x, 0.055f, drop.z);
                c.Halo.gameObject.SetActive(true);
            }
            c.Taken = false;
            c.gameObject.SetActive(true);
            Toast = "CAUGHT!  MINUS 10 HP  AND  A CHEESE";
        }
        else
        {
            Toast = "CAUGHT!  MINUS 10 HP";
        }
        ToastT = 2.4f;

        // **不再把人搬回出生点 —— 这正是"永远动不了"的根源。**
        // 旧写法：被抓住 → PlacePlayer() 传送回起点 → 猫的 ResetChase() 只是"换了个路点索引"、
        // 猫本人还站在原地 → 无敌一过同一只猫立刻又扑上来。日志里连着二十多条
        // 「被 Cat 1 抓住」全是同一处、同一只猫，玩家感受到的就是"按键没用"。
        // 现在：只把人从猫的方向弹开 2.2 m，猫被真的挪到路线远端（NbCat.ResetChase(true)）。
        Knock(cat != null ? cat.transform.position : Player.position - Player.forward);
        // 只有抓人的那一只被搬回路线远端；其余就地放弃追击（全搬走会让人看见一片猫同时瞬移）。
        for (int i = 0; i < Cats.Count; i++)
            if (Cats[i] != null) Cats[i].ResetChase(Cats[i] == cat);
        Debug.LogFormat("[Nb] 被「{0}」抓住：生命 -{1} → {2}/{3}；弹开、猫回路线远端、无敌 {4:F1} s。玩家仍在 ({5:F1}, {6:F1})，本关奶酪 {7}/{8}。",
            cat != null ? cat.name : "猫", NbLevels.BiteDamage, Hp, MaxHp, Invuln,
            Player.position.x, Player.position.z, Collected, CheeseNeeded);

        // 血光就结束这一关。放在弹开之后：玩家能看见自己是被哪只猫顶到墙角的，
        // 而不是"画面一闪、忽然就失败了"。
        if (Hp <= 0) Fail();
    }

    /// 被猫扑到时的推开：沿"背离猫"的方向推 2.2 m，撞到东西就少推一点。
    /// 为什么不用传送：传送会把相机/朝向一起重置，玩家会失去方位感 —— 而且这关的视野本来就很暗，
    /// 一传送就等于"画面闪一下、我还在原地"，跟卡死没区别。
    void Knock(Vector3 from)
    {
        if (Rig == null) return;
        Rig.Nudge(from, 2.2f);
        _lastPos = Player.position;
    }

    /// 本关奶酪集满。**不再直接 "挑战成功"** —— 现在是 10 关，过关只是"进下一关的凭据"。
    void Win()
    {
        Finished = true;
        Won = true;
        Failed = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Rig != null) Rig.AcceptInput = false;

        // 记进度（自由选择模式：三个场景各记各的最高关卡，互不影响）
        NbProgress.Reach(NbScene.Kind, Mathf.Min(Level + 1, NbLevels.Count));

        bool last = NbLevels.IsLast(Level);
        if (NbHud.I != null)
        {
            if (last)
                NbHud.I.ShowEnd(NbScene.Code + " CLEARED",
                    CheeseNeeded + " OF " + CheeseNeeded + " BROUGHT BACK.  " + NbScene.Name + " IS QUIET.",
                    "PRESS  R  TO  PLAY  AGAIN", true);
            else
                NbHud.I.ShowEnd("LEVEL " + Level + " CLEAR",
                    CheeseNeeded + " OF " + CheeseNeeded + " BROUGHT BACK.  NEXT IS LEVEL " + (Level + 1) + ".",
                    "PRESS  R  TO  CONTINUE", true);
        }

        Debug.LogFormat("[Nb] 第 {0} 关通过（生命 {1}/{2}）。{3}",
            Level, Hp, MaxHp, last ? "已是最后一关。" : "按 R 进入第 " + (Level + 1) + " 关。");
    }

    /// 血被咬光。和过关共用一块面板，但标题、颜色、按键文案全不一样 ——
    /// 玩家必须一眼分清"我赢了"和"我死了"，这两件事后面接的动作是相反的。
    internal void Fail()
    {
        if (Finished) return;
        Finished = true;
        Won = false;
        Failed = true;
        Hp = 0;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Rig != null) Rig.AcceptInput = false;

        // 失败的原因每个场景不一样，标题必须说清"是谁弄死我的"：
        // 后院的猫只是干扰，真正扣血的是保安；厨房扣血的还是猫，但更常见的是食物全烂了。
        string cause = Rotted ? "THE FOOD IS GONE"
                     : NbScene.UsesGuards ? "THE NIGHT PATROL GOT YOU"
                     : NbScene.UsesDoor ? "THE LIGHT CAUGHT YOU"
                     : "THE CATS GOT YOU";

        if (NbHud.I != null)
            NbHud.I.ShowEnd(cause,
                "OUT OF HEALTH ON LEVEL " + Level + ".",
                "PRESS  R  TO  TRY  AGAIN", false);

        Debug.LogFormat("[Nb] 生命归零 —— 第 {0} 关失败（本关奶酪 {1}/{2}）。", Level, Collected, CheeseNeeded);
    }

    /// 过关面板上的那一个按键：
    ///   · 赢了且不是最后一关 → 进下一关
    ///   · 赢了且是最后一关   → 从第 1 关重来一整个流程
    ///   · 输了               → 重打本关（血补满、奶酪重摆）
    void NextOrRetry()
    {
        if (Won && !NbLevels.IsLast(Level)) { LoadLevel(Level + 1); return; }
        if (Won) { Debug.Log("[Nb] 十关通关，回到第 1 关重来。"); LoadLevel(1); return; }
        LoadLevel(Level);
    }

    /// 外部（旧脚本 / 调试）调用的"重开一局"：等价于重打本关。
    internal void Restart()
    {
        LoadLevel(Level);
    }

    // ───────────────────────── 换场景 ─────────────────────────

    bool _switching;

    /// 由场景选择页调用。**重建整个世界**，而不是只换一组数字 ——
    /// 12 栋楼、地面、围墙都是场景文件里实实在在的物体，后院/厨房得把它们整体关掉
    /// （NbScene.HideOriginal）再搭自己的。只换数据是搭不出新场景的。
    internal void EnterScene(NbSceneKind k)
    {
        if (_switching) return;
        StartCoroutine(SwitchTo(k));
    }

    IEnumerator SwitchTo(NbSceneKind k)
    {
        _switching = true;
        Debug.LogFormat("[Nb] 切换场景 → {0}", k);

        ClearProps();

        if (Root != null) Nb.Kill(Root.gameObject);
        if (Rig != null) Nb.Kill(Rig.gameObject);
        Root = null;
        Rig = null;
        Player = null;

        // HUD / 小地图是独立根物体、不在 Root 下面，得单独拆。
        // Kill 之后必须等一帧：Destroy 是延迟到帧末的，这一帧旧 Canvas 还在，
        // 紧接着 Create 会让两套 HUD 短暂叠在一起（数值不同，看起来像画面重影）。
        if (NbHud.I != null) Nb.Kill(NbHud.I.gameObject);
        if (NbMap.I != null) Nb.Kill(NbMap.I.gameObject);
        yield return null;

        NbScene.Kind = k;
        yield return StartCoroutine(Boot());

        _switching = false;
    }
}

// ───────────────────────── 奶酪 ─────────────────────────

internal class NbCheese : MonoBehaviour
{
    internal int Index;
    internal Transform Halo;
    internal bool Taken;

    /// 厨房专属：腐烂后整块消失，**不能再被捡起**。
    /// 和 Taken 分开两个字段是必须的 —— Taken 是"我拿在手里"，Rotted 是"它不存在了"，
    /// 用同一个字段会让 Restart 把烂掉的又变回好的。
    internal bool Rotted;

    void Update()
    {
        if (Taken) return;
        transform.Rotate(0f, 42f * Time.deltaTime, 0f, Space.World);
    }
}

// ───────────────────────── 猫 ─────────────────────────

/// 沿固定路线来回走；玩家进入视野且没有遮挡就追。追的时候不看路线，追丢了再回到最近的路点。
internal class NbCat : MonoBehaviour
{
    internal Vector3[] Route;
    internal float Speed = 1.55f;
    internal float ChaseSpeed = 3.15f;

    // 视野。**按关卡给**（NbLevels.Sight：第 1 关 10 m → 第 10 关 16 m），不再写死 16。
    // 这条是"难度层层递进"里最隐蔽的一档：猫没变多、没变快，只是更早抬头看你。
    internal float SightDist = 16f;
    // 这个距离以内不看朝向也能察觉 —— 不随关卡变，否则会出现"猫从背后踩过去却没看见你"。
    internal const float SightClose = 4f;

    /// 后院专属：**蹲守**。蹲着的时候不巡逻、也不扫视，玩家进 AmbushTrigger 才起身追。
    /// 它不看，它等 —— 这才是参考图里 "hide in the dark and sneakily attack players
    /// when they are not paying attention" 的意思。用视野锥做不出"偷袭"，
    /// 因为视野锥在玩家靠近之前就已经开始转头了。
    internal bool Ambush;
    internal float AmbushTrigger = 2.5f;

    Transform _player;
    int _idx = 1;
    int _dir = 1;               // 路线走到头就掉头
    float _wait;
    float _lost;
    bool _chasing;
    Vector3 _lastSeen;

    internal bool IsChasing { get { return _chasing; } }

    // ── 体型相关（由 NbGame.SpawnCats 按**实测包围盒**写进来，不再写死）──
    // 猫从 0.5 m 长到 0.95 m 高之后，原来那组常数（探针 0.35 高、半径 0.17、
    // 抓人 1.15 m）全都对不上了：探针会从奶酪上方飘过去、抓人距离比猫的身子还短，
    // 玩家能站在猫的肚子底下不被抓。
    internal float ProbeUp = 0.55f;        // 扫掠/视线起点离地高度（约胸高）
    internal float ProbeRadius = 0.30f;    // 扫掠球半径（约半个身宽）
    internal float ProbeDist = 0.85f;      // 前瞻距离
    internal float CatchDist = 1.10f;      // 多近算抓住（半个体长 + 玩家半径 + 余量）
    internal float BodyLength = 1.80f;     // 最大水平尺寸，只用于日志与判断

    internal void ConfigureBody(Bounds box, float height)
    {
        float span = Mathf.Max(box.size.x, box.size.z);
        if (span <= 0.01f) span = height * 1.7f;      // 量不到网格时的兜底

        BodyLength = span;
        ProbeUp = Mathf.Clamp(height * 0.55f, 0.2f, 2.5f);
        ProbeRadius = Mathf.Clamp(span * 0.16f, 0.12f, 0.6f);
        ProbeDist = Mathf.Max(0.45f, ProbeRadius * 2.9f);
        // 抓人的判定中心在猫的根节点（脚下），所以距离必须≥半个体长，
        // 否则"看得见、追得上、就是抓不到"。
        CatchDist = span * 0.5f + 0.25f;

        Debug.LogFormat("[Nb] 「{0}」体型：高 {1:F2} m，水平 {2:F2}×{3:F2} m ⇒ 扫掠 半径{4:F2}/前探{5:F2}，" +
                        "抓人距离 {6:F2} m，视线起点离地 {7:F2} m。",
            name, height, box.size.x, box.size.z, ProbeRadius, ProbeDist, CatchDist, ProbeUp);
    }

    void Start()
    {
        if (NbGame.I != null) _player = NbGame.I.Player;
        if (Route != null && Route.Length > 1) _idx = 1;
    }

    /// 放弃追击、回路线。
    /// `relocate = false`（追丢时用）：只挑**离自己最近**的路点，猫就地继续巡逻。
    /// `relocate = true`（被抓之后用）：挑**离玩家最远**的路点，并且真的把猫搬过去。
    ///   —— 这一点是这轮的关键：旧版不管哪种情况都只改 `_idx`，猫的身体一动不动。
    ///   于是"被抓 → 猫就在你脚边 → 无敌一过又抓"，日志里连成串。
    internal void ResetChase(bool relocate = false)
    {
        _chasing = false;
        _lost = 0f;
        _wait = 0f;
        if (Route == null || Route.Length == 0) return;

        int best = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < Route.Length; i++)
        {
            // 求"最远" ⇒ 拿负距离当分数比最小
            float score = (relocate && _player != null)
                ? -Nb.FlatDist(Route[i], _player.position)
                : Nb.FlatDist(transform.position, Route[i]);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        _idx = best;

        if (!relocate) return;

        Vector3 dest = Route[best];
        // 落点朝向路线里"另一个方向"的那一头，免得刚落地就正对着玩家
        int other = Route.Length > 1 ? (best == 0 ? 1 : best - 1) : best;
        Vector3 look = Route[other] - dest;
        look.y = 0f;

        transform.position = new Vector3(dest.x, transform.position.y, dest.z);
        if (look.sqrMagnitude > 1e-3f) transform.rotation = Quaternion.LookRotation(look.normalized);
        Debug.LogFormat("[Nb] 「{0}」被送回路线远端 ({1:F1}, {2:F1})，离玩家 {3:F1} m。",
            name, dest.x, dest.z, _player != null ? Nb.FlatDist(dest, _player.position) : -1f);
    }

    void Update()
    {
        if (NbGame.I == null || NbGame.I.Finished) return;
        if (_player == null && NbGame.I != null) _player = NbGame.I.Player;
        if (Route == null || Route.Length == 0) return;

        // 埋伏猫：蹲在路线上不动，玩家进 2.5 m 才起身。起身之后走正常的追击逻辑。
        if (Ambush && !_chasing)
        {
            if (_player == null || Nb.FlatDist(transform.position, _player.position) > AmbushTrigger)
                return;                        // 不动、不巡逻、不转头 —— 它只是一块影子

            _chasing = true;
            _lastSeen = _player.position;
            _lost = 0f;
            Debug.LogFormat("[Nb] 埋伏的「{0}」起身了（玩家离 {1:F1} m）。", name,
                Nb.FlatDist(transform.position, _player.position));
        }

        bool see = CanSee();
        if (see)
        {
            _chasing = true;
            _lost = 0f;
            _lastSeen = _player.position;      // 只记位置，不实时跟踪
        }
        else if (_chasing)
        {
            _lost += Time.deltaTime;
            if (_lost > 3.2f)
            {
                _chasing = false;
                ResetChase();
            }
        }

        // 抓到人（放在移动之前判断：猫已经贴到身上时不该再往前挤）
        if (_chasing && _player != null &&
            Nb.FlatDist(transform.position, _player.position) < CatchDist)
        {
            NbGame.I.Caught(this);
            return;
        }

        // 追的时候朝"最后看见的位置"走，而不是玩家当前的坐标 ——
        // 后者会让猫隔着一整栋楼抄直线穿墙过去。
        Vector3 target = _chasing ? _lastSeen : Route[_idx];
        float speed = _chasing ? ChaseSpeed : Speed;

        Vector3 flat = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
        float dist = flat.magnitude;

        if (!_chasing && dist < 0.7f)
        {
            _wait += Time.deltaTime;
            if (_wait < 0.45f) { Face(flat); return; }
            _wait = 0f;
            StepRoute();
            return;
        }

        // 追到了"最后看见的位置"还是没人（玩家绕走了）：停下来重新张望
        if (_chasing && dist < 0.8f)
        {
            _chasing = false;
            ResetChase();
            return;
        }

        if (dist < 0.001f) return;
        Vector3 move = Steer(flat / dist);
        transform.position += move * speed * Time.deltaTime;
        Face(move);
    }

    /// 前方短距离球扫掠：撞到楼体或围墙就沿表面滑开。
    /// 这里没有 NavMesh，不这么做的话猫会直接穿墙走过去。
    Vector3 Steer(Vector3 dir)
    {
        RaycastHit h;
        Vector3 o = transform.position + Vector3.up * ProbeUp;
        if (!Physics.SphereCast(o, ProbeRadius, dir, out h, ProbeDist, ~0, QueryTriggerInteraction.Ignore))
            return dir;
        if (h.collider == null) return dir;
        if (_player != null && h.collider.transform.root == _player.root) return dir;  // 撞到人：交给距离判定

        // 奶酪：贴地的小方盒，正面撞上去时"沿表面投影"是零向量，会被下面的兜底原样返回 →
        // 猫就那么从奶酪中间穿过去了。所以奶酪单独处理：往自己当前那一侧绕。
        NbCheese cheese = h.collider.GetComponentInParent<NbCheese>();
        if (cheese != null)
        {
            Vector3 toObs = cheese.transform.position - transform.position;
            toObs.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-6f) return dir;
            right.Normalize();
            Vector3 side = Vector3.Dot(right, toObs) > 0f ? -right : right;   // 障碍在右就往左绕
            Vector3 round = (dir * 0.45f + side).normalized;
            return round.sqrMagnitude > 1e-4f ? round : dir;
        }

        Vector3 n = h.normal;
        n.y = 0f;
        if (n.sqrMagnitude < 1e-4f) return dir;
        Vector3 slide = Vector3.ProjectOnPlane(dir, n.normalized);
        slide.y = 0f;
        return slide.sqrMagnitude > 1e-4f ? slide.normalized : dir;
    }

    void StepRoute()
    {
        if (Route.Length <= 1) return;
        _idx += _dir;
        if (_idx >= Route.Length)
        {
            _idx = Route.Length - 2;
            _dir = -1;
        }
        else if (_idx < 0)
        {
            _idx = 1;
            _dir = 1;
        }
    }

    void Face(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-5f) return;
        dir.y = 0f;
        Quaternion want = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, 7f * Time.deltaTime);
    }

    /// 视野：SightDist（本关给定，10~16 m）内、身前约 140° 的锥形；SightClose（4 m）内不看方向也能察觉。
    /// 中间不能隔着楼或围墙。
    bool CanSee()
    {
        if (_player == null) return false;

        // 出生点周围 6 m 是安全区：猫不追进来。
        // 玩家可以自己把出生点摆在任意位置，而"纵向通道 A 那只"的巡逻线正好穿过街区正中 ——
        // 站在出生点上就永远躲不开，手感上就是"动不了"。离开 6 m 之后猫照旧追。
        if (NbGame.I != null && Nb.FlatDist(_player.position, NbGame.I.Home) < 6f) return false;

        // 猫的眼睛在胸高；看的是**玩家的眼睛**，不是"头顶上方 1 m"——
        // 老鼠只有 0.30 m 高，盯着 1 m 高的地方等于在看墙。
        Vector3 eye = transform.position + Vector3.up * ProbeUp;
        Vector3 at = (NbPlayer.I != null && NbPlayer.I.Eye != null)
            ? NbPlayer.I.Eye.position
            : _player.position + Vector3.up * 0.16f;
        Vector3 d = at - eye;
        float dist = d.magnitude;
        if (dist > SightDist) return false;

        Vector3 flat = new Vector3(d.x, 0f, d.z);
        if (flat.sqrMagnitude < 1e-4f) return true;

        if (dist > SightClose)
        {
            Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
            if (Vector3.Dot(fwd, flat.normalized) < 0.35f) return false;
        }

        RaycastHit hit;
        if (Physics.Linecast(eye, at, out hit, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.transform.root != _player.root) return false;
        }
        return true;
    }
}
