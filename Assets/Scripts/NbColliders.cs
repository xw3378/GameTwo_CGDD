// NbColliders.cs — 运行时给「换过模型的 12 栋楼」重量一次碰撞体
//
// 为什么需要这一步（这不是"保险起见"，是修一个真实存在的 bug）：
//
//   StreetFromModels.AddBodyCollider 原来用 **Renderer.bounds**（世界 AABB）量楼体，
//   再除以 holder 的缩放 k 存进 BoxCollider.size。可 BoxCollider.size 是**本地空间**的量，
//   而 holder 上还挂着 0°/90°/270° 的偏航 —— 存进去的"世界尺寸"会**被再转一次**。
//   日志是干净的（它打印的是量到的世界尺寸 16.9 x 10.1 x 7.7），可落到场景里的盒子
//   实际是 7.7 x 10.1 x 16.9：横竖**转置**了。
//
//   后果有两个，而且方向相反：
//     · 长边方向少了 4.6 m 覆盖 ⇒ 玩家能从楼的正侧面走进楼里（穿模）
//     · 短边方向多了 4.6 m      ⇒ 过道里凭空站着一道隐形墙（撞上去、猫也贴着它滑）
//
// 修法：把每个网格的**本地包围盒** 8 个角乘上 (holder.worldToLocalMatrix × renderer.localToWorldMatrix)，
// 在 holder 的本地空间里重新求一次 AABB。这样旋转和缩放都在矩阵里自然抵消，
// 存进 size/center 就是对的。编辑器脚本也同步改成同样的算法（下次重建也不会再错）。
//
// 2026-10-17 补：**按名字在整个层级里找楼，而不是只认 root 的直接子物体。**
//   实测过的一次事故：Hierarchy 里一次误拖把 industrial_03 拖进了 west_02，
//   于是 west_02 的包围盒把工业仓库一起圈了进去 —— 本地 x 从 3.32 变成 12.39，
//   世界长度 7.71 → 28.77 m，横着堵死整条 H1 过道（奶酪 #8、猫 5 号路线全在盒子里）。
//   同时 industrial_03 因为不再是直接子物体而被"跳过"（日志还写着"量不到网格"，很误导）。
//   现在两个都按"独立一栋"处理，并且会把这种嵌套单独警告出来。
//
// 这一步只读场景、只改 BoxCollider 的 center/size，不改任何 Transform / 渲染 / 层。

using System.Collections.Generic;
using UnityEngine;

internal static class NbColliders
{
    // 老脚本建出来的根节点名；改名了就在日志里说清楚，别静默跳过
    const string RootName = "Street Models (auto)";

    // 楼的名字前缀（west_00 / abandoned_01 / industrial_03 …）
    static readonly string[] Prefixes = { "west_", "abandoned_", "industrial_" };

    struct Foot
    {
        internal string Name;
        internal Bounds Box;
    }

    /// <summary>
    /// 校正之后每栋楼的**世界 AABB**（平面范围就是它的占地）。
    /// 小地图（NbMap）直接画这个，不再用「3 列 × 4 排 + 每列一个宽度」那套网格近似 ——
    /// 12 栋楼是三个不同模型，实际是 16.9×7.7 / 13.1×13.2 / 16.9×13.1 混排，
    /// 用统一网格画出来每块都会偏进过道里，看着就是"方块和楼对不上"。
    /// Normalize() 每局开头重填一次，所以这里永远是这一帧场景里的真值。
    /// </summary>
    internal static readonly List<Bounds> Footprints = new List<Bounds>();

    internal static int Normalize()
    {
        Footprints.Clear();

        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarningFormat("[Nb] 场景里找不到「{0}」，跳过楼体碰撞体校正。", RootName);
            return 0;
        }

        // ① 先在整个层级里把「楼」找齐（含被误拖进另一栋里的那种）
        List<Transform> holders = FindHolders(root.transform);
        if (holders.Count == 0)
        {
            Debug.LogWarning("[Nb] 「Street Models (auto)」下一栋楼都没找到，跳过校正。");
            return 0;
        }

        var feet = new List<Foot>();
        int fixedN = 0, added = 0, skipped = 0;

        for (int i = 0; i < holders.Count; i++)
        {
            Transform holder = holders[i];
            if (holder == null) continue;

            // ② 被拖到别处的，单独说一句 —— 不是致命错误，但值得让作者知道
            if (holder.parent != root.transform)
            {
                Debug.LogWarningFormat(
                    "[Nb] 楼「{0}」没挂在「{1}」下面，而是挂在「{2}」下面。" +
                    "已按独立一栋处理；建议在 Hierarchy 里把它拖回「{1}」（多半是拖拽时手滑）。",
                    holder.name, RootName,
                    holder.parent != null ? holder.parent.name : "?");
            }

            Bounds local;
            if (!LocalBounds(holder, holders, out local) || local.size.sqrMagnitude <= 0.0001f)
            {
                skipped++;
                Debug.LogWarningFormat("[Nb] 楼「{0}」量不到网格（下面一个 Renderer 都没有？），跳过。", holder.name);
                continue;
            }

            BoxCollider bc = holder.GetComponent<BoxCollider>();
            if (bc == null)
            {
                bc = holder.gameObject.AddComponent<BoxCollider>();
                added++;
            }

            Vector3 oldSize = bc.size;
            bc.isTrigger = false;
            bc.enabled = true;
            bc.center = local.center;
            bc.size = local.size;

            // 世界 AABB（旋转后的）：日志里报这个，才方便和"过道有多宽"对得上
            Bounds world = ToWorldBounds(holder, bc);
            Foot f;
            f.Name = holder.name;
            f.Box = world;
            feet.Add(f);
            Footprints.Add(world);
            fixedN++;

            Debug.LogFormat("[Nb] 楼体碰撞体校正「{0}」：本地 {1:F2}×{2:F2}×{3:F2} → 世界 {4:F2}×{5:F2}×{6:F2}，" +
                            "x {7:F2}…{8:F2}  z {9:F2}…{10:F2}（原 size {11:F2}×{12:F2}×{13:F2}）",
                holder.name, bc.size.x, bc.size.y, bc.size.z,
                world.size.x, world.size.y, world.size.z,
                world.min.x, world.max.x, world.min.z, world.max.z,
                oldSize.x, oldSize.y, oldSize.z);
        }

