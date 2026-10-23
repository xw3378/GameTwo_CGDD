// @role Yard night guard: random patrol, sweeping flashlight cone, chases and catches the mouse.
// @calls NbGame NbGuardArt AudioManager
// @tune Speed:float:0.3-3:patrol walk speed m/s
// @tune Sweep:float:0.1-3:flashlight sweep pace (higher = faster)
// @tune Range:float:5-40:flashlight beam range m
// @tune Half:float:2-45:beam cone half-angle deg
// @tune Damage:float:1-100:exposure gained per second in the beam
// @tune ChaseSpeed:float:1-6:chase speed m/s (below player run)

// NbGuard.cs — 夜巡保安（后院专属威胁）
//
// 设计意图（来自参考里的两条）：
//   "Nighttime human patrol: Humans move slowly at night, but their observation skills
//    are heightened. Light will reveal your position."
//   "Light: lit areas eliminate player after fixed amount of time → avoid light"
//
// 这一版把「光」从"慢慢烧血的条"改成了**一个会扑过来的人**：
//   ① 平时 0.95 m/s 在**自己那一带随机游走**（航点 5~16 m、到点站住环顾 1~3 秒），
//      手电 18 m 长、半角 11°，**四处扫**（前方 ±55° 为主、约 1/5 的机会甩到 ±95°，
//      站住时整个人转过去看身后）—— 他在找，不是在追；
//   ② 光扫到玩家的 0.3 秒后，他**抬头**：手电变红、直指玩家，然后以 2.55~3.45 m/s 冲过来
//      （比玩家走路 2.30 快，比跑步 3.91 慢 —— 所以"按 Shift"是唯一的解，而不是可选项）；
//   ③ 玩家躲到掩体后面，光被挡住 ⇒ 他会走到"最后看见你的地方"；
//   ④ **只有跑到 5 m 之外的掩体后**才算脱身。躲在 2 m 的灌木后面，他会径直走过来，
//      1 m 之内就算被抓住（-10 血），因为他的判定是"身体碰到"而不是"看见"。
//
// 巡逻为什么不是固定直线（用户明确要求「按不规则路线运动」）：
//   固定直线有两个问题 —— 玩家站两次就能背下来，以及"他什么时候背对我"变成了时钟。
//   现在每段路都是**当场抽的**：在自己那条路线两侧 ±7 m 的带子里随机取点，
//   长度 5~16 m 之间、走的时候还带一点横向摆动，到点停 1~3 秒、整个人转过去照别处。
//   于是"他背对着我"仍然会出现，但**不能靠数秒数等到**，只能当场看灯。
//   底子还是关卡表里的那 4 条路线：抽点只在带子里抽，所以几个人不会挤到同一个角落。
//
// 判定用几何而不是 Light 组件：
//   ① 距离 ≤ Range
//   ② 与手电朝向夹角 ≤ Half
//   ③ 从灯到玩家的射线**没有被掩体挡住**
// 三条同时成立才算"看见"。第 ③ 条是整个后院玩法的支点 ——
// 灌木、垃圾桶、柴垛、工具棚都是实体碰撞体，就是为了让这一条能成立。
// 注意：灯挂在手上（约 1.0~1.4 m 高）且**俯仰会在 −24°~+6° 之间扫**，
// 所以压低时远处脚下是暗的、贴到 4 m 以内反而照不到。这不是 bug：
// 近身时的威胁是"他撞上你"，而不是"他看见你"。
//
// 曝光值的累积在 NbGame 里（因为要跨多个保安求和 + 和血量/HUD 一起结算），
// 这里只负责回答两个问题：**这一帧我有没有看见他**、**我是不是在追他**。
//
// 外观：Resources/Guards/Walking（Mixamo 真人 + 原地走路动画），见 NbGuardArt.cs。
// 模型只是**外观** —— 手电、判定、巡逻全都挂在保安根节点上，换模型动不到玩法；
// 自动加载失败时退回几何体，两条路的身高一致（1.80 m），所以手感也不会变。

using UnityEngine;

