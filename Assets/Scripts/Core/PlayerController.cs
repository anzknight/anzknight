using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("=== 主要参照 ===")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Transform spriteRoot;
    [SerializeField] private VisualManager visualManager;
    [SerializeField] private Transform cameraTransform;

    [Header("=== 物理パラメータ ===")]
    [SerializeField] private float anchorAttractionForce = 14f;
    [SerializeField] private float gasImpulseForce = 8f;
    [SerializeField] private float maxSpeed = 16f;
    [SerializeField] private float airDrag = 0.04f;

    [Header("=== 時間演出 ===")]
    [SerializeField] private float slowTimeScale = 0.3f;
    [SerializeField] private float timeScaleLerpSpeed = 7f;

    [Header("=== アニメーション ===")]
    [SerializeField] private float rotationLerpSpeed = 12f;
    [SerializeField] private float maxLeanAmount = 0.45f;
    [SerializeField] private float scaleLerpSpeed = 8f;

    private Rigidbody2D _rb;
    private bool _isHolding;
    private Vector2 _pendingGasDir;
    private bool _gasBoostPending;
    private float _targetTimeScale;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;
        _rb.freezeRotation = true;
        _targetTimeScale = 1f;
    }

    private void OnEnable()
    {
        inputManager.OnTapBegin += HandleTapBegin;
        inputManager.OnHold += HandleHold;
        inputManager.OnUntap += HandleUntap;
        inputManager.OnSwipe += HandleSwipe;
    }

    private void OnDisable()
    {
        inputManager.OnTapBegin -= HandleTapBegin;
        inputManager.OnHold -= HandleHold;
        inputManager.OnUntap -= HandleUntap;
        inputManager.OnSwipe -= HandleSwipe;
    }

    private void HandleTapBegin()
    {
        _isHolding = true;
        _targetTimeScale = slowTimeScale;
        visualManager?.ActivateWire(transform);
        visualManager?.CaptureSlashStart(transform.position);
    }

    private void HandleHold()
    {
        visualManager?.UpdateWire(transform);
    }

    private void HandleUntap()
    {
        _isHolding = false;
        _targetTimeScale = 1f;
        visualManager?.DeactivateWire();
        visualManager?.TriggerSlashEffect(cameraTransform);
    }

    private void HandleSwipe(Vector2 swipeDirection)
    {
        _pendingGasDir = -swipeDirection;
        _gasBoostPending = true;
        visualManager?.SetGasDirection(_pendingGasDir);
    }

    private void FixedUpdate()
    {
        ApplyAnchorAttraction();
        ApplyGasBoost();
        ApplyAirDrag();
        ClampSpeed();
    }

    private void Update()
    {
        UpdateTimeScale();
        UpdateProceduralAnimation();
        visualManager?.UpdateWire(transform);
    }

    private void ApplyAnchorAttraction()
    {
        Transform anchor = visualManager?.NearestAnchor;
        if (anchor == null) return;

        Vector2 toAnchor = (Vector2)(anchor.position - transform.position);
        float holdFactor = _isHolding ? 1f : 0f;
        float distFactor = Mathf.Clamp01(toAnchor.magnitude / 6f);
        _rb.AddForce(toAnchor.normalized * anchorAttractionForce * holdFactor * distFactor, ForceMode2D.Force);
    }

    private void ApplyGasBoost()
    {
        if (!_gasBoostPending) return;
        _gasBoostPending = false;
        _rb.AddForce(_pendingGasDir * gasImpulseForce, ForceMode2D.Impulse);
    }

    private void ApplyAirDrag()
    {
        _rb.velocity *= 1f - airDrag;
    }

    private void ClampSpeed()
    {
        _rb.velocity = Vector2.ClampMagnitude(_rb.velocity, maxSpeed);
    }

    private void UpdateTimeScale()
    {
        Time.timeScale = Mathf.Lerp(Time.timeScale, _targetTimeScale, timeScaleLerpSpeed * Time.unscaledDeltaTime);
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
    }

    private void UpdateProceduralAnimation()
    {
        if (spriteRoot == null) return;

        Vector2 velocity = _rb.velocity;
        float speed = velocity.magnitude;
        float motionWeight = Mathf.Clamp01(speed / 0.15f);
        float targetAngle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
        float angle = Mathf.LerpAngle(spriteRoot.eulerAngles.z, targetAngle, rotationLerpSpeed * Time.deltaTime * motionWeight);
        spriteRoot.rotation = Quaternion.Euler(0f, 0f, angle);

        float speedRatio = Mathf.Clamp01(speed / maxSpeed);
        float stretchY = 1f + speedRatio * maxLeanAmount;
        float squishX = 1f / stretchY;
        Vector3 targetScale = new Vector3(squishX, stretchY, 1f);
        spriteRoot.localScale = Vector3.Lerp(spriteRoot.localScale, targetScale, scaleLerpSpeed * Time.deltaTime);
    }
}
