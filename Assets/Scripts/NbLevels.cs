// @role Difficulty tables: 10 levels per scene (cheese/cats/speed/sight/guards/exposure/rot/door).
// @calls NbScene
// @tune BiteDamage:int:1-50:health lost per catch
// @tune StartHp:int:10-500:starting health
// @tune Count:int:1-20:levels per scene

// NbLevels.cs — 三个场景 × 10 关的难度表
//
// 一关 = 一组数字，没有别的。骨架四个字段三个场景共用：
//   ① 场上有几个目标（= 必须带回几个）
//   ② 场上有几只猫
//   ③ 猫有多快（巡逻 / 追人）
//   ④ 猫看多远
// 后面几个字段只有对应场景才读：
//   Guards / Exposure / Sweep  → 后院（保安数量、曝光速率、手电扫灯节奏）
//   Rot / Lamps                → 厨房（食物腐烂秒数、亮着的灯管数）
//
// 速度这一列是有依据的，不是随手填的：
//   玩家走着 2.30 m/s、跑着 3.91 m/s（见 NbPlayer.WalkSpeed / RunMul）。
//   · 巡逻速度 **始终低于走路速度** ⇒ 正常走就能拉开，猫不是"贴着你转的电锯"；
//   · 追击速度 **高于走路、始终低于跑步** ⇒ 被抓过一次之后必须按 Shift 才甩得掉。
//   第 10 关 3.25 仍低于 3.91，所以"跑一定能脱身"这条底线在最后一关也成立 ——
//   难度来自"你跑不了那么久、跑的方向又是错的"，而不是"这游戏跑不过猫"。
//
// 后院的速度比街区**低一档**：那里真正的威胁是手电，不是爪子。
// 猫跑太快会让玩家只敢贴墙，而贴墙正是保安最想看到的行为。

using UnityEngine;

internal struct NbLevelCfg
{
    internal int No;          // 关卡号 1…10
    internal int Cheese;      // 目标数 = 需要带回的数量
    internal int Cats;        // 猫的数量
    internal float Patrol;    // 巡逻速度 m/s
    internal float Chase;     // 追击速度 m/s
    internal float Sight;     // 视野距离 m

    internal int Guards;      // 保安数量（后院）
    internal float Exposure;  // 被手电照到时每秒累积多少曝光（后院）
    internal float Sweep;     // 手电扫灯的节奏：越大转得越快、停得越短（后院）
    internal float Rot;       // 食物从进关到腐烂的秒数（厨房）
    internal int Lamps;       // 亮着的灯管数量（厨房）

    internal float DoorGap;   // 两次开门之间隔多少秒（冰箱）
    internal float DoorOpen;  // 门开着（强光灌进来）持续多少秒（冰箱）
}

internal static class NbLevels
{
    internal const int Count = 10;

    /// 每被抓一次扣多少血。100 血 ⇒ 一关之内最多被咬 10 口。
    internal const int BiteDamage = 10;

    internal const int StartHp = 100;

    // ───────────────────────── 01 居民街区 ─────────────────────────
    // 两条曲线分开走：奶酪 = 关卡数 + 1（封顶 10），猫 = 关卡数（封顶 10）。
    // 两者相乘 ⇒ 第 10 关不是"第 1 关多几只猫"，而是"路更长 + 可躲的道更少"。

    static readonly NbLevelCfg[] Street =
    {
        //  No  Cheese  Cats  Patrol  Chase  Sight
        Lv( 1,    2,     1,   1.10f,  2.20f, 10f),
        Lv( 2,    3,     2,   1.10f,  2.30f, 11f),
        Lv( 3,    4,     3,   1.15f,  2.40f, 11f),
        Lv( 4,    5,     4,   1.20f,  2.50f, 12f),
        Lv( 5,    6,     5,   1.25f,  2.60f, 12f),
        Lv( 6,    7,     6,   1.30f,  2.75f, 13f),
        Lv( 7,    8,     7,   1.35f,  2.90f, 14f),
        Lv( 8,    9,     8,   1.40f,  3.00f, 14f),
        Lv( 9,   10,     9,   1.45f,  3.10f, 15f),
        Lv(10,   10,    10,   1.55f,  3.25f, 16f),
    };

