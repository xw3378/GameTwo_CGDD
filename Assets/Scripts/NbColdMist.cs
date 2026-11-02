// @role Fridge cold-vapor particles: shelf-edge spilling mist, floor pooling, door-seam plume. Runtime only.
// @calls NbScene NbFridge

// NbColdMist.cs — 冰箱的"冷气"
//
// 为什么需要：
//   冰箱场景的几何/灯带/实光都齐了，但"冷"这件事只能靠玩家脑补。
//   真实冰箱开门那一刻最先被读到的是**白色冷气往下淌、贴地铺开** ——
//   冷空气比热空气重，所以它一定是往下沉、往外淌的，而不是往上飘。
//   没有这层雾，六层搁板再亮也只是一个"白色书架"。
//
// 做法：三组粒子，全部运行时生成，贴图也是运行时画的径向渐变软圆（不落盘）：
//   1. 每层搁板前沿一组 —— 雾漫过板沿，缓慢下沉（velocityOverLifetime 的 −y）
//      并朝门口方向淌（+z）。这是"冷"的主体。
//   2. 地面一组大颗粒 —— 沉底后铺开的那层薄雾，几乎不动。
//   3. 门缝一组 —— 平时只渗一丝（rate 1.5），门一开发射率拉到 26，
//      开门那股"白气涌出来"就靠它。发射率每帧由 NeighbourhoodGame 写进来（SetDoor），
//      和门板的视觉开度 _doorVisual 用同一个数，所以雾的强弱和门缝一致。
//
// 注意：
//   · 粒子全部 simulationSpace = World，玩家穿过雾时雾不跟人走。
//   · 粒子没有碰撞体、不参与射线（ParticleSystem 默认两者皆无），不用另设层。
//   · 不加噪声模块：本项目是低帧率网页向的轻场景，velocityOverLifetime 的
//     双常量随机已经够"活"，噪声模块的每粒子每帧开销不值。

using UnityEngine;

internal static class NbColdMist
{
    static ParticleSystem _door;      // 门缝那一股；其它组恒速，不用存
    static Texture2D _puff;
    static Material _mat;

    /// 在冰箱 root 下建冷气。非冰箱场景直接 return（_door 也保持 null，
    /// 这样 SetDoor 在其它场景被每帧调到时是无害的 no-op）。
    internal static void Build(Transform root, NbSceneKind kind)
    {
        _door = null;
        if (kind != NbSceneKind.Fridge || root == null) return;

        Transform holder = new GameObject("Cold Mist (auto)").transform;
        holder.SetParent(root, false);

        int n = 0;

        // 1) 每层搁板前沿。y 抬 0.06 是让粒子出生在板面**以上**一点 ——
        //    出生时就和搁板重叠的粒子，在板沿看过去会有一条明显的"被切断"的线。
        //    alpha 给 0.20：软圆贴图的平均 alpha 只有峰值的 ~1/3，而冰箱内壁本身是亮白的，
        //    0.10 这一档在静帧里几乎读不出来（实测 2026-11-02 s_mist4）。
        for (int layer = 1; layer <= NbFridge.Layers; layer++)
        {
            float y = NbFridge.Top(layer) + 0.06f;
            float z = NbFridge.FrontZ(layer);
            Puff(holder, "Cold Mist L" + layer,
                new Vector3(NbFridge.CX, y, z),
                new Vector3(NbFridge.InnerW * 0.46f, 0.04f, 0.30f),
                8.0f, 4.0f, 0.90f, 0.30f,
                new Vector3(0f, -0.050f, 0.10f));   // 下沉 + 朝门口淌
            n++;
        }

        // 2) 地面薄雾：一颗大、淡、慢的粒子铺满整个内腔。
        Puff(holder, "Cold Mist Floor",
            new Vector3(NbFridge.CX, 0.10f, NbFridge.CZ),
            new Vector3(NbFridge.InnerW * 0.48f, 0.05f, 8.5f),
            6.0f, 7.0f, 1.60f, 0.15f,
            new Vector3(0f, 0.004f, 0.03f));
        n++;

        // 3) 门缝。位置在门内侧根部（z = ZMax − 0.25），漂移方向 +z 朝外。
        //    rate 先给 1.5（关着门渗一丝），开门后由 SetDoor 每帧改写。
        _door = Puff(holder, "Cold Mist Door",
            new Vector3(NbFridge.CX, 0.12f, NbFridge.ZMax - 0.25f),
            new Vector3(NbFridge.InnerW * 0.40f, 0.06f, 0.22f),
            1.5f, 4.5f, 1.10f, 0.38f,
            new Vector3(0f, -0.030f, 0.80f));       // 涌出门口，贴着地往下沉
        n++;

        Debug.LogFormat("[Nb] 冰箱冷气已就位（{0} 组粒子）。", n);
    }

