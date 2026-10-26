// @role First-person mouse controller: builds its own body and camera; walk/run/jump, look, idle squeak.
// @calls NbGame AudioManager
// @tune WalkSpeed:float:1-4:walk speed m/s
// @tune RunMul:float:1-3:run multiplier on WalkSpeed
// @tune JumpSpeed:float:1-6:jump take-off velocity m/s
// @tune Gravity:float:-40--5:gravity acceleration m/s2
// @tune LookSens:float:0.02-0.5:mouse look sensitivity deg/pixel

// NbPlayer.cs — 第一人称玩家（全新一套：自己造身体、自己造相机，不读场景里任何东西）
//
// ───────────────────────── 为什么要重写 ─────────────────────────
// 旧版的第一人称是四样东西拼起来的：
//   ① 场景里那个 FirstPersonController（可能被拖到别的物体下面、改了缩放）
//   ② 它自带的 CapsuleCollider（半径 0.5、中心 (0,0,0)：一半埋在地下）
//   ③ 运行时补的 CharacterController（和 ② 套在一起互顶）
//   ④ 场景里那台本地 y = 0.26 的 Main Camera（眼睛在膝盖高度，还横歪 88°）
// 任何一样出问题，表现都只有一句「按 WASD 不动」，而且 Console 一声不响。
//
// 这一版把四样合成一个**自己新建的**物体，场景里那个老玩家直接整个关掉：
//   · 根物体 = 胶囊，原点在**脚底**（CharacterController 高 0.30、半径 0.10、中心 (0,0.15,0)）
//   · 子物体 Eye = 相机，本地 y = 0.16（**老鼠眼高**），只负责俯仰
//   · 偏航转根物体、俯仰转 Eye —— 两边各写各的，谁也覆盖不掉谁
//   · 出生时把场上**所有别的相机、所有 AudioListener 全部关掉**，永远只有一个相机在画
//   · 位移用 unscaledDeltaTime：世界被 timeScale 冻住时人也照样能走
//     （标题页期间由 AcceptInput = false 挡住，不靠 timeScale）
//
// 键位：WASD / 方向键走，Shift 跑，Space 跳，鼠标转视角，Q/E 键盘左右转（鼠标失灵时的兜底），
//       Esc 释放鼠标，点画面重新锁定。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

internal class NbPlayer : MonoBehaviour
{
    internal static NbPlayer I;

    // ───────────────────────── 尺寸（都不是可调参数，写死） ─────────────────────────
    // **这一关的玩家是一只老鼠，不是成年人。**
    // 眼高 0.16 m —— 比成年人脚踝还低；走过的路面在视野里是一片向远处铺开的平面，
    // 楼是两侧的墙，垃圾桶是一只桶，猫是 0.95 m 高的巨兽（见 NbGame.CatHeight）。
    // 比例感全靠这一组数，改动要连带想清楚猫 / 奶酪 / 路缘（0.03 m）的相对大小。
    //
    // 提醒：旧版在这里踩过一个坑 —— 眼高掉到 0.29 m 时"看着像卡死"，
    // 原因是当时地面没有贴图、光照又平，整屏是一个纯色渐变的平面，走起来画面几乎不变。
    // 现在过道里铺了石子/裂纹/落叶/井盖（NbBuilder.BuildGroundDetail），近距离有参照物，
    // 低眼高才真的像"老鼠视角"，而不是"卡在地板里"。
    internal const float EyeHeight = 0.16f;
    internal const float BodyHeight = 0.30f;
    internal const float BodyRadius = 0.10f;

    // ───────────────────────── 手感 ─────────────────────────
    // 老鼠的体型小，同样的绝对速度在低视角下会被放大很多倍（近处东西唰唰地过）。
    // 2.80 → **2.30**：慢下来的理由不是"太快看不清"，而是**关卡需要速度差**：
    //   猫的巡逻 1.10~1.55、追击 2.20~3.25（见 NbLevels），
    //   走路 2.30 刚好夹在两者之间 —— 走能甩开巡逻，甩不开追击；
    //   跑 2.30×1.70 = 3.91 快过最快的猫，所以"按住 Shift 一定跑得掉"是这游戏对玩家唯一的承诺。
    //   把走路调到 2.8，猫的追击就永远追不上，生命条也就没有意义了。
    internal float WalkSpeed = 2.3f;
    internal float RunMul = 1.70f;
    internal float JumpSpeed = 3.2f;

