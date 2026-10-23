// @role Re-lights the scene at night: a single moonlight key, faint fill, fog, and a night-sky clear colour.
// @calls NbScene NbFridge
// @tune KeyPower:float:0-1.5:moon key light intensity
// @tune FillPower:float:0-0.5:fill light intensity

// NeighbourhoodLighting.cs — 把场景那套"室内三点布光"收成居民区夜里的一盏月光
//
// 为什么要有这个文件（2026-10-17 18:03 排查记录，别再拆掉）：
//   现象：进游戏后 3D 区域一片纯白，HUD / 小地图正常。
//   日志：相机 (70.55, 0.30, 9.50)、朝向 -z、清屏 Skybox、掩码全开、40 m 内 233 个渲染器 —— 场景是好的。
//   截图：把用户那张 PNG 解码后按 4 像素采样 15950 个点，**只有一个颜色 (255,255,255)**。
//   原因：场景里三盏平行光，强度 2.0 + 1.0 + 1.0，方向全部朝下（NdotL 分别 0.77 / 0.89 / 0.70），
//        地面总辐照 ≈ 3.12。而 12 栋楼是 FBX 换过来的、**没有贴图的白材质**（albedo 1.0），
//        1.0 × 3.12 ⇒ 三个通道全部 >1 ⇒ 被裁到 255。地砖 0.66×3.12、草地 0.48×3.12 同样爆掉。
//        于是画面里没有任何边缘、阴影、纹理，全白。
//        这也顺带解释了"无法移动"：走起来画面上没有任何变化，而小地图只有 3.46 px/m，走 1 m 才挪 3 px。
//
// 做法：不改场景文件（本工程的老规矩：运行时自装配），在进关卡时把灯光重排一遍 ——
//   最亮那盏平行光留作主光（月光，冷色，0.62），其余降成一点点冷补光（0.07），
//   环境光改成均匀的夜色，加一层深蓝雾做纵深，相机清屏换成纯色夜空。
//   KeyPower 是这一关的总亮度旋钮：觉得太暗往上调，太白往下调。

using UnityEngine;
using UnityEngine.Rendering;

// public：Assets/Editor 下的烘焙脚本在另一个程序集里，要调 Apply() 把夜色写进场景。
public static class NbLighting
{
    // ───────── 调色板 ─────────
    // 现在是「入夜后的街区」（标题页那句 a residential block, after dark 就是这个调子）。
    // 想改成白天：KeyPower 0.95、KeyColor 纯白 (1,1,1)、AmbCol (0.22,0.24,0.28)、
    //             FogCol (0.62,0.68,0.78) + fogDensity 0.006、SkyColor (0.42,0.60,0.85)。
    // 任何时候只要觉得"太白看不出结构"，先把 KeyPower 往下调 —— 判据是 KeyPower × albedo ≲ 1。
    internal const float KeyPower = 0.62f;      // 主光（月光）
    internal const float FillPower = 0.07f;     // 其余平行光：只留一点点，别让墙角死黑

    static readonly Color KeyColor = new Color(0.78f, 0.85f, 1.00f);   // 冷白月光
    static readonly Color FillColor = new Color(0.52f, 0.60f, 0.86f);  // 天光反射的蓝
    static readonly Color SkyColor = new Color(0.026f, 0.036f, 0.062f); // 夜空（相机清屏）
    static readonly Color FogCol = new Color(0.055f, 0.070f, 0.110f);
    static readonly Color AmbCol = new Color(0.085f, 0.100f, 0.150f);

    public static void Apply(Camera cam)
    {
        Apply(cam, NbSceneKind.Street);
    }

