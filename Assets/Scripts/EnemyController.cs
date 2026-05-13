using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator anim;

    [Header("Target (Player)")]
    [Tooltip("비워두면 레이어 6 플레이어를 자동 탐색합니다.")]
    [SerializeField] private Transform player;
    [SerializeField] private int playerLayer = 6;

    [Header("Dodge Reaction")]
    [Tooltip("회피 판정 레이어")]
    [SerializeField] private int dodgeLayer = 9;

    [Header("Ranges")]
    [SerializeField] private float detectRange = 10f;
    [SerializeField] private float maintainRange = 5f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float rotateSpeed = 12f;

    [Header("Locomotion / Animation Param")]
    [Tooltip("BlendTree float 파라미터 이름 (-1~1)")]
    [SerializeField] private string locomotionParam = "locomotion";

    [Header("Continuous Locomotion Tuning")]
    [SerializeField] private float responseDistance = 2.0f;
    [SerializeField] private float stopBand = 0.10f;
    [SerializeField] private float locomotionSmoothTime = 0.10f;

    [Header("Attack")]
    [SerializeField] private List<string> attackTriggers = new List<string> { "attack1", "attack2" };
    [SerializeField] private float attackCooldown = 5f;

    [Header("Hit Reaction")]
    [SerializeField] private string gotHitTrigger = "GotHit";
    [SerializeField] private float hitLockTime = 0.6f;

    // ✅ 추가: Line of Sight(시야) 레이캐스트
    [Header("Line of Sight (Raycast)")]
    [Tooltip("적 위치에서 위로 올릴 높이. 요청대로 y offset 2")]
    [SerializeField] private float eyeHeight = 2f;

    [Tooltip("레이캐스트가 막힘을 판정할 레이어 마스크(벽+플레이어 포함). 기본은 Everything")]
    [SerializeField] private LayerMask losMask = ~0;

    [Tooltip("벽 뒤면 공격 금지")]
    [SerializeField] private bool requireLineOfSightToAttack = true;

    [Header("Life / Death")]
    [SerializeField] private int life = 3;
    [SerializeField] private string deathTrigger = "Death";
    [SerializeField] private float deathAnimDuration = 3.0f;
    [SerializeField] private bool freezeOnDeath = true;

    // ---- internal state ----
    float _cooldownTimer;
    float _hitLockTimer;
    int _attackIndex;

    int _playerLayerMask;
    bool _isDead;
    Coroutine _deathRoutine;

    float _locomotionCurrent;
    float _locomotionVel;

    void Reset()
    {
        anim = GetComponentInChildren<Animator>();
    }

    void Awake()
    {
        if (!anim) anim = GetComponentInChildren<Animator>();
        _playerLayerMask = 1 << playerLayer;
    }

    void Update()
    {
        if (_isDead) return;

        float dt = Time.deltaTime;

        if (_cooldownTimer > 0f) _cooldownTimer -= dt;
        if (_hitLockTimer > 0f) _hitLockTimer -= dt;

        if (player == null)
            player = FindPlayerInRange();

        if (player == null)
        {
            SetLocomotion(0f, dt);
            return;
        }

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist > detectRange)
        {
            SetLocomotion(0f, dt);
            return;
        }

        FaceTarget(player.position, dt);

        if (_hitLockTimer > 0f)
        {
            SetLocomotion(0f, dt);
            return;
        }

        // ---- continuous locomotion ----
        float diff = dist - maintainRange;
        float targetLoc;

        if (Mathf.Abs(diff) <= stopBand)
            targetLoc = 0f;
        else
            targetLoc = Mathf.Clamp(diff / Mathf.Max(0.0001f, responseDistance), -1f, 1f);

        SetLocomotion(targetLoc, dt);
        MoveByLocomotion(_locomotionCurrent, dt);

        // ✅ 공격 조건 + 시야 체크(벽 뒤면 공격 금지)
        if (Mathf.Abs(diff) <= stopBand)
        {
            if (!requireLineOfSightToAttack || HasLineOfSightToPlayer())
                TryAttack();
        }
    }

    // ✅ 플레이어가 벽 뒤인지 판정
    bool HasLineOfSightToPlayer()
    {
        if (player == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;

        // 플레이어를 "중심 높이"로 살짝 올려 조준(원하면 값 조절)
        Vector3 target = player.position + Vector3.up * 1.2f;

        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist < 0.001f) return true;

        dir /= dist;

        // 트리거는 무시하고, 처음 맞은 콜라이더가 플레이어면 OK
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, losMask, QueryTriggerInteraction.Ignore))
        {
            // 1) 레이어로 판정 (요청: 레이어 6이 안 맞으면 벽 뒤로 간주)
            if (hit.collider != null && hit.collider.gameObject.layer == playerLayer)
                return true;

            // 2) (옵션) 플레이어가 여러 자식 콜라이더라 레이어가 다를 수 있으면 이걸로 보완
            if (hit.collider != null && (hit.collider.transform == player || hit.collider.transform.IsChildOf(player)))
                return true;

            return false;
        }

        return false;
    }

    // ---------------- Movement ----------------

    void MoveByLocomotion(float loc, float dt)
    {
        if (_isDead) return;
        if (Mathf.Abs(loc) < 0.01f) return;

        transform.position += transform.forward * (loc * moveSpeed * dt);
    }

    void FaceTarget(Vector3 targetPos, float dt)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        float a = 1f - Mathf.Exp(-rotateSpeed * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, a);
    }

    void SetLocomotion(float target, float dt)
    {
        if (!anim) return;

        _locomotionCurrent = Mathf.SmoothDamp(
            _locomotionCurrent,
            target,
            ref _locomotionVel,
            locomotionSmoothTime,
            Mathf.Infinity,
            dt
        );

        anim.SetFloat(locomotionParam, _locomotionCurrent);
    }

    // ---------------- Attack ----------------

    void TryAttack()
    {
        if (_cooldownTimer > 0f) return;
        if (attackTriggers == null || attackTriggers.Count == 0) return;

        string trig = attackTriggers[_attackIndex];
        if (!string.IsNullOrWhiteSpace(trig))
            anim.SetTrigger(trig);

        _attackIndex = (_attackIndex + 1) % attackTriggers.Count;
        _cooldownTimer = attackCooldown;
    }

    // ---------------- Collision ----------------

    private void OnTriggerEnter(Collider collider)
    {
        if (_isDead) return;

        int layer = collider.gameObject.layer;

        // 회피
        if (layer == dodgeLayer && (anim.GetCurrentAnimatorStateInfo(0).IsName("Attack1") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack2") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack1") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack3") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack4") ||
                    anim.GetCurrentAnimatorStateInfo(0).IsName("Attack 1") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack 2") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack 3") || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack 4") ||
                    anim.GetCurrentAnimatorStateInfo(0).IsName("attack1") || anim.GetCurrentAnimatorStateInfo(0).IsName("attack2") || anim.GetCurrentAnimatorStateInfo(0).IsName("attack3") || anim.GetCurrentAnimatorStateInfo(0).IsName("attack4")))
        {
            SoundManager.Instance.SFXPlay("ShieldSFX");
            TriggerDodge();
            return;
        }

        // 피격
        if (layer == playerLayer)
        {
            SoundManager.Instance.SFXPlay("HitSFX");
            Physics.IgnoreLayerCollision(6, 8, true);
            Invoke(nameof(EnableHit), 1f);
            ApplyHit();
        }
    }

    void EnableHit()
    {
        Physics.IgnoreLayerCollision(6, 8, false);
    }

    void TriggerDodge()
    {
        if (_isDead) return;

        if (!string.IsNullOrWhiteSpace(gotHitTrigger))
            anim.SetTrigger(gotHitTrigger);

        _hitLockTimer = Mathf.Max(_hitLockTimer, hitLockTime);
    }

    void ApplyHit()
    {
        if (_isDead) return;

        life--;

        // 사망이면 GotHit 없이 Death만
        if (life <= 0)
        {
            Die();
            return;
        }

        if (!string.IsNullOrWhiteSpace(gotHitTrigger))
            anim.SetTrigger(gotHitTrigger);

        _hitLockTimer = Mathf.Max(_hitLockTimer, hitLockTime);
    }

    // ---------------- Death ----------------

    void Die()
    {
        if (_isDead) return;
        _isDead = true;

        if (freezeOnDeath)
        {
            _locomotionCurrent = 0f;
            _locomotionVel = 0f;
            anim.SetFloat(locomotionParam, 0f);

            _cooldownTimer = 9999f;
            _hitLockTimer = 9999f;
        }

        if (!string.IsNullOrWhiteSpace(deathTrigger))
            anim.SetTrigger(deathTrigger);

        if (_deathRoutine != null)
            StopCoroutine(_deathRoutine);

        _deathRoutine = StartCoroutine(CoDestroyAfterDeath());
    }

    IEnumerator CoDestroyAfterDeath()
    {
        yield return new WaitForSeconds(deathAnimDuration);
        Destroy(gameObject);
    }

    // ---------------- Target Find ----------------

    Transform FindPlayerInRange()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            detectRange,
            _playerLayerMask,
            QueryTriggerInteraction.Ignore
        );

        Transform best = null;
        float bestDist = float.PositiveInfinity;

        foreach (var h in hits)
        {
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = h.transform;
            }
        }
        return best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, maintainRange);

        // 디버그: 시야 레이 표시
        if (player != null)
        {
            Vector3 origin = transform.position + Vector3.up * eyeHeight;
            Vector3 target = player.position + Vector3.up * 1.2f;
            Gizmos.color = HasLineOfSightToPlayer() ? Color.green : Color.gray;
            Gizmos.DrawLine(origin, target);
        }
    }
#endif
}
