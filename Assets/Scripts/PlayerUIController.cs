using Oculus.Interaction.Locomotion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class PlayerUIController : MonoBehaviour
{
    // ----------------------------
    // Game State
    // ----------------------------
    public enum GameState
    {
        Playing,
        GameClear,
        GameOver
    }

    [Header("Game State")]
    [SerializeField] private GameState curGameState = GameState.Playing;

    [Header("Life")]
    [SerializeField] private int life = 3;

    [Header("Hit Detection")]
    [SerializeField] private int enemyAttackLayer = 8;
    [SerializeField] private float invincibleTime = 0.25f;
    private float _invincibleTimer = 0f;

    [Header("Damage Overlay Material (INSPECTOR)")]
    [Tooltip("DamageOverlay에 쓰는 머티리얼을 여기로 드래그하세요. (Unlit/Transparent 권장)")]
    [SerializeField] private Material overlayMat;

    [Tooltip("런타임에 머티리얼을 복제해서(인스턴스) 사용. 프로젝트 원본 머티리얼 오염 방지")]
    [SerializeField] private bool instantiateMaterialAtRuntime = true;

    [Range(0f, 255f)]
    [SerializeField] private float hitMaxAlpha255 = 48f;

    [Tooltip("0->최대 알파까지 올라가는 속도(255 기준)")]
    [SerializeField] private float fadeInSpeed255PerSec = 720f;

    [Tooltip("최대->0으로 내려오는 속도(255 기준)")]
    [SerializeField] private float fadeOutSpeed255PerSec = 480f;

    private float _alpha01 = 0f;
    private bool _flashActive = false;
    private bool _fadeIn = true;

    [Header("Death (Move/Rotate Rig Origin)")]
    [SerializeField] private Transform playerOrigin;
    [SerializeField] private Transform deathTarget;
    [SerializeField] private float deathMoveSpeed = 1.2f;
    [SerializeField] private float deathRotateSpeed = 1.8f;
    [SerializeField] private bool yawOnly = true;

    [Header("Menus")]
    [SerializeField] private GameObject deathMenuUI;
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private bool pauseByTimeScale = true;
    [SerializeField] private bool blockPauseWhenDead = true;

    [Header("OpenXR Menu Toggle (Input System)")]
    [Tooltip("메뉴 토글 버튼 액션(Boolean). 예: XRI RightHand/SecondaryButton(B) 같은 것")]
    [SerializeField] private InputActionReference menuToggleAction;
    [SerializeField] private float menuDebounce = 0.25f;
    private float _menuTimer = 0f;

    private bool _isDead = false;
    private bool _isPaused = false;

    [Header("Clear UI")]
    public GameObject clearUI;

    void Awake()
    {
        if (deathMenuUI) deathMenuUI.SetActive(false);
        if (pauseMenuUI) pauseMenuUI.SetActive(false);
        if (clearUI) clearUI.SetActive(false);

        // 머티리얼 복제(원본 오염 방지)
        if (overlayMat != null && instantiateMaterialAtRuntime)
        {
            overlayMat = Instantiate(overlayMat);
        }

        // 시작 시 투명
        SetOverlayAlpha01(0f);

        // 초기 상태
        curGameState = GameState.Playing;
    }

    void OnEnable()
    {
        if (menuToggleAction != null) menuToggleAction.action.Enable();
    }

    void OnDisable()
    {
        if (menuToggleAction != null) menuToggleAction.action.Disable();
        if (pauseByTimeScale) Time.timeScale = 1f;
    }

    void Update()
    {
        float udt = Time.unscaledDeltaTime;
        float dt = Time.deltaTime;

        // 메뉴 토글(ESC 없음, VR 버튼 액션)
        if (_menuTimer > 0f) _menuTimer = Mathf.Max(0f, _menuTimer - udt);

        if (_menuTimer <= 0f && menuToggleAction != null && menuToggleAction.action.triggered)
        {
            // GameOver에서는 pause 토글 막을지 옵션
            if (!blockPauseWhenDead || curGameState != GameState.GameOver)
            {
                TogglePauseMenu();
                _menuTimer = menuDebounce;
            }
        }

        // 무적 타이머
        if (_invincibleTimer > 0f)
            _invincibleTimer = Mathf.Max(0f, _invincibleTimer - dt);

        // 피격 오버레이 업데이트(시간 멈춰도 보여야 하니 unscaled)
        UpdateDamageOverlay(udt);

        // 사망 시 리그 이동/회전
        if (curGameState == GameState.GameOver)
            UpdateDeathRig(udt);
    }

    // ----------------------------
    // Hit Detection
    // ----------------------------
    private void OnTriggerEnter(Collider other)
    {
        // 게임이 끝난 상태(GameOver/GameClear)면 추가 판정 막기
        if (curGameState != GameState.Playing) return;

        if (_invincibleTimer > 0f) return;

        // 적 공격 판정
        if (other.gameObject.layer == enemyAttackLayer)
        {
            if (other.gameObject.GetComponentInParent<Animator>() != null)
            {
                Animator enemyAnim = other.gameObject.GetComponentInParent<Animator>();
                if (enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack1") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack2") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack1") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack3") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack4") ||
                    enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack 1") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack 2") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack 3") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("Attack 4") ||
                    enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("attack1") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("attack2") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("attack3") || enemyAnim.GetCurrentAnimatorStateInfo(0).IsName("attack4"))
                {
                    Physics.IgnoreLayerCollision(7, 8, true);
                    Invoke(nameof(EnableHit), 0.2f);
                    TakeHit();
                    _invincibleTimer = invincibleTime;
                    SoundManager.Instance.SFXPlay("GotHitSFX");
                }
            }
        }

        // 클리어 트리거(레이어 10)
        if (other.gameObject.layer == 10)
        {
            // ✅ Playing 상태에서만 클리어 처리 (GameOver면 무시)
            TriggerGameClear();
        }
    }


    void EnableHit()
    {
        Physics.IgnoreLayerCollision(7, 8, false);
    }

    void TakeHit()
    {
        life--;

        // 피격 플래시 시작
        StartFlash();

        if (life <= 0)
            Die();
    }

    // ----------------------------
    // Game Clear / Game Over
    // ----------------------------
    void TriggerGameClear()
    {
        if (curGameState != GameState.Playing) return;

        curGameState = GameState.GameClear;

        if (clearUI) clearUI.SetActive(true);

        // ✅ GameOver일 때는 적 제거를 막아야 한다고 했으니,
        // GameClear일 때만 적 제거가 일어나게 한다.
        foreach (EnemyController enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
        {
            Destroy(enemy.gameObject);
        }

        // 클리어 시에도 게임 멈추고 싶으면 (원하면)
        ApplyMenuState(true);
    }

    // ----------------------------
    // Overlay Flash (mat.color.a 갱신)
    // ----------------------------
    void StartFlash()
    {
        _flashActive = true;
        _fadeIn = true;
        _alpha01 = 0f;
        SetOverlayAlpha01(_alpha01);
    }

    void UpdateDamageOverlay(float dt)
    {
        if (!_flashActive) return;
        if (overlayMat == null) return;

        float maxA = hitMaxAlpha255 / 255f;

        float inStep = (fadeInSpeed255PerSec / 255f) * dt;
        float outStep = (fadeOutSpeed255PerSec / 255f) * dt;

        if (_fadeIn)
        {
            _alpha01 += inStep;
            if (_alpha01 >= maxA)
            {
                _alpha01 = maxA;
                _fadeIn = false;
            }
        }
        else
        {
            _alpha01 -= outStep;
            if (_alpha01 <= 0f)
            {
                _alpha01 = 0f;
                _flashActive = false;
            }
        }

        SetOverlayAlpha01(_alpha01);
    }

    void SetOverlayAlpha01(float a01)
    {
        if (overlayMat == null) return;

        Color c = overlayMat.color;
        c.a = Mathf.Clamp01(a01);
        overlayMat.color = c;
    }

    // ----------------------------
    // Death
    // ----------------------------
    void Die()
    {
        if (curGameState == GameState.GameOver) return;

        curGameState = GameState.GameOver;
        _isDead = true;

        if (deathMenuUI) deathMenuUI.SetActive(true);
        if (pauseMenuUI) pauseMenuUI.SetActive(false);
        if (clearUI) clearUI.SetActive(false); // 게임오버면 클리어 UI는 끔(원하면)

        _isPaused = false;

        ApplyMenuState(true);
    }

    void UpdateDeathRig(float dt)
    {
        if (playerOrigin == null || deathTarget == null) return;

        playerOrigin.position = Vector3.Lerp(
            playerOrigin.position,
            deathTarget.position,
            1f - Mathf.Exp(-deathMoveSpeed * dt)
        );

        Quaternion targetRot = deathTarget.rotation;
        if (yawOnly)
        {
            float yaw = targetRot.eulerAngles.y;
            targetRot = Quaternion.Euler(0f, yaw, 0f);
        }

        playerOrigin.rotation = Quaternion.Slerp(
            playerOrigin.rotation,
            targetRot,
            1f - Mathf.Exp(-deathRotateSpeed * dt)
        );
    }

    // ----------------------------
    // Pause Menu
    // ----------------------------
    public void TogglePauseMenu()
    {
        // GameOver면 pause토글 막고 싶으면 여기서도 막을 수 있음
        if (blockPauseWhenDead && curGameState == GameState.GameOver) return;

        _isPaused = !_isPaused;

        if (pauseMenuUI) pauseMenuUI.SetActive(_isPaused);

        if (_isPaused)
        {
            ApplyMenuState(true);
        }
        else
        {
            // 클리어/오버 상태에서는 계속 멈춰두고 싶다면 true 유지
            if (curGameState == GameState.Playing) ApplyMenuState(false);
            else ApplyMenuState(true);
        }
    }

    void ApplyMenuState(bool open)
    {
        bool deathOpen = (deathMenuUI && deathMenuUI.activeSelf);
        bool pauseOpen = (pauseMenuUI && pauseMenuUI.activeSelf);
        bool clearOpen = (clearUI && clearUI.activeSelf);

        bool anyOpen = open || deathOpen || pauseOpen || clearOpen;

        if (pauseByTimeScale)
            Time.timeScale = anyOpen ? 0f : 1f;
    }

    // ----------------------------
    // UI Buttons
    // ----------------------------
    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ----------------------------
    // Helpers
    // ----------------------------
    public int GetLife() => life;
    public GameState GetGameState() => curGameState;
    public void SetGameState(GameState state) => curGameState = state;
}
