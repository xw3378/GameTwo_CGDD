// @role Builds fridge food out of raw geometry (carrot, tomato, apple, ...); decoration only, no colliders.
// @calls Nb

// NbFood.cs — 冰箱里的食物（纯几何体拼的，不依赖任何模型资源）
//
// 为什么不用模型：Resources/GameProps 里只有「Cat walk」和「Cheese_0001_LOD0」两样东西。
// 冰箱这一关要的是「瓜果蔬菜牛奶」，用模型既要再导一批 FBX、又要再调一遍归一化/贴地，
// 而这一关的目标物只有 0.2~0.3 m 大、玩家又只能贴到 0.2 m 的距离看 —— 几何体拼完全够，
// 而且颜色就是我调的那几个数，不存在"贴图偏色"。
//
// 每个食物的约定：
//   · 根物体原点在**底面中心**（和 Nb.Box 那套一致），Spawn 传进来的 at 就是它站的地方
//   · 尺寸以 s 为基准（s=0.26 时一个番茄约 0.13 m，对 0.30 m 高的老鼠是半个身位）
//   · 所有零件都是装饰层（无碰撞体、不参与射线）—— 目标物不该挡路，也不该挡门光
//
// **不要给这里加碰撞体。** 收集判定靠 0.62 m 的平面距离，加了碰撞体反而会把玩家顶开，
// 而这一关的走道只有层高 0.53 m，顶一下就够不着了。

using UnityEngine;

internal static class NbFood
{
    /// 品种表。名字会进日志和 HUD 提示，所以用全大写英文（TMP 用的是那套只含 ASCII 的字体）。
    internal static readonly string[] Names =
    {
        "CARROT", "TOMATO", "APPLE", "LEMON", "MILK", "LETTUCE",
        "CORN", "BERRY", "BREAD", "MELON", "EGGS", "MUSHROOM",
    };

    internal static int Kinds { get { return Names.Length; } }

