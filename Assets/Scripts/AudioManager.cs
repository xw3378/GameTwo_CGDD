// @role Central audio hub: one-shot SFX, 3D cat/guard footstep loops, and the idle squeak.
// @calls NbCat NbGuard
// @tune HearRange:float:5-60:max audible distance (m) for cat/guard footsteps

using System.Collections.Generic;
using UnityEngine;

public enum SoundType
{
    CAT_Hiss,
    CAT_Meow,
    CAT_walk,
    RAT_squeak,
    RAT_running,
    nom,
    MAN_footsteps,
    MAN_running,
    MAN_yell,
    MAN_talking,
    FRIDGE_open,
    FRIDGE_close,
    hum
}

[RequireComponent(typeof(AudioSource))]

public class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioClip[] audioList;
    private static AudioManager instance;
    private AudioSource audiosource;

    
    private const float HearRange = 20f;    // 多远开始听得到（米）

    private class Step
    {
        public AudioSource source;
        public Vector3 lastPos;
    }

    private readonly Dictionary<Transform, Step> _steps = new Dictionary<Transform, Step>();
    private float _scanT;

    private void Awake()
    {
        instance = this;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audiosource = GetComponent<AudioSource>();
    }

    // Update is  called once per frame
    void Update()
    {
        if (instance != this) return;

        // 换关会把猫和人整批销毁再重建，所以每 0.5 秒重新认一次场上还有谁
        _scanT -= Time.deltaTime;
        if (_scanT <= 0f)
        {
            _scanT = 0.5f;
            Rescan();
        }

        // 每帧只做一件事：这一格挪动了就出声，站着就静音
        foreach (KeyValuePair<Transform, Step> kv in _steps)
        {
            Transform body = kv.Key;
            Step s = kv.Value;
            if (body == null) continue;

            bool moving = (body.position - s.lastPos).sqrMagnitude > 1e-6f;
            s.lastPos = body.position;

            // 距离和方向都交给 3D 衰减，这里只开关
            s.source.volume = moving ? 1f : 0f;
        }
    }

    void Rescan()
    {
        // 回收已经消失的（换关时猫 / 人会被 Destroy）
        List<Transform> dead = null;
        foreach (KeyValuePair<Transform, Step> kv in _steps)
        {
            if (kv.Key != null) continue;
            if (dead == null) dead = new List<Transform>();
            dead.Add(kv.Key);
        }
        if (dead != null)
            for (int i = 0; i < dead.Count; i++) _steps.Remove(dead[i]);

        // 捞出场上每一只猫、每一个保安
        foreach (NbCat cat in FindObjectsByType<NbCat>(FindObjectsSortMode.None))
            Attach(cat.transform, SoundType.CAT_walk);

        foreach (NbGuard man in FindObjectsByType<NbGuard>(FindObjectsSortMode.None))
            Attach(man.transform, SoundType.MAN_footsteps);
    }

    void Attach(Transform body, SoundType sound)
    {
        if (_steps.ContainsKey(body)) return;

        GameObject go = new GameObject("Audio");
        go.transform.SetParent(body, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = audioList[(int)sound];
        src.loop = true;
        src.spatialBlend = 1f;                    // 3D：音量随距离、左右声道随方向
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 1f;
        src.maxDistance = HearRange;              // 20 m 处刚好静音
        src.dopplerLevel = 0f;
        src.volume = 0f;
        src.Play();

        _steps.Add(body, new Step { source = src, lastPos = body.position });
    }

    public static float GetSoundLength(SoundType sound)
    {
        return instance.audioList[(int)sound].length;
    }

    public static void PlaySound(SoundType sound, float volume = 1)
    {
        instance.audiosource.PlayOneShot(instance.audioList[(int)sound], volume);
    }

    public static void StopSound()
    {
        instance.audiosource.Stop();
    }

    /// 玩家闲置时的吱吱叫。走共享声源的**主音轨**（不是 PlayOneShot），
    /// 这样 StopSound() 才能把它立刻截断 —— PlayOneShot 的音是 Stop 停不掉的。
    public static void PlaySqueak()
    {
        instance.audiosource.clip = instance.audioList[(int)SoundType.RAT_squeak];
        instance.audiosource.loop = false;
        instance.audiosource.Play();
    }
}