    /// 按场景换一套调色板。
    ///   Street  0.62 —— 街区要能看清"这条巷子里有没有猫"，暗但可读；
    ///   Yard    0.34 —— 后院故意压到几乎只剩轮廓，因为**唯一的亮源应该是保安的手电**；
    ///   Kitchen 0.22 —— 室内没有月光，天花板的灯管才是光，抬到 0.6 会把它冲掉。
    /// 这三个数别单独调：它们和各自场景里手电/灯管的强度是一套。
    internal static void Apply(Camera cam, NbSceneKind kind)
    {
        float keyPower = KeyPower;
        float fillPower = FillPower;
        float fogDensity = 0.014f;
        float sunPitch = 50f;

        Color keyColor = KeyColor;
        Color fillColor = FillColor;
        Color skyColor = SkyColor;
        Color fogCol = FogCol;
        Color ambCol = AmbCol;

        switch (kind)
        {
            case NbSceneKind.Yard:
                keyPower = 0.34f;
                fogDensity = 0.020f;
                sunPitch = 42f;
                keyColor = new Color(0.66f, 0.76f, 1.00f);
                skyColor = new Color(0.014f, 0.020f, 0.038f);
                fogCol = new Color(0.035f, 0.048f, 0.075f);
                ambCol = new Color(0.050f, 0.060f, 0.095f);
                break;

            case NbSceneKind.Kitchen:
                keyPower = 0.22f;
                fillPower = 0.04f;
                fogDensity = 0.010f;
                sunPitch = 72f;
                keyColor = new Color(0.72f, 0.76f, 0.82f);
                fillColor = new Color(0.55f, 0.58f, 0.64f);
                skyColor = new Color(0.010f, 0.010f, 0.012f);
                fogCol = new Color(0.045f, 0.047f, 0.052f);
                ambCol = new Color(0.060f, 0.062f, 0.072f);
                break;

            case NbSceneKind.Fridge:
                // 冰箱关门时只有冷藏灯：冷白、几乎全直下（sunPitch 74°），
                // 雾压得很浓 —— 那层雾就是"冷气"，它同时干掉了"一眼看到 30 m 外"这件事，
                // 六层板才会显出层层退进去的深度。
                // 环境光和 NbFridge.SetDoor 里的 AmbClosed **必须一样**：
                // 两处都写这个数，是因为 SetDoor(0) 在 LoadLevel 里会把 Apply 的结果覆盖掉，
                // 改一处不改另一处，会看到"进关闪一下才变暗"。
                keyPower = 0.28f;
                fillPower = 0.05f;
                fogDensity = 0.032f;
                sunPitch = 74f;
                keyColor = new Color(0.80f, 0.88f, 1.00f);
                fillColor = new Color(0.52f, 0.58f, 0.68f);
                skyColor = new Color(0.008f, 0.010f, 0.014f);
                fogCol = new Color(0.058f, 0.068f, 0.085f);
                ambCol = new Color(0.052f, 0.058f, 0.072f);
                break;
        }

        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 平行光按当前强度排序：最亮那盏当主光（保留它原来的颜色倾向之外的一切设置）
        int key = -1;
        float best = -1f;
        int dirCount = 0;
        float sumBefore = 0f;
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null || l.type != LightType.Directional) continue;
            dirCount++;
            sumBefore += l.intensity;
            if (l.intensity > best) { best = l.intensity; key = i; }
        }

        float sumAfter = 0f;
        int fillUsed = 0;
        int killed = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null || l.type != LightType.Directional) continue;
            if (i == key)
            {
                l.intensity = keyPower;
                l.color = keyColor;
                // 阴影不打开：楼体是薄壳模型 + 地面是多层薄板，开阴影容易出现自阴影条纹。
                // 明暗已经靠"不同朝向的面 NdotL 不同"分开了，够读。
                l.shadows = LightShadows.None;

                // 方向也得给。这顶光原来几乎是**平着**照的（俯角很小）：竖着的墙面被照亮，
                // 水平的路面拿不到光 ⇒ 站街上低头是一块死黑的砖、抬头是一面灰墙，
                // 整个画面没有可读的明暗，走起来跟"停在原地"一模一样。
                l.transform.rotation = Quaternion.Euler(sunPitch, 35f, 0f);
            }
            else if (fillUsed < 1)
            {
                // 只留一盏补光。以前是"每盏都留 0.07"，可运行时一数居然是 16 盏平行光，
                // 15 × 0.07 = 1.05，光补光就把安全线 ~1.0 顶穿了（自检会一直报警）。
                l.intensity = fillPower;
                l.color = fillColor;
                l.shadows = LightShadows.None;
                fillUsed++;
            }
            else
            {
                // 其余整盏关掉：叠在一起既没人看得见，又让"总辐照"这个判据失效。
                l.intensity = 0f;
                l.enabled = false;
                killed++;
            }
            sumAfter += l.intensity;
        }

        // 环境光：天空盒模式会跟着天空一起变亮，这里改成均匀夜色，亮度可控
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambCol;

        // 雾：夜里过道尽头该化掉。指数平方，40 m 约 34%，80 m 约 81%
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogCol;
        RenderSettings.fogDensity = fogDensity;

        // 夜空：不要默认那个偏白的天光，纯色深蓝
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
        }

        Debug.LogFormat("[Nb] 灯光收暗（{0}）：平行光 {1} 盏、强度合计 {2:F2} → 主光 {3:F2} + 补光 {4:F2}，其余 {5} 盏关掉；" +
                        "环境光改夜色，雾开（{6:F3}）。合计超过 ~1.0 就会把白墙推到纯白。",
            kind, dirCount, sumBefore, keyPower, fillPower, killed, fogDensity);
        Debug.LogFormat("[Nb] 灯光·校对 平行光强度合计现在是 {0:F2}（安全线 ≈ 1.0 以下）", sumAfter);
    }
}