    /// 门的视觉开度 0..1（NeighbourhoodGame._doorVisual，每帧调用）。
    /// 关门 = 门缝渗一丝；全开 = 整股涌出。用 Lerp 而不是开关量，
    /// 因为 _doorVisual 本身就是 MoveTowards 平滑过渡的，雾跟着它走才不跳变。
    internal static void SetDoor(float f)
    {
        if (_door == null) return;
        var em = _door.emission;
        em.rateOverTime = Mathf.Lerp(1.5f, 34f, f);
    }

    // ---------------------------------------------------------------

    static ParticleSystem Puff(Transform parent, string name, Vector3 at, Vector3 box,
        float rate, float life, float size, float alpha, Vector3 drift)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;                    // 进场景第一帧雾就已经在，不是"从零开始喷"
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
        main.startSpeed = 0f;                   // 运动全部交给 velocityOverLifetime
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        // 颜色压到比内壁更"冷灰"一点：纯白雾叠在亮白内壁上没有任何对比度（静帧里读不出），
        // 冷灰调叠在白上是"薄霜感"，叠在深色商品上是"提亮的哈气"，两边都读得出来。
        main.startColor = new Color(0.78f, 0.85f, 0.96f, 1f);   // alpha 由 COL 管
        main.gravityModifier = 0f;
        main.maxParticles = 200;

        var em = ps.emission;
        em.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;                      // Box 的 scale 是全长，不是半径

        // 漂移 + 轻微扰动：双常量随机，每颗粒子各走各的，雾才不"列队"
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(drift.x - 0.020f, drift.x + 0.020f);
        vel.y = new ParticleSystem.MinMaxCurve(drift.y - 0.015f, drift.y + 0.015f);
        vel.z = new ParticleSystem.MinMaxCurve(drift.z - 0.030f, drift.z + 0.030f);

        // 淡入淡出：没有淡入的话，粒子是"凭空出现"的一个圆，一眼就假
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.30f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        // 雾气一边漂一边散开（水汽扩散），所以尺寸随寿命长大
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.65f)));

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);   // 慢转，破掉"全是同一个圆"

        var rd = go.GetComponent<ParticleSystemRenderer>();
        rd.sharedMaterial = Mat();
        rd.renderMode = ParticleSystemRenderMode.Billboard;
        rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rd.receiveShadows = false;

        // 直接快进到稳态。为什么不能只靠 main.prewarm：本工程标题页期间 Time.timeScale=0
        // （CatCheeseTitle），prewarm 的预模拟跟着时间一起被冻住 —— 实测玩家点开始那一刻
        // 8 组粒子全是 0 颗，雾要再等好几秒才慢慢长出来，"打开冰箱一股白气"的第一印象就没了。
        // Simulate 是手动推进、不看 timeScale，结束后粒子留在场上，Play 接着正常发射。
        ps.Simulate(life * 1.5f, true, true);
        ps.Play();

        return ps;
    }

    static Material Mat()
    {
        if (_mat != null) return _mat;
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _mat = new Material(sh);
        _mat.name = "Nb ColdMist";
        Texture2D t = PuffTex();
        if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", t);
        if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", t);       // 兜底着色器用

        // 运行时建 URP 透明材质不能只设 _Surface/_Blend：
        // _SrcBlend/_DstBlend/_ZWrite 是隐藏属性，默认值是 One/Zero/ZWrite On（不透明直写），
        // 它们平时由材质编辑器在"选 Transparent"时顺手改掉，代码里建材质必须自己改，
        // 否则贴图 alpha 完全不参与混合，粒子渲成硬边不透明块（实测，见开发日志 2026-11-02）。
        if (_mat.HasProperty("_Surface")) { _mat.SetFloat("_Surface", 1f); _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
        if (_mat.HasProperty("_Blend")) _mat.SetFloat("_Blend", 0f);            // 0 = Alpha，不要 Additive（会过曝成光斑）
        if (_mat.HasProperty("_SrcBlend")) _mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (_mat.HasProperty("_DstBlend")) _mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_mat.HasProperty("_SrcBlendAlpha")) _mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (_mat.HasProperty("_DstBlendAlpha")) _mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_mat.HasProperty("_ZWrite")) _mat.SetFloat("_ZWrite", 0f);
        _mat.SetOverrideTag("RenderType", "Transparent");
        _mat.renderQueue = 3000;
        return _mat;
    }

    /// 软圆贴图：径向 smoothstep 衰减。不用外部素材，128px 足够 —— 粒子本来就半透明。
    static Texture2D PuffTex()
    {
        if (_puff != null) return _puff;
        const int N = 128;
        _puff = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _puff.wrapMode = TextureWrapMode.Clamp;
        _puff.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[N * N];
        float half = N * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - half + 0.5f) * (x - half + 0.5f) + (y - half + 0.5f) * (y - half + 0.5f)) / half;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);        // smoothstep：边缘收得干净，中心不结硬芯
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        _puff.SetPixels32(px);
        _puff.Apply(false, false);
        return _puff;
    }
}