    // ───────────────────────── 02 夜巡后院 ─────────────────────────
    // 这一关**没有猫**（Cats 恒为 0）：威胁全部来自手电。
    // 被光扫到 0.3 秒后，那个人会以 2.55~3.45 m/s 冲过来 —— 比玩家走路（2.30）快、
    // 比跑步（3.91）慢，所以"按住 Shift 跑到 5 m 之外的掩体后面"是唯一正确的反应。
    //
    // Exposure 18→38 只在**一直站在光里**时才致命（5.5 秒 → 2.6 秒）；
    // 通常他会在那之前就撞上你（-10 血）。两根条各管一件事：
    // 曝光条管"别在光里站住"，保安本人管"别让他追到"。
    //
    // Guards **从 2 起、封顶 4**。下限抬到 2 是因为"一个人巡逻"读不出
    // "这里有人在找东西"——玩家只当那是一盏会动的灯。两个人才会形成"这边扫过去、
    // 那边扫过来"的空档，而**空档正是这一关真正要玩家去读的东西**。
    // 上限 4 受 patrol 路线数限制（NbGame.GuardLanes 四条），再多就得先加路线。

    static readonly NbLevelCfg[] Yard =
    {
        //  No  Cheese  Cats  Patrol  Chase  Sight  Guards  Expo  Sweep
        LvY( 1,    2,     0,   1.00f,  2.10f,  9f,     2,    18f, 0.45f),
        LvY( 2,    3,     0,   1.00f,  2.20f, 10f,     2,    20f, 0.50f),
        LvY( 3,    4,     0,   1.05f,  2.30f, 10f,     2,    22f, 0.58f),
        LvY( 4,    5,     0,   1.05f,  2.40f, 11f,     3,    24f, 0.66f),
        LvY( 5,    6,     0,   1.10f,  2.50f, 11f,     3,    26f, 0.72f),
        LvY( 6,    7,     0,   1.10f,  2.60f, 12f,     3,    28f, 0.80f),
        LvY( 7,    8,     0,   1.15f,  2.75f, 12f,     3,    30f, 0.90f),
        LvY( 8,    9,     0,   1.15f,  2.85f, 13f,     4,    32f, 1.00f),
        LvY( 9,   10,     0,   1.20f,  3.00f, 13f,     4,    34f, 1.12f),
        LvY(10,   10,     0,   1.25f,  3.10f, 14f,     4,    38f, 1.25f),
    };

    // ───────────────────────── 03 深夜厨房 ─────────────────────────
    // Rot 是这一关的独有轴：**全场食物同时开始倒计时**，所以最优解不是"先捡最近的"，
    // 而是"先冲最远的那块"。60 → 26 秒的压缩，逼玩家在开局十秒内就规划完整条路线。
    // Lamps 3→8：第 8 关起八根灯管全亮，五条侧走道没有一条是暗的，只剩主走道能走。

    static readonly NbLevelCfg[] Kitchen =
    {
        //  No  Cheese  Cats  Patrol  Chase  Sight   Rot   Lamps
        LvK( 1,    2,     1,   1.10f,  2.20f, 10f,    60f,   3),
        LvK( 2,    3,     1,   1.10f,  2.30f, 10f,    56f,   3),
        LvK( 3,    4,     2,   1.15f,  2.40f, 11f,    52f,   4),
        LvK( 4,    5,     2,   1.15f,  2.50f, 11f,    48f,   5),
        LvK( 5,    6,     2,   1.20f,  2.60f, 12f,    44f,   5),
        LvK( 6,    7,     3,   1.20f,  2.75f, 12f,    40f,   6),
        LvK( 7,    8,     3,   1.25f,  2.90f, 13f,    36f,   7),
        LvK( 8,    9,     3,   1.30f,  3.00f, 13f,    32f,   8),
        LvK( 9,   10,     4,   1.35f,  3.10f, 14f,    30f,   8),
        LvK(10,   10,     4,   1.40f,  3.25f, 14f,    26f,   8),
    };