        Debug.LogFormat("[Nb] 楼体碰撞体：校正 {0} 栋（新增 {1} 个 BoxCollider），跳过 {2} 栋（量不到网格）。",
            fixedN, added, skipped);

        CheckFree(feet, "出生点", new[] { Nb.PlayerStart });
        CheckFree(feet, "奶酪", Nb.CheeseSpots);
        CheckFree(feet, "猫的路线", Flatten(Nb.CatRoutes));
        return fixedN;
    }

    // ───────────────────────── 找楼 ─────────────────────────

    /// 递归找所有名字像楼的对象。**不能用 GetChild(i)** —— 只认直接子物体时，
    /// 一次误拖就会让"被拖走的那栋"消失、"外面的那栋"吃掉它的尺寸（见文件头）。
    static List<Transform> FindHolders(Transform root)
    {
        var list = new List<Transform>();
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t == root) continue;
            if (!IsBuildingName(t.name)) continue;
            list.Add(t);
        }
        return list;
    }

    /// 只认 `前缀 + 纯数字`（west_00 / abandoned_01 / industrial_03…）。
    /// 不能只判 StartsWith：FBX 里常有叫 "west_wall_01" 的子网格，
    /// 认错一次就会把一栋楼拆成两个盒子（父的那半反而变小）。
    static bool IsBuildingName(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        for (int i = 0; i < Prefixes.Length; i++)
        {
            string pre = Prefixes[i];
            if (!n.StartsWith(pre)) continue;

            string tail = n.Substring(pre.Length);
            int sp = tail.IndexOf(' ');            // 重名时 Unity 会加 " (1)"
            if (sp > 0) tail = tail.Substring(0, sp);
            if (tail.Length == 0) return false;

            for (int c = 0; c < tail.Length; c++)
                if (tail[c] < '0' || tail[c] > '9') return false;
            return true;
        }
        return false;
    }

    // ───────────────────────── 量本地包围盒 ─────────────────────────

    /// 在 holder 的本地空间里求「本栋楼自己的网格」的 AABB。
    /// 用 sharedMesh.bounds 而不是 Renderer.bounds：后者是**世界**轴对齐盒，已经被旋转过，
    /// 再拿去当本地尺寸就会得到本文开头说的那种转置盒子。
    /// holders 用来排除嵌在本栋里的**另一栋楼**（否则两栋会互相圈进对方的盒子里）。
    static bool LocalBounds(Transform holder, List<Transform> holders, out Bounds local)
    {
        local = new Bounds();
        bool any = false;
        Matrix4x4 toLocal = holder.worldToLocalMatrix;

        Renderer[] rs = holder.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r.GetComponent<Camera>() != null) continue;
            if (InNestedHolder(holder, r.transform, holders)) continue;

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

    /// 这个 renderer 是不是属于"嵌在本栋里面的另一栋楼"
    static bool InNestedHolder(Transform holder, Transform r, List<Transform> holders)
    {
        for (int i = 0; i < holders.Count; i++)
        {
            Transform h = holders[i];
            if (h == null || h == holder) continue;
            if (!h.IsChildOf(holder)) continue;     // 只关心真正嵌在本栋层级里的
            if (r.IsChildOf(h)) return true;
        }
        return false;
    }

    static Bounds ToWorldBounds(Transform holder, BoxCollider bc)
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

    // ───────────────────────── 自检：路面上不该有楼 ─────────────────────────

    /// 出生点 / 奶酪 / 猫的路线全都在过道上。任何一项落进楼体盒子里，
    /// 表现都是"一开局就卡在墙里"或"奶酪永远捡不到" —— 而画面看上去没有任何异常。
    static void CheckFree(List<Foot> feet, string what, Vector3[] points)
    {
        if (points == null) return;
        int bad = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 p = points[i];
            // 只比平面：楼体盒子往上 10 m，用 3D 包含会把站在楼边的点也算进去
            for (int b = 0; b < feet.Count; b++)
            {
                Bounds w = feet[b].Box;
                if (p.x < w.min.x - 0.25f || p.x > w.max.x + 0.25f) continue;
                if (p.z < w.min.z - 0.25f || p.z > w.max.z + 0.25f) continue;
                Debug.LogWarningFormat("[Nb] 自检·{0} #{1}（{2:F2}, {3:F2}）落在「{4}」的碰撞体里 —— 会卡住/够不到。",
                    what, i + 1, p.x, p.z, feet[b].Name);
                bad++;
                break;
            }
        }
        if (bad == 0 && points.Length > 0)
            Debug.LogFormat("[Nb] 自检·{0} {1} 个点全部落在空地/过道上（不在任何楼体盒子里）。", what, points.Length);
    }

    static Vector3[] Flatten(Vector3[][] routes)
    {
        if (routes == null) return null;
        var list = new List<Vector3>();
        for (int i = 0; i < routes.Length; i++)
            if (routes[i] != null) list.AddRange(routes[i]);
        return list.ToArray();
    }
}
