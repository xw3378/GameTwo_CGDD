// @role Runtime practical lights: lamp heads, porch bulb, fridge shelf strips. No scene-file edits.
// @calls NbScene

// NbLamps.cs — 给"看起来是灯"的东西补上真正的光源
//
// 为什么需要：
//   路灯、门廊灯、冰箱灯带在几何上都只是一块**自发光材质**（Nb.MatEmissive），
//   它们自己亮，但不照亮任何东西 —— 于是夜里走道上是均匀的一层月光，
//   灯下和灯之间没有差别，玩家判断不出"我现在站在灯下"。
//   真实场景里，人眼最先读到的是**光池**：地面上一圈暖黄、周围迅速衰减。
//
// 做法：在灯罩位置挂一盏点光，颜色/强度按场景调。**不加阴影**：
//   一盏点光开阴影就是 6 面 cube map，四个路灯直接翻倍开销；
//   而这些灯都不高（3.1 m），夜里也没有会被投出长影的东西，得不偿失。
//
// 全部是运行时生成，不写场景文件 —— 和这个工程其它系统一致。

using UnityEngine;

internal static class NbLamps
{
    internal static void Add(Transform root, NbSceneKind kind)
    {
        int n = 0;
        switch (kind)
        {
            case NbSceneKind.Street: n = Street(); break;
            case NbSceneKind.Yard:   n = Yard(root); break;
            case NbSceneKind.Fridge: n = Fridge(root); break;
        }
        if (n > 0) Debug.LogFormat("[Nb] 补了 {0} 盏实光源（{1}）。", n, kind);
    }

    /// 街区：每根灯杆的灯罩下面一盏暖黄点光。
    /// baked 版本的灯也在场景里，所以按名字扫全场，不按 root 扫。
    static int Street()
    {
        int n = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t == null || !t.name.StartsWith("Lamp ")) continue;
            Transform glass = t.Find("Glass");
            if (glass == null) continue;
            if (t.Find("Lamp Light") != null) continue;

            GameObject go = new GameObject("Lamp Light");
            go.transform.SetParent(t, false);
            go.transform.position = glass.position + Vector3.down * 0.10f;

            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.00f, 0.80f, 0.52f);
            l.intensity = 24f;
            l.range = 14f;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            n++;
        }
        return n;
    }

    /// 后院：门廊灯泡 + 工具棚门口一盏。后院整体是暗的（保安的手电才是主角），
    /// 所以这两盏只负责给出"房子在哪一侧"的方位感，强度压得很低。
    static int Yard(Transform root)
    {
        if (root == null) return 0;
        int n = 0;
        n += Bulb(root, "Porch Light", new Vector3(73.6f, 2.35f, -0.35f), new Color(1.00f, 0.78f, 0.50f), 6f, 10f);
        n += Bulb(root, "Shed Light", new Vector3(44.5f, 2.05f, -15.35f), new Color(1.00f, 0.72f, 0.42f), 3.2f, 6f);
        return n;
    }

    /// 冰箱：每两层搁板下补一盏冷白点光，让"层"这件事在画面里立起来。
    /// 灯带本身是自发光的，但照不亮货架；没有这层实光，六层会糊成一片灰。
    static int Fridge(Transform root)
    {
        if (root == null) return 0;
        int n = 0;
        for (int layer = 1; layer <= NbFridge.Layers; layer++)
        {
            float y = NbFridge.Top(layer) - 0.10f;
            n += Bulb(root, "Chill " + layer + " a", new Vector3(NbFridge.CX, y, NbFridge.CZ - 6.0f),
                new Color(0.84f, 0.92f, 1.00f), 1.5f, 8f);
            n += Bulb(root, "Chill " + layer + " b", new Vector3(NbFridge.CX, y, NbFridge.CZ + 2.5f),
                new Color(0.84f, 0.92f, 1.00f), 1.2f, 7f);
        }
        return n;
    }

    static int Bulb(Transform root, string name, Vector3 at, Color col, float power, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = at;

        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = col;
        l.intensity = power;
        l.range = range;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
        return 1;
    }
}