    // ───────────────────────── 03 冰箱（当前第三格） ─────────────────────────
    // 这一关**没有猫**（Cats 恒为 0），唯一的威胁是门：
    //   DoorGap  两次开门之间隔多久 —— 15 → 8 秒。这不是"喘息时间"，而是
    //            "你能连续跑几趟"：跑完一趟（爬一层、捡两件、躲一次）大约 12 秒，
    //            所以第 8 关之后，一次开门会把你正在做的事劈成两半。
    //   DoorOpen 门开多久 —— 1.4 → 2.5 秒。这段时间里**只在开门那一刻判定一次**，
    //            不是持续照射：玩家要么已经躲好了，要么没有，中间再冲过去不算数。
    //            所以这一列实际控制的是"你有多久不敢动"，越长越像被钉在原地。
    //
    // 3 秒预警是写死的（NbGame.DoorWarnTime），不随关卡变 ——
    // 它是玩家唯一能建立的条件反射，动了这个数，整关的节奏就没法靠身体记了。

    static readonly NbLevelCfg[] Fridge =
    {
        //  No  Cheese Cats  Gap   Open     ← Gap = 两次开门之间的秒数（含 3 秒预警）
        LvF( 1,   2,    0,  15.0f, 1.4f),
        LvF( 2,   3,    0,  14.0f, 1.5f),
        LvF( 3,   4,    0,  13.0f, 1.6f),
        LvF( 4,   5,    0,  12.5f, 1.7f),
        LvF( 5,   6,    0,  12.0f, 1.8f),
        LvF( 6,   7,    0,  11.0f, 1.9f),
        LvF( 7,   8,    0,  10.5f, 2.0f),
        LvF( 8,   9,    0,  10.0f, 2.2f),
        LvF( 9,  10,    0,   9.0f, 2.3f),
        LvF(10,  12,    0,   8.0f, 2.5f),
    };

    // ───────────────────────── 工厂 ─────────────────────────

    static NbLevelCfg Lv(int no, int cheese, int cats, float patrol, float chase, float sight)
    {
        NbLevelCfg c = new NbLevelCfg();
        c.No = no; c.Cheese = cheese; c.Cats = cats;
        c.Patrol = patrol; c.Chase = chase; c.Sight = sight;
        c.Exposure = 0f; c.Sweep = 0f; c.Rot = 0f;
        return c;
    }

    static NbLevelCfg LvY(int no, int cheese, int cats, float patrol, float chase, float sight,
                          int guards, float exposure, float sweep)
    {
        NbLevelCfg c = Lv(no, cheese, cats, patrol, chase, sight);
        c.Guards = guards;
        c.Exposure = exposure;
        c.Sweep = sweep;
        return c;
    }

    static NbLevelCfg LvK(int no, int cheese, int cats, float patrol, float chase, float sight,
                          float rot, int lamps)
    {
        NbLevelCfg c = Lv(no, cheese, cats, patrol, chase, sight);
        c.Rot = rot;
        c.Lamps = lamps;
        return c;
    }

    static NbLevelCfg LvF(int no, int food, int cats, float gap, float open)
    {
        // 冰箱没有巡逻者，Patrol/Chase/Sight 一律给 0 —— 留着别的场景的默认值会让日志误导人。
        NbLevelCfg c = Lv(no, food, cats, 0f, 0f, 0f);
        c.DoorGap = gap;
        c.DoorOpen = open;
        return c;
    }

    // ───────────────────────── 取表 ─────────────────────────

    /// 按当前场景取第 level 关。越界夹回 1…10，绝不返回空 ——
    /// 这一关的所有数值都从这里出，返回空会让"猫不生成 / 目标数为 0"变成静默的空白关卡。
    internal static NbLevelCfg Get(int level)
    {
        return Get(NbScene.Kind, level);
    }

    internal static NbLevelCfg Get(NbSceneKind kind, int level)
    {
        NbLevelCfg[] t = Table(kind);
        int i = Mathf.Clamp(level - 1, 0, t.Length - 1);
        return t[i];
    }

    static NbLevelCfg[] Table(NbSceneKind kind)
    {
        switch (kind)
        {
            case NbSceneKind.Yard:    return Yard;
            case NbSceneKind.Kitchen: return Kitchen;
            case NbSceneKind.Fridge:  return Fridge;
            default:                  return Street;
        }
    }

    internal static bool IsLast(int level)
    {
        return level >= Count;
    }
}
