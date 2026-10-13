// FirstPersonMove.cs — 给场景里的 FirstPersonController 加第一人称移动（运行时自装配）
//
// 键位：WASD / 方向键走路，Shift 跑 ×2，Space 跳，鼠标转视角，Esc 释放鼠标、点击画面重新锁定。
// 约束：本项目只启用新 Input System（activeInputHandler: 1），全部用 UnityEngine.InputSystem，
//       用旧版 Input.* 会直接抛异常。
// 自装配：Bootstrap 在 SampleScene 加载后按名字找「FirstPersonController」，没挂本组件就补上；
//         缺 CharacterController 也补上；同时移除 FlyCamera 实例（它的 Start 会禁用其它相机，和玩家相机打架）。
// 也可以手动拖到任意物体上用，不依赖 Bootstrap。

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class FirstPersonMove : MonoBehaviour
{
    [Header("移动")]
    public float walkSpeed = 3.5f;
    public float runMultiplier = 2f;

    [Header("跳跃 / 重力")]
    public float jumpVelocity = 5f;
    public float gravity = -18f;

    [Header("视角")]
    public bool enableMouseLook = true;
    public float lookSensitivity = 0.12f; // 度/像素，与 FlyCamera 一致

    CharacterController _cc;
    Transform _head;
    float _pitch;
    float _vy;
    bool _cursorLocked;

    void Start()
    {
        _cc = GetComponent<CharacterController>();
        if (_cc == null)
        {
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.3f;
            _cc.center = new Vector3(0f, 0.9f, 0f);
            _cc.slopeLimit = 45f;
            _cc.stepOffset = 0.35f;
            _cc.skinWidth = 0.03f;
            _cc.minMoveDistance = 0f;
            Debug.Log("[FirstPersonMove] 你没加 CharacterController，已自动补一个（h1.8/r0.3）。");
        }

        // 视角用子级相机（你 Hierarchy 里的 Main Camera）；找不到就造一个
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            _head = cam.transform;
        }
        else
        {
            var headGo = new GameObject("Head (auto)");
            headGo.transform.SetParent(transform, false);
            headGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            headGo.AddComponent<Camera>().nearClipPlane = 0.05f;
            headGo.AddComponent<AudioListener>();
            _head = headGo.transform;
            Debug.LogWarning("[FirstPersonMove] FirstPersonController 下没有相机，已自动造一个 Head (auto)。");
        }
        _pitch = _head.localEulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        LockCursor();
        Debug.Log("[FirstPersonMove] 已接管「" + name + "」：WASD/方向键移动，Shift 跑，Space 跳，鼠标转视角，Esc 释放鼠标。");
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return; // 失焦或没有键盘的帧直接跳过

        // —— 鼠标锁定管理 ——
        if (kb.escapeKey.wasPressedThisFrame) UnlockCursor();
        var mouse = Mouse.current;
        if (!_cursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame) LockCursor();

        // —— 视角 ——
        if (enableMouseLook && _cursorLocked && mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue();
            float yaw = d.x * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - d.y * lookSensitivity, -89f, 89f);
            transform.Rotate(0f, yaw, 0f);              // 偏航转身体（你的胶囊）
            _head.localEulerAngles = new Vector3(_pitch, 0f, 0f); // 俯仰转头
        }

        // —— 平面移动（相对身体朝向）——
        float h = 0f, v = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;

        Vector3 move = (transform.right * h + transform.forward * v);
        if (move.sqrMagnitude > 1f) move.Normalize();
        float speed = walkSpeed * ((kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ? runMultiplier : 1f);

        // —— 跳跃与重力 ——
        if (_cc.isGrounded)
        {
            if (_vy < 0f) _vy = -2f; // 贴地小下压，保证 isGrounded 稳定
            if (_cursorLocked && kb.spaceKey.wasPressedThisFrame) _vy = jumpVelocity;
        }
        _vy += gravity * Time.deltaTime;

        Vector3 motion = move * speed * Time.deltaTime + Vector3.up * (_vy * Time.deltaTime);
        _cc.Move(motion);
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _cursorLocked = true;
    }

    /// 由标题页 / 结算页调用：立刻把鼠标交还玩家（等价于玩家点了一下画面）。
    /// 光标状态与 _cursorLocked 必须一起改 —— 只锁光标、不改 _cursorLocked，会得到
    /// 「光标被锁住、但转鼠标视角没反应」这种很难自己看出来的状态。
    internal void LockCursorNow() => LockCursor();

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _cursorLocked = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ———— 运行时自装配 ————

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => Install(scene);
        Scene active = SceneManager.GetActiveScene();
        if (active.isLoaded) Install(active);
    }

    static void Install(Scene scene)
    {
        if (scene.name != "SampleScene") return;

        GameObject player = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "FirstPersonController") { player = root; break; }
            Transform t = root.transform.Find("FirstPersonController");
            if (t != null) { player = t.gameObject; break; }
        }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (player != null) break;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
                if (t.name == "FirstPersonController") { player = t.gameObject; break; }
        }
        if (player == null)
        {
            Debug.LogWarning("[FirstPersonMove] 场景里没有叫「FirstPersonController」的物体，本次不装配（脚本也可以手动拖上去用）。");
            return;
        }
        if (player.GetComponent<FirstPersonMove>() != null) return;
        player.AddComponent<FirstPersonMove>();

        // FlyCamera 的 Start 会禁用场景里其它相机（包括你的 Main Camera），有它在玩家就瞎了 → 移除
        foreach (FlyCameraController fly in Object.FindObjectsByType<FlyCameraController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Debug.Log("[FirstPersonMove] 已移除 FlyCamera 实例（它的 Start 会禁用其它相机，和玩家相机冲突）。宿主：" + fly.name);
            Object.Destroy(fly.gameObject);
        }
    }
}