internal class NbGuard : MonoBehaviour
{
    /// 关卡表里的那条路线。现在**不再被逐点走**，只用来定义这个人的"地盘"：
    /// 航点在这条线段两侧 ±ZonePad 的带子里抽。见 PickWaypoint。
    internal Vector3[] Route;

    // ── 巡逻 ──
    internal float Speed = 0.95f;       // m/s，比玩家走路（2.30）慢一半多
    internal float Sweep = 0.55f;       // 扫灯节奏：数值越大转得越快、停得越短（关卡表 Sweep 列）
    internal float Range = 18f;         // 手电射程
    internal float Half = 11f;          // 光锥半角（度）
    internal float Damage = 25f;        // 每秒累积多少曝光（满 100 被抓）

    // ── 不规则游走 ──
    /// 巡逻区半宽：航点在这条路线两侧各 ZonePad 米之内抽。
    /// 7 m 大约是"他站在路线上，往两边各走七八步"的范围 ——
    /// 再宽几个人就会互相穿场，那 4 条路线就等于没设。
    internal const float ZonePad = 7f;

    /// 一段路的最短 / 最长距离。太短会变成原地抖动，太长看起来像在赶路。
    internal const float LegMin = 5f;
    internal const float LegMax = 16f;

    /// 离墙 / 离院子的边留多少。保安没有碰撞体，全靠这个数不让他走进砖墙里。
    internal const float WallInset = 2.2f;

    /// 出生点周围不让巡逻。Sense() 里本来就豁免了 6 m 安全区，
    /// 但如果他就站在那儿扫灯，玩家看到的是"我在光里怎么没事" —— 那是自相矛盾的画面。
    internal const float HomeClear = 8f;

    // ── 被照到之后 ──
    /// 追人速度。由 NbGame 按关卡从 2.55 拉到 3.45 —— **上限永远低于玩家跑步 3.91**，
    /// 所以"按住 Shift 一定跑得掉"这条承诺在这一关同样成立。
    internal float ChaseSpeed = 2.9f;

    /// 被照到之后到起跑之间的迟滞。0.3 秒不是手感调参，是**可读性**：
    /// 玩家需要一个"他抬头了"的瞬间，否则只会觉得自己凭空被撞了一下。
    internal const float SpotDelay = 0.30f;

    /// 脱身距离：光被挡住**并且**离他 ≥5 m 才算甩掉。
    /// 躲得近他会直接走到你藏的那丛灌木前面 —— 这正是玩家必须"跑起来"的原因。
    internal const float LoseDist = 5f;

    /// 找不到人之后最多在附近搜多久。没有这一条，玩家躲进 5 m 内的掩体后
    /// 会被永久钉在原地（他永远不走，你也永远不敢动）。
    internal const float AlertHold = 6f;

    /// 多近算被抓住。保安没有碰撞体（1.2 m 的走道要能从身边挤过去），
    /// 所以"撞上"只能靠距离判定；这个数约等于一个身位。
    internal const float CatchDist = 1.05f;

    /// 站住环顾时整个人转过来的角速度（度/秒）。55°/s 左右是"一个在张望的人"，
    /// 快了像在抽搐，慢了玩家会以为他卡住了。
    internal const float LookTurnSpeed = 70f;

    Transform _lamp;
    Light _lampLight;
    Transform _player;

    /// 模型右手。有它就跟着手走（看起来是"拿着"），没有就退回固定位置。
    Transform _hand;
    Vector3 _lampLocal;        // 手电在保安根节点下的位置（每帧平滑跟随手掌）
    NbGuardModel _model;       // 走路动画的播放器（追人时要加速，不然腿在原地慢走）

    // ── 游走状态 ──
    Vector3 _laneA, _laneB;    // 地盘的轴线（来自 Route 的首尾点）
    Vector3 _wp;               // 当前航点
    bool _hasWp;
    float _pause;              // 到点后站住多久（这段时间在 LookAround）
    float _lookT;              // 离下一次换"看的方向"还有多久
    float _bodyYawWant;        // 站住时想转到哪个朝向（世界 yaw，度）
    float _wobble, _wobbleAmp; // 走路时那条缓弯的相位 / 幅度
    Vector3 _stuckFrom;        // 卡死检测：上一次"确实挪动了"的位置
    float _stuckT;

