// ============================================================
// [PlayerController.cs]
// 2Dトップダウン型立体機動アクションゲーム - 物理移動・プロシージャルアニメーション
// ============================================================
//
// 【役割】
// InputManager からイベントで入力を受け取り、
// 立体機動の物理演算とスプライトのプロシージャルアニメーションを担当する。
//
// 【立体機動の物理モデル（ベクトル合成）】
//   ① アンカー引力：ホールド中にアンカーへの引力を毎FixedUpdateでAddForce → 弧状旋回
//   ② ガス推進：スワイプ逆方向にImpulse（瞬間加力）→ 速度ベクトルの変化
//   ③ 空気抵抗：速度ベクトルに減衰率を毎フレーム乗算 → 自然な減速
//   3つのベクトル合成のみで if 文最少の軽量な物理が実現できる。
//
// 【プロシージャルアニメーション】
//   - Z軸回転：velocity の方向角へ LerpAngle で追従（360度、最短経路）
//   - スカッシュ&ストレッチ：速度に応じてスプライトを前方向に伸縮
//     → 2Dスプライトでは X 軸回転が視覚的に反映されないため、
//       スケール変形による「擬似前傾き」が最も効果的な手法。
//
// 【依存関係】
//   InputManager    ← OnEnable/OnDisable でイベント購読・解除
//   SoulWireEffect  ← ActivateWire / DeactivateWire / NearestAnchor
//   GasThrustEffect ← SetThrustDirection / SetEmissionActive
//
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("=== 参照 ===")]
    [Tooltip("入力マネージャー（同一シーンの InputManager をアタッチ）")]
    [SerializeField] private InputManager inputManager;

    [Tooltip("スプライト専用の子Transform（回転・スケール変形をここに適用）")]
    [SerializeField] private Transform spriteRoot;

    [Tooltip("アンカーワイヤーエフェクト（NearestAnchor でアンカー座標を取得）")]
    [SerializeField] private SoulWireEffect soulWireEffect;

    [Tooltip("ガス噴射エフェクト（噴射方向と発火/停止を制御）")]
    [SerializeField] private GasThrustEffect gasThrustEffect;

    [Header("=== 立体機動 物理パラメータ ===")]
    [Tooltip("アンカーへの引力の強さ（大きいほど小さい軌道を描く）")]
    [SerializeField] private float anchorAttractionForce = 14f;

    [Tooltip("ガス噴射の瞬間推進力（ForceMode2D.Impulse）")]
    [SerializeField] private float gasImpulseForce = 8f;

    [Tooltip("速度の上限（units/秒）")]
    [SerializeField] private float maxSpeed = 16f;

    [Tooltip("空気抵抗による減衰率（0〜1）。毎FixedUpdateでvelocityに乗算。0.04≒4%減速/フレーム")]
    [SerializeField] private float airDrag = 0.04f;

    [Header("=== タイムスロー ===")]
    [Tooltip("ホールド中のタイムスケール（0.3 ≒ 3分の1速）")]
    [SerializeField] private float slowTimeScale = 0.3f;

    [Tooltip("タイムスケール補間速度（大きいほどスロー切替が素早い）")]
    [SerializeField] private float timeScaleLerpSpeed = 7f;

    [Header("=== プロシージャルアニメーション ===")]
    [Tooltip("進行方向へのスプライト回転（Z軸）の追従速度")]
    [SerializeField] private float rotationLerpSpeed = 12f;

    [Tooltip("最大速度時のスプライト縦伸び率（0.45 → 45%伸びる）")]
    [SerializeField] private float maxStretchAmount = 0.45f;

    [Tooltip("スケール変形の補間速度（小さいほどぬるっと変形する）")]
    [SerializeField] private float scaleLerpSpeed = 8f;

    private Rigidbody2D _rb;
    private bool    _isHolding;         // ホールド中フラグ（引力計算の乗数として使用）
    private Vector2 _pendingGasDir;     // 次のFixedUpdateで消費するガス噴射方向
    private bool    _gasBoostPending;   // ガス噴射要求フラグ（FixedUpdateで1回だけ消費）
    private float   _targetTimeScale;   // タイムスケールのLerp目標値

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale   = 0f;   // 2D見下ろし：重力不要
        _rb.freezeRotation = true; // 物理的な自動回転を抑制（プロシージャルアニメで制御）
        _targetTimeScale   = 1f;
    }

    // ── イベント購読管理（メモリリーク防止のためOnDisableで必ず解除）──

    private void OnEnable()
    {
        inputManager.OnTapBegin += HandleTapBegin;
        inputManager.OnHold     += HandleHold;
        inputManager.OnUntap    += HandleUntap;
        inputManager.OnSwipe    += HandleSwipe;
    }

    private void OnDisable()
    {
        inputManager.OnTapBegin -= HandleTapBegin;
        inputManager.OnHold     -= HandleHold;
        inputManager.OnUntap    -= HandleUntap;
        inputManager.OnSwipe    -= HandleSwipe;
    }

    // ── 入力イベントハンドラ（InputManager から呼ばれる）──

    /// <summary>タップ開始：スローモーション開始・ワイヤー表示</summary>
    private void HandleTapBegin()
    {
        _isHolding       = true;
        _targetTimeScale = slowTimeScale;
        soulWireEffect?.ActivateWire(transform);
    }

    /// <summary>
    /// ホールド中（毎フレーム呼ばれる）：
    /// 引力はFixedUpdateのApplyAnchorAttraction()が_isHoldingフラグを参照して加算するため、
    /// このハンドラでは状態更新不要。将来の拡張用としてメソッドは残す。
    /// </summary>
    private void HandleHold() { }

    /// <summary>アンタップ：通常速度へ復帰・エフェクト停止</summary>
    private void HandleUntap()
    {
        _isHolding       = false;
        _targetTimeScale = 1f;
        gasThrustEffect?.SetEmissionActive(false);
        soulWireEffect?.DeactivateWire();
    }

    /// <summary>
    /// スワイプ確定：ガス噴射方向を記録し、次のFixedUpdateで加力する。
    /// スワイプ方向の「逆」= ガスが出る方向（作用・反作用の法則）。
    /// </summary>
    private void HandleSwipe(Vector2 swipeDirection)
    {
        _pendingGasDir   = -swipeDirection;
        _gasBoostPending = true;
        gasThrustEffect?.SetThrustDirection(_pendingGasDir);
        gasThrustEffect?.SetEmissionActive(true);
    }

    // ── 物理更新（FixedUpdate）──

    private void FixedUpdate()
    {
        ApplyAnchorAttraction(); // ① アンカー引力
        ApplyGasBoost();         // ② ガス推進（1回のみ）
        ApplyAirDrag();          // ③ 空気抵抗
        ClampSpeed();            // ④ 速度上限
    }

    /// <summary>
    /// アンカーへの引力ベクトルを毎FixedUpdateでAddForceし、弧状の旋回を生み出す。
    ///
    /// 【if文なしで円軌道になる仕組み】
    /// 速度ベクトル(慣性)に対し毎フレームアンカー方向へ微量加算し続けると、
    /// ベクトルが徐々に曲げられ弧が描かれる（向心力の模倣）。
    /// _isHolding を float の holdFactor に変換して乗算することで if 文を排除する。
    /// </summary>
    private void ApplyAnchorAttraction()
    {
        if (soulWireEffect == null || soulWireEffect.NearestAnchor == null) return;

        Vector2 toAnchor   = (Vector2)(soulWireEffect.NearestAnchor.position - transform.position);
        float   holdFactor = _isHolding ? 1f : 0f;                    // if文の代替：乗数化
        float   distFactor = Mathf.Clamp01(toAnchor.magnitude / 6f);  // 距離が遠いほど引力強

        _rb.AddForce(toAnchor.normalized * anchorAttractionForce * holdFactor * distFactor,
                     ForceMode2D.Force);
    }

    /// <summary>ガス推進力をImpulse（瞬間加力）で適用。フラグを消費し重複加力を防ぐ</summary>
    private void ApplyGasBoost()
    {
        if (!_gasBoostPending) return;
        _gasBoostPending = false;
        _rb.AddForce(_pendingGasDir * gasImpulseForce, ForceMode2D.Impulse);
    }

    /// <summary>空気抵抗：(1-airDrag)を乗算して指数的な速度低下を実現する</summary>
    private void ApplyAirDrag()
    {
        _rb.linearVelocity *= 1f - airDrag;
        // ※ Unity 2022以前では linearVelocity → velocity に読み替えること
    }

    /// <summary>速度の上限をクランプ（ClampMagnitudeで方向を変えずに大きさだけ制限）</summary>
    private void ClampSpeed()
    {
        _rb.linearVelocity = Vector2.ClampMagnitude(_rb.linearVelocity, maxSpeed);
    }

    // ── タイムスケール制御（Update）──

    private void Update()
    {
        UpdateTimeScale();
        UpdateProceduralAnimation();
    }

    /// <summary>
    /// ホールド中はスロー、離すと通常速度へLerpで滑らかに補間する。
    ///
    /// 【unscaledDeltaTimeを使う理由】
    /// timeScale=0.3fのスロー中はdeltaTimeも0.3倍になりLerpが遅くなるため、
    /// unscaledDeltaTimeを使うことでスロー中も補間速度が変化しない。
    ///
    /// 【fixedDeltaTimeを同期させる理由】
    /// timeScaleを変えるとFixedUpdateの頻度が変わるため、
    /// fixedDeltaTime=0.02f×timeScaleで更新して物理演算の精度を保つ（Unity公式推奨）。
    /// </summary>
    private void UpdateTimeScale()
    {
        Time.timeScale = Mathf.Lerp(
            Time.timeScale,
            _targetTimeScale,
            timeScaleLerpSpeed * Time.unscaledDeltaTime
        );
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
    }

    // ── プロシージャルアニメーション（Update）──

    /// <summary>
    /// velocityの方向と大きさに応じて、spriteRootの回転とスケールを毎フレーム自動更新する。
    ///
    /// 【Z軸回転：進行方向への追従】
    ///   Atan2でvelocityの角度を求め、LerpAngleで現在の角度から最短経路で補間する。
    ///   180°をまたぐ場合もLerpAngleが自動的に最短経路を選ぶ（例：350°→10°は20°変化）。
    ///
    /// 【スカッシュ&ストレッチ：擬似的な前傾き表現】
    ///   2DスプライトではX軸回転が視覚的に反映されないため、スケール変形で前傾きを演出する。
    ///   速度が上がるほどローカルY軸（進行方向）を引き伸ばし、
    ///   面積保存のためX軸はYの逆数でスカッシュする。
    ///   stretchY = 1 + speedRatio × maxStretchAmount（最大1.45倍）
    ///   squishX  = 1 / stretchY（面積保存：Y×X=一定）
    /// </summary>
    private void UpdateProceduralAnimation()
    {
        if (spriteRoot == null) return;

        Vector2 vel   = _rb.linearVelocity;
        float   speed = vel.magnitude;

        // ── Z軸回転：進行方向へスプライトを向ける ──
        if (speed > 0.15f)
        {
            // -90°はSpriteの「上」がUnityワールドの「右」を向いている場合の補正値
            float targetAngle  = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg - 90f;
            float currentAngle = spriteRoot.eulerAngles.z;

            float smoothed = Mathf.LerpAngle(
                currentAngle,
                targetAngle,
                rotationLerpSpeed * Time.deltaTime
            );
            spriteRoot.rotation = Quaternion.Euler(0f, 0f, smoothed);
        }

        // ── スカッシュ&ストレッチ：速度に応じた擬似前傾き ──
        float speedRatio = Mathf.Clamp01(speed / maxSpeed); // 0(停止)→1(最大速度)
        float stretchY   = 1f + speedRatio * maxStretchAmount;
        float squishX    = 1f / stretchY; // 面積保存

        Vector3 current = spriteRoot.localScale;
        spriteRoot.localScale = new Vector3(
            Mathf.Lerp(current.x, squishX,  scaleLerpSpeed * Time.deltaTime),
            Mathf.Lerp(current.y, stretchY, scaleLerpSpeed * Time.deltaTime),
            1f
        );
    }
}