    /// 起跳速度的倍率。0.16 m 眼高的老鼠，3.2 m/s 起跳 = **0.26 m 高**，刚好跨过路缘和台阶
    /// （街区/后院要的就是这个数：跳太高会变成"随时在飘"）。
    /// 冰箱曾经把它调到 1.75 硬跳 0.56 m 的层高 —— 实测跳不上去，已经改成**走楼梯**，
    /// 所以现在四个场景都是 1。留着这个字段是为了以后调手感，不是给某个场景专用的开关。
    internal float JumpMul = 1f;
    internal float Gravity = -20f;
    internal float LookSens = 0.10f;       // 度 / 像素

    internal CharacterController Cc;
    internal Transform Eye;
    internal Camera Cam;

    internal float Yaw;                    // 身体朝向（度）
    internal float Pitch;                  // 眼睛俯仰（度），正数低头
    internal float Speed;                  // 实测水平速度 m/s（读数条 / 小地图共用）

    /// 水平朝向（单位向量，已去掉俯仰）。
    ///
    /// **站着不动、只转鼠标时它也会变** —— 小地图箭头要的正是这个。
    /// 别再拿"上一帧位移方向"当朝向：静止时那是 0，方向只能靠最后一次移动残留，
    /// 于是"面朝北站着转了一圈"箭头一动不动。
    internal Vector3 Facing
    {
        get
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            // 俯仰接近 ±90° 时投影会退化；这时退回上一帧的 Yaw，别返回零向量
            if (f.sqrMagnitude < 1e-6f)
                return new Vector3(Mathf.Sin(Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(Yaw * Mathf.Deg2Rad));
            return f.normalized;
        }
    }

    /// 能不能操作。标题页、结算页把它设成 false —— 拦截方式**不是** timeScale，
    /// 所以不存在"世界被冻住、人也没法走"这种连坐。
    internal bool AcceptInput = true;

    float _vy;
    float _noLookUntil;
    float _unlockedAt = -99f;
    bool _escaped;                          // 按过 Esc：不再自动锁光标，等玩家自己点回来
    float _stuckT;
    float _movedTotal;
    bool _loggedMove;
    bool _loggedStuck;
    float _idleT;                           // 距上次"移动输入"的秒数（满 3 秒吱一声）
    Vector3 _spawn;                         // 出生点（贴地之后的真实值），掉出世界时回这里

    // ───────────────────────── 出生 ─────────────────────────