    // ── 扫灯状态 ──
    float _scanYaw, _scanPitch;             // 当前灯角度（保安本地：yaw 绕 Y、pitch 绕 X）
    float _scanYawWant, _scanPitchWant;     // 目标角
    float _holdT;                           // 在这个方向上停多久

    float _spotT;              // 本轮被照到的累计时间（到 SpotDelay 起跑）
    float _lostT;              // 连续看不到人的时间
    float _alertT;             // 追击 / 搜索的剩余时间
    bool _chasing;
    Vector3 _lastSeen;

    internal bool Sees { get; private set; }
    internal bool Chasing { get { return _chasing; } }
    internal float _nextSound = -1;

    internal Vector3 Eye { get { return _lamp != null ? _lamp.position : transform.position; } }

    // ───────────────────────── 生成 ─────────────────────────

    internal static NbGuard Spawn(Transform parent, Vector3[] route, float speed, float sweep,
                                  float damage, float chaseSpeed)
    {
        GameObject go = new GameObject("Guard (auto)");
        go.transform.SetParent(parent, false);

        Vector3 at = route != null && route.Length > 0 ? route[0] : Vector3.zero;
        go.transform.position = new Vector3(at.x, 0f, at.z);

        // 身体：优先用 Resources/Guards/Walking 的真人模型（带走路动画）。
        // 读不到就退回旧的"圆柱 + 头 + 帽子" —— 这一关的玩法不能被一个资源缺失打断，
        // 而且几何体版本和模型版本**身高一致**（NbGuardArt.Height = 1.80），手电位置和判定都不用变。
        //
        // 两条都不加碰撞体：保安要是能挡住玩家，1.2 m 的走道和后院的灌木间隙会把人卡死；
        // 玩家应该能从他身边挤过去（真正危险的是光，不是他的身体）。
        GameObject model = NbGuardArt.Attach(go.transform);
        if (model == null)
        {
            Color coat = new Color(0.10f, 0.11f, 0.14f);
            Color skin = new Color(0.38f, 0.30f, 0.24f);
            Color cap  = new Color(0.07f, 0.08f, 0.10f);

            Nb.Cyl(go.transform, "Coat", new Vector3(0f, 0.86f, 0f), 0.60f, 1.72f, coat, false);
            Nb.Ball(go.transform, "Head", new Vector3(0f, 1.80f, 0f), 0.44f, skin, false);
            Nb.Box(go.transform, "Cap", new Vector3(0f, 2.02f, 0f), new Vector3(0.52f, 0.10f, 0.52f), cap, false);
        }

        // 手电：一个**实体**（握把 + 灯头 + 会发光的镜片 + 一小盏点光），挂在灯泡节点下。
        // 灯泡节点本身是保安的子物体 ⇒ 他转身，光就跟着转；额外的扫动由 Scan() 叠在 localRotation 上。
        //
        // **故意不把手电直接挂到手骨上。** 走路时手臂前后摆，会把 18 m 长的光锥一起甩成一条鞭子，
        // 玩家就读不出"光扫过去了没有" —— 而这一关要求他能读出来。
        // 现在的折中在 LateUpdate：**位置**跟着手掌走（看起来是拿在手里），**朝向**由保安控制。
        GameObject lampGo = new GameObject("Torch");
        lampGo.transform.SetParent(go.transform, false);
        lampGo.transform.localPosition = NbGuardArt.TorchLocal;
        lampGo.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

        NbGuardArt.BuildTorch(lampGo.transform);

        Light l = lampGo.AddComponent<Light>();
        l.type = LightType.Spot;
        l.range = 18f;
        l.spotAngle = 22f;
        l.innerSpotAngle = 12f;
        l.intensity = 7f;
        l.color = new Color(1f, 0.90f, 0.70f);
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;

        NbGuard g = go.AddComponent<NbGuard>();
        g.Route = route;
        g.Speed = speed;
        g.Sweep = sweep;
        g.Damage = damage;
        g.ChaseSpeed = chaseSpeed;
        g._lamp = lampGo.transform;
        g._lampLight = l;
        g._lampLocal = lampGo.transform.localPosition;
        g._hand = NbGuardArt.FindHand(model);
        g._model = model != null ? model.GetComponent<NbGuardModel>() : null;

        // 地盘 = 这条路线的首尾点。少于两个点就退化成"脚下一小圈"。
        g._laneA = at;
        g._laneB = route != null && route.Length > 1 ? route[route.Length - 1] : at;
        g._laneA.y = g._laneB.y = 0f;

        g._wp = go.transform.position;
        g._stuckFrom = go.transform.position;
        g._bodyYawWant = go.transform.eulerAngles.y;
        g._scanYaw = g._scanYawWant = 0f;
        g._scanPitch = g._scanPitchWant = -8f;
        g._holdT = 0.6f;
        g._wobble = Random.Range(0f, 6.283f);

        // 手在哪儿：模型是归一化过的，所以世界坐标直接换算到保安本地就行。
        // 换算失败（手骨在世界原点之类的怪情况）就保留 TorchLocal，不报错也不影响玩法。
        if (g._hand != null)
        {
            Vector3 hp = go.transform.InverseTransformPoint(g._hand.position);
            if (hp.sqrMagnitude > 0.01f && hp.y > 0.3f) g._lampLocal = hp;
        }

        return g;
    }

