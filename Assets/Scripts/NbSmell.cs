// @role Mouse smell power: on R, a horizontal 3D teardrop of yellow scent spawns near the player and streams toward the cheese.
// @calls NbGame

// NbSmell.cs — 老鼠的"嗅觉"
//
// 按 R 闻一下：一团淡黄色气味粒子在玩家身前生成，形状是一根**横着的泪滴**——
// 胖的那头朝玩家（粒子更密），细的那头指向奶酪（粒子越远越稀）。
// 靠近玩家的那一小撮留在原地悬浮，其余粒子则朝奶酪的方向流过去，像一条气味带。
// 气味粒子不改变任何判定（不捡奶酪、不惊动猫），只是指个方向。
//
// 和 NbColdMist 同一套路：粒子运行时生成、贴图也是运行时画的软圆（不落盘）。
// 差别在于它是**一次性爆发**而且每颗粒子各有各的位置/速度 —— 用一个常开的空粒子系统
// （loop=true、rateOverTime=0），每次 Sniff 用 EmitParams 一颗颗手动喷：
//   · 位置：按泪滴轮廓采样（圆截面，半径随距玩家变远而收窄），密度朝玩家集中；
//   · 速度：越靠玩家越接近 0（原地悬着），越靠尖端越快朝奶酪流。

using UnityEngine;

internal static class NbSmell
{
    const float Speed = 1.2f;       // 朝奶酪流的速率 m/s
    const float Life = 2.6f;        // 粒子寿命 s
    const float Size = 0.20f;       // 单颗粒子直径 m
    const int Burst = 70;           // 每闻一次喷多少颗

    const float Length = 1.6f;      // 泪滴总长（玩家 → 奶酪）
    const float BaseRadius = 0.42f; // 泪滴胖端半径（贴玩家那一头）

    static ParticleSystem _ps;
    static Material _mat;
    static Texture2D _tex;

    /// 在 `spawn`（视线前方）生成一条泪滴状气味带：胖端朝玩家、尖端朝 `dir`。
    internal static void Sniff(Vector3 spawn, Vector3 dir)
    {
        ParticleSystem ps = Ensure();
        if (ps == null) return;

        // 水平轴：从玩家指向奶酪。没奶酪（dir 空）就默认朝前。
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        Vector3 axis = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        Vector3 side = new Vector3(-axis.z, 0f, axis.x);   // 水平面内垂直于轴

        for (int i = 0; i < Burst; i++)
        {
            // t∈[0,1]：0=玩家（胖端），1=奶酪（尖端）。1-sqrt(u) 让密度朝玩家集中。
            float t = 1f - Mathf.Sqrt(Random.value);
            float r = BaseRadius * Mathf.Sqrt(1f - t);      // 泪滴轮廓：圆头、尖尾

            // 截面圆内均匀取一点（sqrt 让面积均匀，不往圆心挤）
            float ang = Random.value * Mathf.PI * 2f;
            float rad = r * Mathf.Sqrt(Random.value);
            Vector3 pos = spawn + axis * (t * Length)
                        + (side * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang)) * rad;

            // 越靠尖端流得越快；最贴玩家的那一小撮基本原地不动。
            float flow = Mathf.SmoothStep(0.12f, 0.50f, t);
            Vector3 vel = axis * (Speed * flow)
                        + Vector3.up * (0.05f + 0.12f * Random.value)        // 轻微上扬，像气味升起
                        + side * (Random.value - 0.5f) * 0.25f
                        + axis * (Random.value - 0.5f) * 0.25f;

            var ep = new ParticleSystem.EmitParams();
            ep.position = pos;
            ep.velocity = vel;
            ps.Emit(ep, 1);
        }
    }

    static ParticleSystem Ensure()
    {
        if (_ps != null) return _ps;

        Transform root = NbGame.I != null ? NbGame.I.Root : null;
        GameObject go = new GameObject("Smell Scent (auto)");
        if (root != null) go.transform.SetParent(root, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;                 // 常开但不自动喷 —— 只靠 Emit() 手动喷
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(Life * 0.85f, Life * 1.15f);
        main.startSpeed = 0f;             // 速度由 EmitParams 逐颗给
        main.startSize = new ParticleSystem.MinMaxCurve(Size * 0.7f, Size * 1.35f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        main.startColor = new Color(1f, 0.93f, 0.62f, 1f);   // 偏浅的淡黄
        main.gravityModifier = 0f;
        main.maxParticles = 1024;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        // 位置逐颗给（EmitParams），不靠 shape 模块。
        var shape = ps.shape;
        shape.enabled = false;

        // 雾状：淡入慢、中段保持、再淡出。峰值 alpha 压低，靠数量叠出"浓雾"感。
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.30f, 0.22f),
                    new GradientAlphaKey(0.22f, 0.80f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        // 出生时较小，慢慢胀大 —— 雾散开的样子
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.30f, 1f), new Keyframe(1f, 1.45f)));

        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        var rd = go.GetComponent<ParticleSystemRenderer>();
        rd.sharedMaterial = Mat();
        rd.renderMode = ParticleSystemRenderMode.Billboard;
        rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rd.receiveShadows = false;

        ps.Play();
        _ps = ps;
        return ps;
    }

    static Material Mat()
    {
        if (_mat != null) return _mat;
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _mat = new Material(sh);
        _mat.name = "Nb Smell";
        Texture2D t = Tex();
        if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", t);
        if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", t);       // 兜底着色器用

        // 透明混合必须手动设（同 NbColdMist）：默认是 One/Zero/ZWrite On 的不透明直写。
        if (_mat.HasProperty("_Surface")) { _mat.SetFloat("_Surface", 1f); _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
        if (_mat.HasProperty("_Blend")) _mat.SetFloat("_Blend", 0f);            // 0 = Alpha，不要 Additive
        if (_mat.HasProperty("_SrcBlend")) _mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (_mat.HasProperty("_DstBlend")) _mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_mat.HasProperty("_SrcBlendAlpha")) _mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (_mat.HasProperty("_DstBlendAlpha")) _mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_mat.HasProperty("_ZWrite")) _mat.SetFloat("_ZWrite", 0f);
        _mat.SetOverrideTag("RenderType", "Transparent");
        _mat.renderQueue = 3000;
        return _mat;
    }

    static Texture2D Tex()
    {
        if (_tex != null) return _tex;
        const int N = 128;
        _tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[N * N];
        float half = N * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - half + 0.5f) * (x - half + 0.5f) + (y - half + 0.5f) * (y - half + 0.5f)) / half;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);        // smoothstep：边缘收干净、中心不结硬芯
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        _tex.SetPixels32(px);
        _tex.Apply(false, false);
        return _tex;
    }
}