    /// 造一个全新的玩家。`feet` 是**脚底**的世界坐标，`yaw` 是身体朝向（度）。
    /// 调用前场景里的老玩家还在也没关系 —— 这里会把它整个关掉。
    internal static NbPlayer Spawn(Vector3 feet, float yaw)
    {
        if (I != null) return I;

        // ① 场景里那个老玩家整个关掉（连带它自带的胶囊、相机、AudioListener、旧移动脚本一起停）
        Transform old = FindScenePlayer();
        string oldInfo = "无";
        if (old != null)
        {
            oldInfo = string.Format("「{0}」位置 ({1:F2},{2:F2},{3:F2}) 缩放 ({4:F2},{5:F2},{6:F2})",
                old.name, old.position.x, old.position.y, old.position.z,
                old.lossyScale.x, old.lossyScale.y, old.lossyScale.z);
            old.gameObject.SetActive(false);
        }

        // ② 场上所有别的相机 / 音频监听器全部关掉：两个相机同时画、其中一个还没有 AudioListener，
        //    表现可能是"画面是另一个相机看到的"——而且完全不报错。
        int camOff = 0, listenOff = 0;
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i] == null || !cams[i].enabled) continue;
            cams[i].enabled = false;
            camOff++;
        }
        AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] == null || !listeners[i].enabled) continue;
            listeners[i].enabled = false;
            listenOff++;
        }

        // ③ 新身体
        GameObject go = new GameObject("Player (new)");
        go.transform.position = feet;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one;

        CharacterController cc = go.AddComponent<CharacterController>();
        cc.height = BodyHeight;
        cc.radius = BodyRadius;
        cc.center = new Vector3(0f, BodyHeight * 0.5f, 0f);   // 原点在脚底
        cc.slopeLimit = 52f;
        // 老鼠只有 0.30 高：台阶上限必须跟着压到 8 cm（铺装层之间差 3~4 cm，够跨）。
        // 原来 0.32 是为了让成年人一步跨过 0.34 m 高的奶酪，现在这一步由
        // SpawnCheese 里的 Physics.IgnoreCollision 负责，不再靠 teleport 式的台阶。
        cc.stepOffset = 0.08f;
        cc.skinWidth = 0.012f;
        cc.minMoveDistance = 0f;

        // ④ 眼睛（相机）
        GameObject eyeGo = new GameObject("Eye");
        eyeGo.transform.SetParent(go.transform, false);
        eyeGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
        eyeGo.transform.localRotation = Quaternion.identity;

        Camera cam = eyeGo.AddComponent<Camera>();
        // 老鼠视角：近裁剪面必须很小，否则贴着地面/墙走时会"切"出一条空缝；
        // FOV 放宽到 72° —— 小动物眼睛的视野本来就宽，而且低机位配宽视场能把
        // 两侧楼体的透视压得更陡，显得楼更高、自己更小。
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 400f;
        cam.fieldOfView = 72f;
        cam.depth = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.035f, 0.045f, 0.075f);
        cam.allowHDR = true;
        cam.allowMSAA = true;
        cam.cullingMask = ~0;             // 什么都不剔除：宁可多画，也不要"图层被剔掉只剩一片底色"
        eyeGo.tag = "MainCamera";         // Camera.main 要能找到它（NbLighting / 自检都靠这个）

        eyeGo.AddComponent<AudioListener>();

        NbPlayer p = go.AddComponent<NbPlayer>();
        p.Cc = cc;
        p.Eye = eyeGo.transform;
        p.Cam = cam;
        p.Yaw = yaw;
        p.Pitch = 0f;
        I = p;

        Debug.LogFormat("[Player] 新玩家已生成：(脚底) {0:F2},{1:F2},{2:F2}，朝向 {3:F0}°；" +
                        "胶囊 高{4:F2}/半径{5:F2}，眼高 {6:F2}。" +
                        "关掉旧玩家 {7}；关掉其它相机 {8} 台、其它 AudioListener {9} 个。",
            feet.x, feet.y, feet.z, yaw, BodyHeight, BodyRadius, EyeHeight,
            oldInfo, camOff, listenOff);

        return p;
    }

    /// 找场景里原来那个玩家（不管它是不是被关掉、挂在哪一层）。
    internal static Transform FindScenePlayer()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "FirstPersonController") return root.transform;
            Transform t = root.transform.Find("FirstPersonController");
            if (t != null) return t;
        }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "FirstPersonController") return t;
        }
        return null;
    }

    // ───────────────────────── 生命周期 ─────────────────────────

    IEnumerator Start()
    {
        GroundSnap();
        _spawn = transform.position;
        _noLookUntil = Time.unscaledTime + 0.45f;   // 刚出生别吃鼠标跳变

        LogSetup();

        yield return new WaitForSecondsRealtime(0.9f);
        Probe("开场");
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LogSetup()
    {
        Debug.LogFormat("[Player] 就位：世界位置 ({0:F2},{1:F2},{2:F2})，Yaw {3:F0}°，Pitch {4:F0}°；" +
                        "Eye 世界 ({5:F2},{6:F2},{7:F2})；相机 enabled={8} depth={9} 清屏={10} FOV={11:F0} 掩码 0x{12:X} near={13:F2}",
            transform.position.x, transform.position.y, transform.position.z, Yaw, Pitch,
            Eye.position.x, Eye.position.y, Eye.position.z,
            Cam != null && Cam.enabled, Cam != null ? Cam.depth : -1f,
            Cam != null ? Cam.clearFlags.ToString() : "?", Cam != null ? Cam.fieldOfView : 0f,
            Cam != null ? Cam.cullingMask : 0, Cam != null ? Cam.nearClipPlane : 0f);

        int live = 0;
        Camera[] all = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy) live++;
        Debug.LogFormat("[Player] 场上启用中的相机共 {0} 台（必须正好 1 台，就是玩家眼睛）。", live);
        if (live != 1)
            Debug.LogError("[Player] 相机数量不是 1 —— 画面可能来自别人的相机，这就足以让「你看到的东西」和「玩家所在的位置」完全脱节。");
    }

    // ───────────────────────── 每帧 ─────────────────────────

    void Update()
    {
        if (Cc == null || Eye == null) return;      // Spawn 还没把字段塞进来（同帧竞态，理论上不会）

        float raw = Time.unscaledDeltaTime;
        float dt = Mathf.Clamp(raw, 0f, 0.05f);

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        ManageCursor(kb, mouse);

        // ── 视角 ──
        if (AcceptInput)
        {
            if (mouse != null && Time.unscaledTime >= _noLookUntil)
            {
                // 不看 lockState：Input System 的 delta 在未锁定时也照常给，
                // 这样"锁光标失败"不会顺带把转视角也废掉（旧版就是栽在这上面）。
                Vector2 d = mouse.delta.ReadValue();
                d.x = Mathf.Clamp(d.x, -70f, 70f);      // 锁光标那一帧会有一次巨大跳变，夹住而不是丢掉
                d.y = Mathf.Clamp(d.y, -70f, 70f);
                Yaw += d.x * LookSens;
                // 旧版这里一次 −2000 px 的跳变能把俯仰顶到 −89°：整屏只剩脚下一块地砖，
                // 而那地砖没有贴图、光照又平 ⇒ 走起来画面几乎不变 ⇒「卡死了」。
                Pitch = Mathf.Clamp(Pitch - d.y * LookSens, -80f, 80f);
            }

            if (kb != null)
            {
                float turn = 0f;
                if (kb.qKey.isPressed) turn -= 1f;
                if (kb.eKey.isPressed) turn += 1f;
                if (turn != 0f) Yaw += turn * 100f * dt;

                if (kb.enterKey.isPressed)
                {
                    NbSceneSelect.Show();
                    AcceptInput = false;
                    return;
                }
            }

            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            Eye.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }

        // ── 位移 ──
        float h = 0f, v = 0f;
        if (AcceptInput && kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        }

        Quaternion yawQ = Quaternion.Euler(0f, Yaw, 0f);
        Vector3 wish = yawQ * Vector3.forward * v + yawQ * Vector3.right * h;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        bool running = AcceptInput && kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        float speed = WalkSpeed * (running ? RunMul : 1f);

        if (Cc.isGrounded)
        {
            if (_vy < 0f) _vy = -3f;                                  // 贴地小下压，让 isGrounded 稳定
            if (AcceptInput && kb != null && kb.spaceKey.wasPressedThisFrame) _vy = JumpSpeed * JumpMul;
        }
        _vy += Gravity * dt;

        Vector3 before = transform.position;
        Cc.Move(wish * speed * dt + Vector3.up * (_vy * dt));
        Vector3 delta = transform.position - before;
        delta.y = 0f;

        Speed = raw > 0.0001f ? delta.magnitude / raw : 0f;
        _movedTotal += delta.magnitude;

        // 掉出世界兜底：某段过道漏了碰撞体时人会一直往下掉，屏幕上只剩天空/雾 ——
        // 那看起来和"卡住"一模一样，而且一路往下掉时也是"什么都不动"。
        if (transform.position.y < _spawn.y - 8f)
        {
            Debug.LogWarningFormat("[Player] 掉到 y={0:F1}（出生点 y={1:F1}）—— 脚底下有段地面没有碰撞体，已送回出生点。",
                transform.position.y, _spawn.y);
            Teleport(_spawn, Yaw);
        }

        // ── 两条自证日志（只打一次，不刷屏）──
        if (!_loggedMove && _movedTotal > 1.2f)
        {
            _loggedMove = true;
            Debug.LogFormat("[Player] 已经在移动：累计 {0:F2} m，速度 {1:F2} m/s —— 输入 → CharacterController 这条链是通的。" +
                            "（如果你看到的画面还是不动，问题在画面上，不在输入上）", _movedTotal, Speed);
        }

        if (wish.sqrMagnitude > 0.01f && delta.sqrMagnitude < 0.000001f)
        {
            _stuckT += raw;
            if (_stuckT > 0.7f && !_loggedStuck)
            {
                _loggedStuck = true;
                Debug.LogWarningFormat("[Player] 按着键却一步没动（位置 {0:F2},{1:F2},{2:F2}）：{3}",
                    transform.position.x, transform.position.y, transform.position.z, Blockers());
            }
        }
        else _stuckT = 0f;

        // ── 闲置吱吱叫 ──
        // 只认**移动输入**（WASD / 方向键 / 跳）：站着转头、Shift、Q/E 都不算。
        // 满 3 秒吱一声，之后每满 3 秒再吱；一旦重新移动，立刻停。
        bool moveInput = h != 0f || v != 0f || (kb != null && kb.spaceKey.wasPressedThisFrame);
        if (moveInput)
        {
            _idleT = 0f;
        }
        else
        {
            _idleT += raw;
            if (_idleT >= 3f)
            {
                _idleT = 0f;
                AudioManager.PlaySqueak();
                NbGame game = NbGame.I;
                if (game != null) game.OnSqueak(transform.position);
            }
        }
    }

    string Blockers()
    {
        Vector3 mid = transform.position + Vector3.up * (BodyHeight * 0.5f);
        float r = BodyRadius * 0.92f;
        Collider[] hits = Physics.OverlapCapsule(mid + Vector3.up * (BodyHeight * 0.5f - r),
                                                mid - Vector3.up * (BodyHeight * 0.5f - r),
                                                r, ~0, QueryTriggerInteraction.Ignore);
        var names = new List<string>();
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;
            if (hits[i] is CharacterController && hits[i].transform == transform) continue;
            names.Add(hits[i].name);
        }
        if (names.Count == 0)
            return "胶囊没有和任何碰撞体重叠 ⇒ 不是被卡住，检查 Cc.enabled 与地面高度";
        return "胶囊被 " + names.Count + " 个碰撞体包着：" + string.Join(", ", names.ToArray());
    }

    // ───────────────────────── 对外的动作 ─────────────────────────

    /// 台阶上限。**冰箱必须调高**：那一关是走上楼梯（每级 0.14 m），
    /// 而默认的 0.08 是按街区那些 3~4 cm 的铺装高差定的 ——
    /// 用 0.08 去走 0.14 的踏步，表现是"贴着台阶推不动"，
    /// 而且角色控制器不会报任何错，看上去就像那里有一堵透明的墙。
    /// 切场景时**每次都要显式设**（街区/后院要设回 0.08），否则会带着高台阶上限在街上走。
    internal void SetStepUp(float h)
    {
        // 上限卡在 0.75×身高：stepOffset 一旦接近身高，CharacterController 会把
        // 任何齐腰的坎都当成"台阶"—— 在冰箱里刚好够用（0.14 的踏步），再多就是作弊。
        if (Cc != null) Cc.stepOffset = Mathf.Clamp(h, 0.02f, BodyHeight * 0.75f);
    }

    /// 传送到某处（脚底坐标）。关-改-开 CharacterController，否则位置会被它吃掉。
    internal void Teleport(Vector3 feet, float yaw)
    {
        bool was = Cc != null && Cc.enabled;
        if (Cc != null) Cc.enabled = false;

        transform.position = feet;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Yaw = yaw;
        Pitch = 0f;
        _vy = 0f;
        Speed = 0f;
        _stuckT = 0f;
        if (Eye != null) Eye.localRotation = Quaternion.identity;

        if (Cc != null) Cc.enabled = was;
        GroundSnap();
        _noLookUntil = Time.unscaledTime + 0.2f;
    }

    /// 沿「背离 from」的方向推开一段距离（被猫扑到用），撞墙就少推一点。
    internal void Nudge(Vector3 from, float dist)
    {
        Vector3 dir = transform.position - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.04f) dir = -transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
        dir.Normalize();

        RaycastHit hit;
        Vector3 origin = transform.position + Vector3.up * (BodyHeight * 0.5f);
        if (Physics.SphereCast(origin, BodyRadius, dir, out hit, dist, ~0, QueryTriggerInteraction.Ignore) &&
            hit.collider != null && !hit.collider.transform.IsChildOf(transform))
        {
            dist = Mathf.Max(0.6f, hit.distance - 0.15f);
        }

        Teleport(transform.position + dir * dist, Yaw);
    }

    /// 把脚底贴到地面上（正下方 6 m 内最近的实体表面）。
    internal void GroundSnap()
    {
        Vector3 from = transform.position + Vector3.up * 0.6f;
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null) continue;
            if (hits[i].collider.transform.IsChildOf(transform)) continue;   // 别量到自己身上
            if (hits[i].distance < best) best = hits[i].distance;
        }
        if (best < float.MaxValue)
        {
            float y = from.y - best + 0.02f;
            if (Mathf.Abs(y - transform.position.y) > 0.001f)
            {
                bool was = Cc != null && Cc.enabled;
                if (Cc != null) Cc.enabled = false;
                transform.position = new Vector3(transform.position.x, y, transform.position.z);
                if (Cc != null) Cc.enabled = was;
                Debug.LogFormat("[Player] 贴地：脚底 y {0:F3} → {1:F3}（正下方地面 {2:F2} m）。",
                    transform.position.y, y, best);
            }
        }
        else
        {
            Debug.LogWarning("[Player] 脚底正下方 6 m 内没有地面 —— 人会一直往下掉。检查过道/地面有没有碰撞体。");
        }
    }

    internal void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _escaped = false;
        _noLookUntil = Time.unscaledTime + 0.25f;
    }

    // ───────────────────────── 光标 ─────────────────────────

    void ManageCursor(Keyboard kb, Mouse mouse)
    {
        if (!AcceptInput)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        if (Cursor.lockState == CursorLockMode.Locked) return;

        // Esc 放手：记下"是玩家主动放开的"，之后不再自动锁回来，等他自己点画面
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _escaped = true;
            _unlockedAt = Time.unscaledTime;
            return;
        }

        bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
        if (clicked)
        {
            LockCursor();
            return;
        }
        if (!_escaped && Time.unscaledTime - _unlockedAt > 2.0f)
        {
            // 一直没锁上（编辑器切窗口、系统把锁丢了）就自己锁回来：
            // 不锁光标时鼠标能飘出游戏窗口，转视角也会跟着卡。
            LockCursor();
        }
    }

    // ───────────────────────── 诊断 ─────────────────────────

    /// 只回答一个问题：眼睛现在站在哪、朝哪、眼前有没有东西挡着。
    internal void Probe(string tag)
    {
        if (Cam == null) return;

        Vector3 p = Cam.transform.position;
        Vector3 f = Cam.transform.forward;
        Debug.LogFormat("[Player] 自检({0}) 眼位 ({1:F2},{2:F2},{3:F2})，朝向 ({4:F2},{5:F2},{6:F2})，" +
                        "Yaw {7:F0}° Pitch {8:F0}°，离地 {9:F2} m",
            tag, p.x, p.y, p.z, f.x, f.y, f.z, Yaw, Pitch, p.y - GroundY());

        RaycastHit[] hits = Physics.RaycastAll(p, f, 60f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        var sb = new List<string>();
        for (int i = 0; i < hits.Length && sb.Count < 4; i++)
        {
            if (hits[i].collider == null) continue;
            sb.Add(string.Format("{0:F2} m「{1}」", hits[i].distance, hits[i].collider.name));
        }
        Debug.LogFormat("[Player] 自检({0}) 视线上的碰撞体（60 m）：{1}",
            tag, sb.Count == 0 ? "一个都没有 —— 前面是空的" : string.Join(" | ", sb.ToArray()));
    }

    float GroundY()
    {
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 0.6f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null) continue;
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].distance < best) best = hits[i].distance;
        }
        if (best == float.MaxValue) return transform.position.y;
        return transform.position.y + 0.6f - best;
    }
}