    // ───────────────────────── 每帧 ─────────────────────────

    void Update()
    {
        // 过关 / 失败之后所有人停在原地：否则"已经赢了"的画面里还有个人在冲你跑过来。
        NbGame game = NbGame.I;
        if (game != null && game.Finished) return;

        Sense();
        Think();

        if (_chasing)
        {
            // 跑步的脚步声由 AudioManager 按距离播放（见 AudioManager 的脚步系统），
            // 这里不再单独放一次性音效，否则会和它叠在一起。
            Chase();
        }
        else if (_pause > 0f) LookAround();
        else Walk();

        // 追人并且还看得见 → 光钉在玩家身上（这是玩家唯一能读出"他知道我在哪"的东西）；
        // 其余所有时候都在四处扫 —— 包括追丢了之后的那 6 秒搜索。
        if (_chasing && Sees) Aim();
        else Scan();

        // 步频跟着速度走：巡逻 1.0×、追人最多 3.5×。不改这一项，"加速"只体现在位移上，
        // 腿还在慢悠悠迈 —— 玩家看到的是一个人滑过来，而不是一个人跑过来。
        if (_model != null)
            _model.Rate = _chasing ? Mathf.Clamp(ChaseSpeed / Mathf.Max(0.1f, Speed), 1f, 3.5f) : 1f;

        Paint();
    }

    /// 手电的**位置**跟着手掌走（平滑），朝向不跟 —— 见 Spawn 里的注释。
    /// 放在 LateUpdate：走路动画是手动采样的，Update 里读到的还是上一帧的手。
    void LateUpdate()
    {
        if (_lamp == null) return;

        Vector3 want = _lampLocal;
        if (_hand != null)
        {
            Vector3 p = transform.InverseTransformPoint(_hand.position);
            // 手骨在手腕上；手电的握把是往**后** 0.18 m 的，所以灯头要放在手腕前 0.12 m，
            // 手掌才刚好落在握把上。放 0 的话握把会插进小臂里。
            want = p + transform.InverseTransformDirection(transform.forward * 0.12f);
        }

        float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
        _lamp.localPosition = Vector3.Lerp(_lamp.localPosition, want, k);
    }

    // ───────────────────────── 巡逻（不规则游走） ─────────────────────────

