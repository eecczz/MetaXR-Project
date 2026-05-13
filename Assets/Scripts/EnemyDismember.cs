using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyDismember : MonoBehaviour
{
    public enum DismemberGroup
    {
        None,
        Head,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
        Torso
    }

    [System.Serializable]
    public class DismemberBone
    {
        public string boneName;
        public DismemberGroup group;
    }

    [System.Serializable]
    public class DismemberHitReaction
    {
        public DismemberGroup group;
        public string hitAnimationState;
    }

    [Header("References")]
    [SerializeField] private Animator anim;

    [Header("Hit Settings")]
    [SerializeField] private int playerLayer = 6;

    [Header("Clone Prefab")]
    [SerializeField] private GameObject characterPrefab;

    [Header("Dismember Setup")]
    [Tooltip("절단 가능한 본들만 등록")]
    [SerializeField] private List<DismemberBone> dismemberBones;

    [Tooltip("절단 부위별 피격 애니메이션")]
    [SerializeField] private List<DismemberHitReaction> hitReactions;

    [Header("Clone Physics")]
    [SerializeField] private float ejectImpulse = 2.5f;
    [SerializeField] private float destroyCloneAfterSeconds = 8f;

    private Dictionary<string, DismemberGroup> _boneGroupMap;
    private Dictionary<DismemberGroup, string> _reactionMap;
    private HashSet<string> _alreadyDismembered = new HashSet<string>();

    private void Awake()
    {
        if (!anim) anim = GetComponentInChildren<Animator>();

        _boneGroupMap = new Dictionary<string, DismemberGroup>();
        foreach (var b in dismemberBones)
        {
            if (!_boneGroupMap.ContainsKey(b.boneName))
                _boneGroupMap.Add(b.boneName, b.group);
        }

        _reactionMap = new Dictionary<DismemberGroup, string>();
        foreach (var r in hitReactions)
        {
            if (!_reactionMap.ContainsKey(r.group))
                _reactionMap.Add(r.group, r.hitAnimationState);
        }

        AttachProxies();
    }

    private void AttachProxies()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            var proxy = col.gameObject.GetComponent<DismemberHitProxy>();
            if (!proxy) proxy = col.gameObject.AddComponent<DismemberHitProxy>();
            proxy.Init(this, col.transform);
        }
    }

    public void TryDismember(Transform hitPart)
    {
        if (!hitPart) return;
        if (!_boneGroupMap.TryGetValue(hitPart.name, out var group)) return;
        if (_alreadyDismembered.Contains(hitPart.name)) return;

        _alreadyDismembered.Add(hitPart.name);

        // 🔹 피격 애니메이션
        if (_reactionMap.TryGetValue(group, out string animState))
        {
            anim.CrossFade(animState, 0.05f, 0);
        }

        // 🔹 클론 생성
        if (!characterPrefab) return;
        GameObject clone = Instantiate(characterPrefab, transform.position, transform.rotation);

        var rb = clone.AddComponent<Rigidbody>();
        rb.AddForce((transform.forward + Vector3.up * 0.4f).normalized * ejectImpulse, ForceMode.Impulse);

        Destroy(clone, destroyCloneAfterSeconds);

        // 🔹 원본 숨김
        hitPart.localScale = Vector3.zero;
        var col = hitPart.GetComponent<Collider>();
        if (col) col.enabled = false;
    }

    // ---------------- Proxy ----------------

    private class DismemberHitProxy : MonoBehaviour
    {
        private EnemyDismember root;
        private Transform part;

        public void Init(EnemyDismember r, Transform p)
        {
            root = r;
            part = p;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.layer != root.playerLayer) return;
            root.TryDismember(part);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer != root.playerLayer) return;
            root.TryDismember(part);
        }
    }
}