    /// 在 parent 下造一个食物。`at` 是底面中心（世界坐标）。
    internal static GameObject Spawn(Transform parent, Vector3 at, int kind, float s, string name)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = at;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        int k = ((kind % Kinds) + Kinds) % Kinds;
        switch (k)
        {
            case 0: Carrot(root.transform, s);     break;
            case 1: Tomato(root.transform, s);     break;
            case 2: Apple(root.transform, s);      break;
            case 3: Lemon(root.transform, s);      break;
            case 4: Milk(root.transform, s);       break;
            case 5: Lettuce(root.transform, s);    break;
            case 6: Corn(root.transform, s);       break;
            case 7: Berry(root.transform, s);      break;
            case 8: Bread(root.transform, s);      break;
            case 9: Melon(root.transform, s);      break;
            case 10: Eggs(root.transform, s);      break;
            default: Mushroom(root.transform, s);  break;
        }
        return root;
    }

    // ───────────────────────── 调色板 ─────────────────────────
    // 全部比"真实颜色"暗一档：冰箱里只有冷白灯，纯饱和色在这种光下会糊成一片白点。

    static readonly Color Orange = new Color(0.72f, 0.36f, 0.10f);
    static readonly Color Red    = new Color(0.62f, 0.13f, 0.12f);
    static readonly Color Yellow = new Color(0.76f, 0.66f, 0.16f);
    static readonly Color Green  = new Color(0.20f, 0.40f, 0.16f);
    static readonly Color GreenD = new Color(0.12f, 0.28f, 0.11f);
    static readonly Color Cream  = new Color(0.86f, 0.85f, 0.80f);
    static readonly Color Blue   = new Color(0.16f, 0.27f, 0.46f);
    static readonly Color Brown  = new Color(0.36f, 0.22f, 0.11f);
    static readonly Color BrownD = new Color(0.24f, 0.15f, 0.08f);
    static readonly Color White  = new Color(0.90f, 0.89f, 0.85f);
    static readonly Color Grey   = new Color(0.42f, 0.43f, 0.44f);

    // ───────────────────────── 品种 ─────────────────────────

    static void Carrot(Transform t, float s)
    {
        Nb.Cyl(t, "Body", new Vector3(0f, 0.34f * s, 0f), 0.20f * s, 0.68f * s, Orange, false);
        Nb.Cyl(t, "Tip", new Vector3(0f, 0.03f * s, 0f), 0.07f * s, 0.08f * s, Orange, false);
        Nb.Ball(t, "Leaf a", new Vector3(0.03f * s, 0.70f * s, 0f), 0.16f * s, Green, false);
        Nb.Ball(t, "Leaf b", new Vector3(-0.04f * s, 0.75f * s, 0.02f * s), 0.14f * s, GreenD, false);
        Nb.Ball(t, "Leaf c", new Vector3(0f, 0.73f * s, -0.04f * s), 0.13f * s, GreenD, false);
    }

    static void Tomato(Transform t, float s)
    {
        Nb.Ball(t, "Body", new Vector3(0f, 0.26f * s, 0f), 0.52f * s, Red, false);
        Nb.Ball(t, "Cap", new Vector3(0f, 0.50f * s, 0f), 0.16f * s, GreenD, false);
        Nb.Box(t, "Stem", new Vector3(0f, 0.54f * s, 0f), new Vector3(0.04f, 0.10f, 0.04f) * s, Green, false);
    }

    static void Apple(Transform t, float s)
    {
        Nb.Ball(t, "Body", new Vector3(0f, 0.25f * s, 0f), 0.50f * s, Red, false);
        Nb.Box(t, "Stem", new Vector3(0f, 0.53f * s, 0f), new Vector3(0.035f, 0.12f, 0.035f) * s, BrownD, false);
        Nb.Ball(t, "Leaf", new Vector3(0.07f * s, 0.56f * s, 0f), 0.14f * s, Green, false);
    }

    static void Lemon(Transform t, float s)
    {
        Nb.Ball(t, "Body", new Vector3(0f, 0.20f * s, 0f), 0.42f * s, Yellow, false);
        Nb.Ball(t, "Nub a", new Vector3(0.21f * s, 0.20f * s, 0f), 0.09f * s, Yellow, false);
        Nb.Ball(t, "Nub b", new Vector3(-0.21f * s, 0.20f * s, 0f), 0.09f * s, Yellow, false);
    }

    static void Milk(Transform t, float s)
    {
        Nb.Box(t, "Carton", new Vector3(0f, 0.52f * s, 0f),
            new Vector3(0.44f, 1.04f, 0.40f) * s, Cream, false);
        Nb.Box(t, "Label", new Vector3(0f, 0.44f * s, -0.205f * s),
            new Vector3(0.40f, 0.42f, 0.02f) * s, Blue, false);
        // 折角屋顶：两块斜板拼出来，比一个平顶更像纸盒
        Nb.Box(t, "Roof a", new Vector3(0f, 1.08f * s, -0.10f * s),
            new Vector3(0.44f, 0.10f, 0.24f) * s, Cream, false);
        Nb.Box(t, "Roof b", new Vector3(0f, 1.08f * s, 0.10f * s),
            new Vector3(0.44f, 0.10f, 0.24f) * s, Cream, false);
    }

    static void Lettuce(Transform t, float s)
    {
        Nb.Ball(t, "Core", new Vector3(0f, 0.24f * s, 0f), 0.50f * s, Green, false);
        Nb.Ball(t, "Leaf a", new Vector3(0.14f * s, 0.20f * s, 0.06f * s), 0.34f * s, GreenD, false);
        Nb.Ball(t, "Leaf b", new Vector3(-0.12f * s, 0.22f * s, -0.08f * s), 0.32f * s, GreenD, false);
        Nb.Ball(t, "Leaf c", new Vector3(0f, 0.34f * s, 0.10f * s), 0.28f * s, Green, false);
    }

    static void Corn(Transform t, float s)
    {
        Nb.Cyl(t, "Cob", new Vector3(0f, 0.14f * s, 0f), 0.24f * s, 0.62f * s, Yellow, false);
        Nb.Box(t, "Husk a", new Vector3(0.10f * s, 0.12f * s, 0f),
            new Vector3(0.05f, 0.30f, 0.26f) * s, GreenD, false);
        Nb.Box(t, "Husk b", new Vector3(-0.10f * s, 0.12f * s, 0f),
            new Vector3(0.05f, 0.30f, 0.26f) * s, GreenD, false);
        Nb.Cyl(t, "Stem", new Vector3(0f, 0.62f * s, 0f), 0.06f * s, 0.12f * s, Green, false);
    }

    static void Berry(Transform t, float s)
    {
        Nb.Ball(t, "Body", new Vector3(0f, 0.18f * s, 0f), 0.36f * s, Red, false);
        Nb.Ball(t, "Tip", new Vector3(0f, 0.02f * s, 0f), 0.14f * s, Red, false);
        Nb.Ball(t, "Cap", new Vector3(0f, 0.34f * s, 0f), 0.20f * s, GreenD, false);
        Nb.Box(t, "Stem", new Vector3(0f, 0.40f * s, 0f), new Vector3(0.03f, 0.08f, 0.03f) * s, Green, false);
    }

    static void Bread(Transform t, float s)
    {
        Nb.Box(t, "Loaf", new Vector3(0f, 0.24f * s, 0f),
            new Vector3(0.86f, 0.48f, 0.44f) * s, Brown, false);
        Nb.Box(t, "Crust", new Vector3(0f, 0.47f * s, 0f),
            new Vector3(0.88f, 0.06f, 0.46f) * s, BrownD, false);
        Nb.Box(t, "Score a", new Vector3(-0.18f * s, 0.50f * s, 0f),
            new Vector3(0.06f, 0.02f, 0.40f) * s, BrownD, false);
        Nb.Box(t, "Score b", new Vector3(0.10f * s, 0.50f * s, 0f),
            new Vector3(0.06f, 0.02f, 0.40f) * s, BrownD, false);
    }

    static void Melon(Transform t, float s)
    {
        Nb.Ball(t, "Rind", new Vector3(0f, 0.40f * s, 0f), 0.80f * s, GreenD, false);
        Nb.Ball(t, "Stripe a", new Vector3(0f, 0.40f * s, 0f), 0.82f * s, Green, false);
        Nb.Box(t, "Stem", new Vector3(0f, 0.78f * s, 0f), new Vector3(0.05f, 0.10f, 0.05f) * s, BrownD, false);
    }

    static void Eggs(Transform t, float s)
    {
        Nb.Box(t, "Tray", new Vector3(0f, 0.17f * s, 0f),
            new Vector3(0.74f, 0.34f, 0.52f) * s, Grey, false);
        Nb.Box(t, "Lid", new Vector3(0f, 0.35f * s, 0f),
            new Vector3(0.76f, 0.05f, 0.54f) * s, White, false);
        for (int i = 0; i < 3; i++)
            Nb.Ball(t, "Egg " + i, new Vector3((-0.22f + i * 0.22f) * s, 0.30f * s, 0f),
                0.16f * s, White, false);
    }

    static void Mushroom(Transform t, float s)
    {
        Nb.Cyl(t, "Stalk", new Vector3(0f, 0.14f * s, 0f), 0.16f * s, 0.28f * s, White, false);
        Nb.Ball(t, "Cap", new Vector3(0f, 0.30f * s, 0f), 0.42f * s, Brown, false);
        Nb.Box(t, "Gill", new Vector3(0f, 0.26f * s, 0f),
            new Vector3(0.34f, 0.02f, 0.34f) * s, White, false);
    }
}