    /// 走向当前航点。**没有固定直线**：航点是当场抽的，路径还带一点横向缓弯。
    /// 避障只靠 Steer（他没有碰撞体），卡住了有 2 秒兜底换点。
    void Walk()
    {
        float dt = Time.deltaTime;
        if (!_hasWp) PickWaypoint();

        Vector3 p = transform.position;
        Vector3 d = new Vector3(_wp.x - p.x, 0f, _wp.z - p.z);
        float dist = d.magnitude;

        if (dist < 0.55f) { Arrive(); return; }

        Vector3 dir = d / dist;

        // 轻微横摆：把直线走成一条缓弯。幅度很小（≤0.18），终点不变，
        // Steer 仍然接管避障 —— 这条只负责让"他走的不是轨道"。
        _wobble += dt * 0.85f;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 want = (dir + side * (Mathf.Sin(_wobble) * _wobbleAmp)).normalized;

        Vector3 move = Steer(want);
        transform.position = p + move * (Speed * dt);

        // 转身不要瞬移：0.3 秒转过去，光锥扫过墙面的过程玩家能看见 —— 那是预警。
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(move, Vector3.up), 3.5f * dt);

        // 卡住兜底：2 秒内几乎没挪动就换一个航点。Steer 是沿表面滑的，
        // 偶尔会滑进灌木和墙的夹角里出不来 —— 没有这一条，他会站在墙角扫一晚上的灯。
        if (Nb.FlatDist(transform.position, _stuckFrom) > 0.55f)
        {
            _stuckFrom = transform.position;
            _stuckT = 0f;
        }
        else
        {
            _stuckT += dt;
            if (_stuckT > 2f) { _hasWp = false; _stuckT = 0f; }
        }
    }

    /// 到点：站住 1.2~3.0 秒，整个人转过去照别处（LookAround 接手）。
    void Arrive()
    {
        _hasWp = false;
        _pause = Random.Range(1.2f, 3.0f);
        _lookT = 0f;
        _stuckT = 0f;
    }

    /// 站住环顾。**整个人转过去**，而不是只拧手腕：
    /// 手电拿在手里，手腕拧 140° 的画面是错的（握把会横穿过手掌），
    /// 而"这个人转过身去了"也正是玩家敢横穿空地的那个窗口。
    void LookAround()
    {
        float dt = Time.deltaTime;
        _pause -= dt;
        _lookT -= dt;

        if (_lookT <= 0f)
        {
            _bodyYawWant = Mathf.Repeat(transform.eulerAngles.y + Random.Range(-135f, 135f), 360f);
            _lookT = Random.Range(0.9f, 1.7f);
        }

        float y = Mathf.MoveTowardsAngle(transform.eulerAngles.y, _bodyYawWant, LookTurnSpeed * dt);
        transform.rotation = Quaternion.Euler(0f, y, 0f);

        if (_pause <= 0f) _lookT = 0f;
    }

    /// 在"自己那条路线两侧 ±ZonePad"的带子里抽一个航点。
    /// 抽不出来（全是掩体 / 全在安全区 / 都太近）时退化成"朝随机方向走 LegMin 米"，
    /// 下一段再抽 —— 不要为了抽一个完美点把这一帧卡住。
    void PickWaypoint()
    {
        Vector3 along = _laneB - _laneA;
        along.y = 0f;
        float len = along.magnitude;
        Vector3 perp = len > 0.01f ? Vector3.Cross(Vector3.up, along / len) : Vector3.right;

        Vector3 best = Vector3.zero;
        float bestScore = -1f;

        for (int i = 0; i < 16; i++)
        {
            Vector3 q = _laneA + along * Random.value;
            q += perp * Random.Range(-ZonePad, ZonePad);
            q.y = 0f;
            q.x = Mathf.Clamp(q.x, NbScene.XMin + WallInset, NbScene.XMax - WallInset);
            q.z = Mathf.Clamp(q.z, NbScene.ZMin + WallInset, NbScene.ZMax - WallInset);

            float leg = Nb.FlatDist(q, transform.position);
            if (leg < LegMin || leg > LegMax) continue;
            if (!Standable(q)) continue;

            // 偏好 8 m 左右的一段路：太近像抖，太远像赶路。随机量让它不至于每段一样长。
            float score = 1f - Mathf.Abs(leg - 8f) / 12f + Random.value * 0.6f;
            if (score > bestScore) { bestScore = score; best = q; }
        }

        if (bestScore < 0f)
        {
            // 兜底：朝随机方向走 LegMin。Steer 会把它推到墙外，卡住检测会在 2 秒后重抽。
            float a = Random.Range(0f, 6.283f);
            best = transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * LegMin;
            best.x = Mathf.Clamp(best.x, NbScene.XMin + WallInset, NbScene.XMax - WallInset);
            best.z = Mathf.Clamp(best.z, NbScene.ZMin + WallInset, NbScene.ZMax - WallInset);
        }

        _wp = best;
        _hasWp = true;
        _pause = 0f;
        _stuckFrom = transform.position;
        _stuckT = 0f;
        _wobbleAmp = Random.Range(0.04f, 0.18f);
        _wobble = Random.Range(0f, 6.283f);
    }

    /// 这个点能不能站人（0.55 m 半径、膝盖到胸口这一段）。
    /// 顺带把出生点附近的 8 m 排掉 —— 见 HomeClear。
    bool Standable(Vector3 q)
    {
        NbGame game = NbGame.I;
        if (game != null && Nb.FlatDist(q, game.Home) < HomeClear) return false;

        return !Physics.CheckSphere(q + Vector3.up * 0.75f, 0.55f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    // ───────────────────────── 追击 / 搜索 ─────────────────────────

    /// 追击 / 搜索。目标永远是**最后看见的位置**，不是玩家当前坐标 ——
    /// 后者等于透视，玩家绕到灌木后面就变成了"他隔着一丛树直线撞过来"。
    void Chase()
    {
        if (_player != null &&
            Nb.FlatDist(transform.position, _player.position) <= CatchDist &&
            Clear(transform.position + Vector3.up * 0.25f, _player.position + Vector3.up * 0.15f))
        {
            Catch();
            return;
        }

        Vector3 p = transform.position;
        Vector3 flat = new Vector3(_lastSeen.x - p.x, 0f, _lastSeen.z - p.z);
        float d = flat.magnitude;
        if (d < 0.35f) return;          // 到点了：原地扫灯（Scan 会接手）

        Vector3 dir = flat / d;
        Vector3 move = Steer(dir);
        transform.position = p + move * (ChaseSpeed * Time.deltaTime);

        // 追的时候转身更快：他是在扑，不是在散步。
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(move, Vector3.up), 8f * Time.deltaTime);
    }

    /// 中间有没有东西挡着（抓人用）。少了这一句，玩家隔着一层 0.4 m 的墙
    /// 贴着保安站也会被"抓" —— 而这一关的掩体里有工具棚和柴垛，那种穿墙抓人的手感
    /// 会让人根本分不清"我到底躲好了没有"。
    bool Clear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float m = d.magnitude;
        if (m < 0.05f) return true;

        RaycastHit h;
        if (!Physics.Raycast(a, d / m, out h, m - 0.04f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return true;

        return h.collider != null && _player != null && h.collider.transform.root == _player.root;
    }

    /// 前方短距离球扫掠：撞到灌木 / 墙就沿表面滑开。
    /// 保安没有碰撞体（不改场景、也不挡人），所以这一句是**唯一**阻止他穿进灌木里的东西。
    Vector3 Steer(Vector3 dir)
    {
        RaycastHit h;
        Vector3 o = transform.position + Vector3.up * 0.55f;
        if (!Physics.SphereCast(o, 0.28f, dir, out h, 0.55f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return dir;
        if (h.collider == null) return dir;
        if (_player != null && h.collider.transform.root == _player.root) return dir;

        Vector3 n = h.normal;
        n.y = 0f;
        if (n.sqrMagnitude < 1e-4f) return dir;

        Vector3 slide = Vector3.ProjectOnPlane(dir, n.normalized);
        slide.y = 0f;
        return slide.sqrMagnitude > 1e-4f ? slide.normalized : dir;
    }

    // ───────────────────────── 扫灯 ─────────────────────────

    /// 四处扫：不断挑一个新的"看的方向"，线性转过去，停一会儿，再挑下一个。
    /// 用角度线性推进（MoveTowardsAngle）而不是 sin 叠加 —— sin 的节奏是死的，
    /// 玩家数两次就能踩点走；而"随机目标 + 随机停留"没有节拍可数。
    void Scan()
    {
        float dt = Time.deltaTime;
        bool alert = _alertT > 0f;      // 刚被甩掉、正在附近找人：转得更快、范围更大

        _holdT -= dt;
        if (_holdT <= 0f) NextLook();

        float turn = Mathf.Min(100f, Sweep * 95f) * (alert ? 1.6f : 1f);
        _scanYaw = Mathf.MoveTowardsAngle(_scanYaw, _scanYawWant, turn * dt);
        _scanPitch = Mathf.MoveTowards(_scanPitch, _scanPitchWant, turn * 0.55f * dt);

        _lamp.localRotation = Quaternion.Euler(_scanPitch, _scanYaw, 0f);
    }

    /// 挑下一个方向。**大部分时间扫前方一个扇形，约 1/5 的机会扫到侧面 / 身后** ——
    /// 那一下必须真的看见，因为"他背过身去了"是玩家唯一敢横穿空地的窗口。
    /// 俯仰也在变（−24°~+6°）：只左右扫的话，18 m 外的光斑是一条水平线，
    /// 玩家会以为自己贴着地面走就安全，而实际上灯在 1.4 m 高、压 20° 时远处什么都照不到。
    void NextLook()
    {
        bool alert = _alertT > 0f;
        // 95° 是"举着手电把手臂横出去"的极限：再大，握把就会从掌心里横穿过去。
        // 身后的那一片不靠拧手腕，靠站住时整个人转过去（LookAround）。
        float wide = alert ? 110f : 95f;

        if (Random.value < 0.22f)
            _scanYawWant = (Random.value < 0.5f ? -1f : 1f) * Random.Range(58f, wide);
        else
            _scanYawWant = Random.Range(-55f, 55f);

        _scanPitchWant = Random.Range(-6f, 24f);
        _holdT = alert ? Random.Range(0.28f, 0.75f) : Random.Range(0.6f, 1.6f);
    }

    /// 追人的时候把光**钉在玩家身上**：这是玩家唯一能读出"他知道我在哪"的东西。
    void Aim()
    {
        if (_player == null) { Scan(); return; }

        Vector3 to = _player.position + Vector3.up * 0.10f - _lamp.position;
        if (to.sqrMagnitude < 1e-4f) return;

        Vector3 local = transform.InverseTransformDirection(to.normalized);
        Quaternion want = Quaternion.LookRotation(local, Vector3.up);
        _lamp.localRotation = Quaternion.Slerp(_lamp.localRotation, want, 9f * Time.deltaTime);
    }

    // ───────────────────────── 感知 / 状态机 ─────────────────────────

    void Sense()
    {
        Sees = false;

        NbGame game = NbGame.I;
        if (_player == null && game != null) _player = game.Player;
        if (_player == null) return;

        // 被抓之后的 3 秒无敌期里，光当没照到：否则他会在你无敌的这 3 秒里
        // 一直贴着你、把"无敌"变成"被一个甩不掉的人顶着跑"。
        if (game != null && game.Invuln > 0f) return;

        // 出生点 6 m 内是安全区（和猫共用同一条规矩）。没有这一条，
        // 第 8 关那条中轴路线一上场，玩家一睁眼就站在手电里。
        if (game != null && Nb.FlatDist(_player.position, game.Home) < 6f) return;

        Vector3 eye = _lamp.position;
        // 瞄胸口而不是脚下：玩家只有 0.30 m 高，瞄脚会先打到地面，永远判"被挡住"。
        Vector3 to = _player.position + Vector3.up * 0.10f - eye;
        float dist = to.magnitude;

        if (dist < 0.05f || dist > Range) return;
        if (Vector3.Angle(_lamp.forward, to) > Half) return;

        // 被掩体挡住 ⇒ 看不见。射线短 0.15 m，免得终点落在玩家皮肤上还算"被挡"。
        RaycastHit hit;
        if (Physics.Raycast(eye, to / dist, out hit, dist - 0.15f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.root != _player.root) return;
        }

        Sees = true;
    }

    /// 状态机：看见 → （迟滞 0.3 s）起跑 → 看不见 → 走到最后位置 → 到点/够远就放弃。
    void Think()
    {
        float dt = Time.deltaTime;

        if (Sees)
        {
            _lostT = 0f;
            _alertT = AlertHold;
            _lastSeen = _player != null ? _player.position : transform.position;

            if (!_chasing)
            {
                _spotT += dt;
                if (_spotT >= SpotDelay) Begin();
            }
            return;
        }

        _spotT = 0f;
        if (!_chasing) return;

        _lostT += dt;
        _alertT -= dt;

        float d = _player != null ? Nb.FlatDist(transform.position, _player.position) : 999f;

        // 光被挡住 **并且** 人在 5 m 之外 ⇒ 脱身。这两条必须同时成立：
        // 只判遮挡的话，蹲在 2 m 的灌木后就能让一个正在冲过来的人当场忘掉你。
        if (_lostT > 1.2f && d >= LoseDist) { End(); return; }

        // 兜底：搜了 6 秒还是没结果。玩家可以靠"躲得近"拖住他，但不能把他永久钉在那儿。
        if (_alertT <= 0f) End();
    }

    void Begin()
    {
        _chasing = true;
        _alertT = AlertHold;
        _lostT = 0f;

        Debug.LogFormat("[Guard] 手电扫到玩家（{0:F1} m），开始追击：{1:F2} m/s。",
            _player != null ? Nb.FlatDist(transform.position, _player.position) : -1f, ChaseSpeed);

        if ( _nextSound <= Time.time)
        {
            AudioManager.PlaySound(SoundType.MAN_yell, 0.8f);
            _nextSound = Time.time + AudioManager.GetSoundLength(SoundType.MAN_yell);
        }
        
    }

    void End()
    {
        AudioManager.StopSound();

        _chasing = false;
        _spotT = 0f;
        _lostT = 0f;
        _alertT = 0f;

        // 从"钉着玩家"切回"四处扫"：把当前的灯角度接过来，别让 18 m 的光锥瞬移几十度 ——
        // 而那正是玩家最需要看懂的一瞬间。
        if (_lamp != null)
        {
            Vector3 e = _lamp.localEulerAngles;
            _scanYaw = Norm180(e.y);
            _scanPitch = Norm180(e.x);
            _scanYawWant = _scanYaw;
            _scanPitchWant = _scanPitch;
            _holdT = 0.35f;
        }

        // 追丢之后先站住扫一圈再继续走：他会"在原地找一下"，而不是立刻若无其事地巡逻。
        _hasWp = false;
        _pause = 1.0f;
        _lookT = 0f;
    }

    static float Norm180(float a)
    {
        return Mathf.Repeat(a + 180f, 360f) - 180f;
    }

    /// 撞上玩家：交给 NbGame 结算（扣血 / 红闪 / 所有人回起点 / 失败面板）。
    void Catch()
    {
        NbGame game = NbGame.I;
        if (game == null) { End(); return; }

        End();
        game.CaughtByGuard(this);
    }

    /// 看见人的一瞬间手电变红、变亮 —— 这是玩家唯一的即时反馈。
    /// 只靠 HUD 上那根曝光条不够：抬头看条的时候人已经死了。
    void Paint()
    {
        if (_lampLight == null) return;

        if (Sees || _chasing)
        {
            _lampLight.color = new Color(1f, 0.34f, 0.24f);
            _lampLight.intensity = 10f;
        }
        else
        {
            _lampLight.color = new Color(1f, 0.90f, 0.70f);
            _lampLight.intensity = 7f;
        }
    }

    /// 换关时把保安放回路线起点，并清掉这一关的游走状态。
    internal void ResetToStart()
    {
        End();
        _pause = 0f;
        _holdT = 0.6f;
        if (Route == null || Route.Length == 0) return;

        Vector3 at = new Vector3(Route[0].x, 0f, Route[0].z);
        transform.position = at;
        _wp = at;
        _stuckFrom = at;
        _hasWp = false;
        _stuckT = 0f;
        _wobble = Random.Range(0f, 6.283f);
        _bodyYawWant = transform.eulerAngles.y;
    }
}
