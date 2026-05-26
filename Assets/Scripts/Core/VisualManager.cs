using System.Collections;
using UnityEngine;

/// <summary>
/// 画面演出を完全コードで実装する。
/// ホールド中のワイヤー、斬撃閃光、火花、カメラ揺れ、ヒットストップを扱う。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class VisualManager : MonoBehaviour
{
    [Header("=== ワイヤー ===")]
    [SerializeField] private float wireWidth = 0.04f;
    [SerializeField] private Gradient wireGradient;

    [Header("=== 斬撃演出 ===")]
    [SerializeField] private float slashFlashDuration = 0.12f;
    [SerializeField] private float slashHoldDelay = 0.2f;
    [SerializeField] private Color slashColor = new Color(0.3f, 0.9f, 1f, 1f);

    [Header("=== カメラ演出 ===")]
    [SerializeField] private float hitStopDuration = 0.06f;
    [SerializeField] private float cameraShakeDuration = 0.12f;
    [SerializeField] private float cameraShakeStrength = 0.16f;

    [Header("=== アンカー ===")]
    [SerializeField] private Transform[] anchorPoints;

    public Transform NearestAnchor { get; private set; }

    private LineRenderer _lineRenderer;
    private ParticleSystem _sparkSystem;
    private bool _wireActive;
    private Transform _playerTransform;
    private Vector3 _slashStart;
    private Vector2 _gasDirection = Vector2.right;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
        CreateSparkSystem();
        _lineRenderer.enabled = false;
    }

    private void ConfigureLineRenderer()
    {
        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.widthMultiplier = wireWidth;
        _lineRenderer.colorGradient = wireGradient != null ? wireGradient : CreateDefaultGradient();
        _lineRenderer.numCapVertices = 8;
    }

    private Gradient CreateDefaultGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.1f, 0.7f, 1f), 0f),
                new GradientColorKey(new Color(0.2f, 1f, 1f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.2f, 0f),
                new GradientAlphaKey(0.8f, 0.5f),
                new GradientAlphaKey(0.2f, 1f)
            }
        );
        return gradient;
    }

    private void CreateSparkSystem()
    {
        GameObject sparkObject = new GameObject("VisualManager_Sparks");
        sparkObject.transform.SetParent(transform, false);
        _sparkSystem = sparkObject.AddComponent<ParticleSystem>();
        var main = _sparkSystem.main;
        main.startColor = slashColor;
        main.startSize = 0.08f;
        main.startSpeed = 2.2f;
        main.duration = 0.15f;
        main.loop = false;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = _sparkSystem.emission;
        emission.rateOverTime = 0f;
        emission.burstCount = 1;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, 18, 22, 1, 0.03f));

        var shape = _sparkSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;

        var colorOverLifetime = _sparkSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(slashColor, 0f),
                new GradientColorKey(new Color(0.1f, 0.8f, 1f, 0.1f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = gradient;
    }

    public void ActivateWire(Transform player)
    {
        _wireActive = true;
        _playerTransform = player;
        _lineRenderer.enabled = false;
    }

    public void DeactivateWire()
    {
        _wireActive = false;
        _lineRenderer.enabled = false;
    }

    public void UpdateWire(Transform player)
    {
        _playerTransform = player;
        NearestAnchor = FindNearestAnchor(player.position);
        if (!_wireActive || NearestAnchor == null)
        {
            _lineRenderer.enabled = false;
            return;
        }

        _lineRenderer.enabled = true;
        Vector3 start = player.position;
        Vector3 end = NearestAnchor.position;
        _lineRenderer.SetPosition(0, start);
        _lineRenderer.SetPosition(1, end);

        float intensity = Mathf.Clamp01(1f - Vector3.Distance(start, end) / 6f);
        _lineRenderer.widthMultiplier = Mathf.Lerp(_lineRenderer.widthMultiplier, wireWidth * (0.75f + intensity), Time.unscaledDeltaTime * 12f);
    }

    public void CaptureSlashStart(Vector3 position)
    {
        _slashStart = position;
    }

    public void SetGasDirection(Vector2 direction)
    {
        _gasDirection = direction.normalized;
    }

    public void TriggerSlashEffect(Transform cameraTransform)
    {
        StartCoroutine(SlashRoutine(cameraTransform));
    }

    private Transform FindNearestAnchor(Vector3 position)
    {
        Transform nearest = null;
        float bestDistance = float.MaxValue;
        foreach (Transform anchor in anchorPoints)
        {
            if (anchor == null) continue;
            float distance = Vector3.SqrMagnitude(anchor.position - position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = anchor;
            }
        }
        return nearest;
    }

    private IEnumerator SlashRoutine(Transform cameraTransform)
    {
        yield return new WaitForSecondsRealtime(slashHoldDelay);
        if (_playerTransform == null) yield break;

        Vector3 slashEnd = _playerTransform.position;
        _lineRenderer.enabled = true;
        _lineRenderer.SetPosition(0, _slashStart);
        _lineRenderer.SetPosition(1, slashEnd);
        _lineRenderer.widthMultiplier = wireWidth * 5f;
        _lineRenderer.colorGradient = CreateSlashGradient();

        _sparkSystem.transform.position = (_slashStart + slashEnd) * 0.5f;
        _sparkSystem.Play();

        StartCoroutine(HitStopAndShake(cameraTransform));
        yield return new WaitForSecondsRealtime(slashFlashDuration);

        _lineRenderer.colorGradient = wireGradient != null ? wireGradient : CreateDefaultGradient();
        _lineRenderer.widthMultiplier = wireWidth;
        _lineRenderer.enabled = _wireActive;
    }

    private Gradient CreateSlashGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(slashColor, 0.5f),
                new GradientColorKey(Color.clear, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.5f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        return gradient;
    }

    private IEnumerator HitStopAndShake(Transform cameraTransform)
    {
        float originalTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Vector3 originalCameraPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;

        yield return new WaitForSecondsRealtime(hitStopDuration);

        float elapsed = 0f;
        while (elapsed < cameraShakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float damp = 1f - (elapsed / cameraShakeDuration);
            float noiseX = (Mathf.Sin(elapsed * 40f) + Mathf.Cos(elapsed * 26f)) * cameraShakeStrength * damp;
            float noiseY = (Mathf.Cos(elapsed * 37f) + Mathf.Sin(elapsed * 29f)) * cameraShakeStrength * damp;
            if (cameraTransform != null)
            {
                cameraTransform.position = originalCameraPosition + new Vector3(noiseX, noiseY, 0f);
            }
            yield return null;
        }

        if (cameraTransform != null)
        {
            cameraTransform.position = originalCameraPosition;
        }

        Time.timeScale = originalTimeScale;
    }
}
