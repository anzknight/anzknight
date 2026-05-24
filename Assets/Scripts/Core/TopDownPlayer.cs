// ============================================================
// [TopDownPlayer.cs]
// 2Dトップダウン型アクションゲーム - メインプレイヤーコントローラー
// ============================================================
//
// 【設計メモ①：Vectorの合成による円運動の仕組み】
//
//   物理の「向心力」をコードで模倣する方法：
//
//   ① currentVelocity  ← プレイヤーが今持っている速度ベクトル（慣性）
//                          例）右上に飛んでいれば (0.7, 0.7) * speed
//
//   ② attractionVector ← アンカーへ向かう引力ベクトル
//                          例）アンカーが左にあれば (-1, 0) * strength
//
//   ③ 毎フレーム加算：
//      currentVelocity += attractionVector * deltaTime
//
//   → 速度ベクトルが「少しずつアンカー方向に曲げられ続ける」
//   → if文ゼロで、自然な弧を描く円軌道が生まれる
//   → 慣性の強さ(speed)と引力の強さ(attractionStrength)のバランスで
//      軌道の半径が決まる（引力が強いほど小さい軌道）
//
//
// 【設計メモ②：Lerpによる数値の滑らかな変化（ゲージの引き算）】
//
//   Mathf.Lerp(現在値, 目標値, 変化速度 * deltaTime)
//
//   これを毎フレーム呼び続けると：
//   ・最初は大きく変化する（目標との差が大きいため）
//   ・目標値に近づくにつれ変化量が指数的に小さくなる
//   ・数学的には「指数減衰」と同じ挙動
//
//   例）Time.timeScale = Mathf.Lerp(timeScale, 0.3f, 5f * unscaledDt)
//   → ホールドを始めた直後は素早くスローモーションになり、
//     その後はなめらかにスローが維持される「引き算的な補間」が実現できる
//
//
// 【設計メモ③：Particleの動的生成の連動概要】
//
//   GasThrustEffect.cs が以下の手順でParticleSystemを生成・制御する：
//
//   1. Awake() で new GameObject を作り AddComponent<ParticleSystem>()
//   2. MainModule/EmissionModule/ShapeModule をコードから全設定
//   3. ホールド中の毎フレームで SetEmissionActive(bool) を呼ぶ
//   4. 噴射方向は transform.rotation を変えることで制御
//
//   TopDownPlayer → GasThrustEffect の関係は「指揮者 → 演奏者」：
//   TopDownPlayer はスワイプ方向の計算のみ行い、
//   GasThrustEffect はそれを受け取って視覚表現に変換する。
//
// ============================================================

using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownPlayer : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値（Unityエディタから調整可能）
    // ──────────────────────────────────────────────

    [Header("移動設定")]
    [Tooltip("通常時の最高速度（units/秒）")]
    public float maxSpeed = 8f;

    [Tooltip("ダッシュ時の速度倍率（アンタップ後の突進の強さ）")]
    public float dashSpeedMultiplier = 3f;

    [Tooltip("ホールド中の引力の強さ（大きいほど小さい円軌道）")]
    public float attractionStrength = 12f;

    [Tooltip("ガス噴射1回あたりの推進力")]
    public float thrustPowerPerPixel = 0.03f;

    [Header("タイムスケール設定")]
    [Tooltip("ホールド中のスローモーション倍率（0.3f = 3分の1速）")]
    public float slowTimeScale = 0.3f;

    [Tooltip("時間スケールが目標値に戻る補間速度（大きいほど素早く戻る）")]
    public float timeScaleRecoverSpeed = 5f;

    [Header("エフェクト参照（エディタでアタッチしてください）")]
    [Tooltip("魂のワイヤーエフェクト（SoulWireEffect）")]
    public SoulWireEffect soulWireEffect;

    [Tooltip("ガス噴射エフェクト（GasThrustEffect）")]
    public GasThrustEffect gasThrustEffect;

    [Tooltip("時間差斬撃エフェクト（SlashTrailEffect）")]
    public SlashTrailEffect slashTrailEffect;

    // ──────────────────────────────────────────────
    //  内部状態変数（privateで隠蔽）
    // ──────────────────────────────────────────────

    private Rigidbody2D rb;                   // 物理コンポーネントへの参照
    private Vector2 currentVelocity;          // 現在の速度ベクトル（慣性を保持）

    private bool isHolding;                   // ホールド中かどうか
    private bool isDashing;                   // ダッシュ突進中かどうか

    private Vector2 touchPreviousPosition;    // 1フレーム前のタッチ座標（スワイプ差分計算用）
    private Vector2 dashStartPosition;        // ダッシュ開始地点（軌跡記録用）

    private float targetTimeScale = 1f;       // 目標タイムスケール（Lerpの終点）

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 2Dトップダウンなので重力は使わない
        rb.gravityScale = 0f;

        // 回転もロック（プレイヤーが物理的に回転しないように）
        rb.freezeRotation = true;
    }

    // ──────────────────────────────────────────────
    //  メインループ（クリーンに保つ：処理はすべてメソッドに委譲）
    // ──────────────────────────────────────────────

    private void Update()
    {
        HandleInput();       // 入力状態の判定と各フェーズの呼び出し
        UpdateTimeScale();   // タイムスケールのLerp補間
    }

    private void FixedUpdate()
    {
        ApplyMovement();     // 速度ベクトルをRigidbody2Dに反映
    }

    // ──────────────────────────────────────────────
    //  入力処理：タップ・ホールド・スワイプ・アンタップの振り分け
    // ──────────────────────────────────────────────

    /// <summary>
    /// マウス/タッチの状態を毎フレーム判定し、各フェーズのメソッドへ振り分ける。
    /// GetMouseButtonDown/Up はモバイルのタッチにも対応する（Unity仕様）。
    /// </summary>
    private void HandleInput()
    {
        Vector2 inputPosition = Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
        {
            OnTouchBegan(inputPosition);
        }

        if (Input.GetMouseButton(0) && isHolding)
        {
            OnTouchHeld(inputPosition);
        }

        if (Input.GetMouseButtonUp(0) && isHolding)
        {
            OnTouchEnded(inputPosition);
        }
    }

    // ──────────────────────────────────────────────
    //  フェーズ①：タップ開始
    // ──────────────────────────────────────────────

    /// <summary>
    /// 指を置いた瞬間の処理。
    /// スローモーションを開始し、魂のワイヤーを表示する。
    /// </summary>
    private void OnTouchBegan(Vector2 screenPos)
    {
        isHolding = true;
        touchPreviousPosition = screenPos;

        // タイムスケール目標値をスローモーションに切り替え
        // （実際の変化はUpdateTimeScale()がLerpで行う）
        targetTimeScale = slowTimeScale;

        // 魂のワイヤーをプレイヤーからアンカーへ向けて表示
        if (soulWireEffect != null)
            soulWireEffect.ActivateWire(transform);
    }

    // ──────────────────────────────────────────────
    //  フェーズ②：ホールド中（スワイプ→ガス噴射）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 指を押しながらドラッグしている間の処理。
    /// スワイプ方向の「逆向き」にガスを噴射し、慣性ベクトルに推進力を加える。
    /// </summary>
    private void OnTouchHeld(Vector2 screenPos)
    {
        // 今フレームと前フレームの差分 = スワイプのデルタ（移動量と方向）
        Vector2 dragDelta = screenPos - touchPreviousPosition;
        touchPreviousPosition = screenPos;

        // スワイプの逆方向 = ガス噴射方向（ロケットの反動原理）
        Vector2 thrustDirection = CalculateThrustDirection(dragDelta);

        // 指が実際に動いているかどうかを判定（微細な揺れを無視）
        bool isActuallySwiping = dragDelta.magnitude > 0.5f;

        if (gasThrustEffect != null)
        {
            gasThrustEffect.SetThrustDirection(thrustDirection);
            gasThrustEffect.SetEmissionActive(isActuallySwiping);
        }

        // スワイプ量に応じた推進力を速度ベクトルに加算
        if (isActuallySwiping)
        {
            AddThrustForce(thrustDirection, dragDelta.magnitude);
        }
    }

    // ──────────────────────────────────────────────
    //  フェーズ③：アンタップ（指を離す）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 指を離した瞬間の処理。
    /// 時間を通常速度に戻し、最高速ダッシュを開始する。
    /// </summary>
    private void OnTouchEnded(Vector2 screenPos)
    {
        isHolding = false;

        // タイムスケールを通常速度（1.0）に戻す目標をセット
        targetTimeScale = 1f;

        // ガスエフェクトを停止
        if (gasThrustEffect != null)
            gasThrustEffect.SetEmissionActive(false);

        // 魂のワイヤーを非表示にする
        if (soulWireEffect != null)
            soulWireEffect.DeactivateWire();

        // ダッシュ開始地点を記録してから突進
        dashStartPosition = rb.position;
        StartDashSequence();
    }

    // ──────────────────────────────────────────────
    //  ダッシュ突進処理
    // ──────────────────────────────────────────────

    /// <summary>
    /// 現在の速度ベクトルを最大倍率まで引き上げてダッシュを開始する。
    /// 移動方向は変えず、スピードだけを増幅させる。
    /// </summary>
    private void StartDashSequence()
    {
        isDashing = true;

        // 速度がほぼゼロの場合は最後の移動方向を維持できないため、
        // 代わりに現在の向き（transform.right）をデフォルト方向とする
        if (currentVelocity.magnitude < 0.1f)
        {
            currentVelocity = (Vector2)transform.right * 0.1f;
        }

        // 方向を保ちながら速度を一気に最大値×倍率まで引き上げる
        currentVelocity = currentVelocity.normalized * maxSpeed * dashSpeedMultiplier;

        // 少し移動した後に斬撃エフェクトを発火するコルーチン
        StartCoroutine(TriggerSlashAfterDash());
    }

    /// <summary>
    /// ダッシュ中に軌跡の終点を記録し、0.1秒後に斬撃エフェクトを発火するコルーチン。
    /// WaitForSecondsRealtime を使うことでスローモーション中でも正確なタイミングになる。
    /// </summary>
    private IEnumerator TriggerSlashAfterDash()
    {
        // リアルタイムで0.1秒待機（timeScaleの影響を受けない）
        yield return new WaitForSecondsRealtime(0.1f);

        Vector2 dashEndPosition = rb.position;

        // 斬撃エフェクト：軌跡の始点と終点を渡す（0.2秒後に閃光が走る）
        if (slashTrailEffect != null)
        {
            slashTrailEffect.TriggerSlashEffect(dashStartPosition, dashEndPosition);
        }

        // ダッシュ終了：速度を減衰させて通常状態に戻す
        isDashing = false;
        currentVelocity *= 0.3f;
    }

    // ──────────────────────────────────────────────
    //  移動の適用（FixedUpdate から呼ばれる）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 計算済みの速度ベクトルをRigidbody2Dに適用する。
    /// ホールド中は引力も計算してベクトルを曲げる。
    /// </summary>
    private void ApplyMovement()
    {
        if (isHolding)
        {
            // ホールド中はアンカーへの引力を慣性に加算して円軌道を作る
            ApplyAttractionForce();
        }

        // 速度の上限をClampして暴走を防ぐ
        float currentMaxSpeed = isDashing
            ? maxSpeed * dashSpeedMultiplier
            : maxSpeed;

        currentVelocity = Vector2.ClampMagnitude(currentVelocity, currentMaxSpeed);

        // Rigidbody2Dに速度を設定（物理エンジンが位置を更新する）
        rb.linearVelocity = currentVelocity;
    }

    // ──────────────────────────────────────────────
    //  引力による円運動計算（if文を使わない実装）
    // ──────────────────────────────────────────────

    /// <summary>
    /// アンカーへの引力ベクトルを慣性ベクトルに毎フレーム加算することで、
    /// if文を一切使わずに滑らかな円運動を実現する核心メソッド。
    ///
    /// 【なぜif文なしで円運動になるのか？】
    /// プレイヤーが右に動いていて、アンカーが上にある場合：
    ///   慣性ベクトル = (1, 0) → 右に進む
    ///   引力ベクトル = (0, 1) → 上に引かれる
    ///   合成後      = (1, 1) → 右上に進む（軌道が上に曲がる）
    /// 次フレームは位置が変わるので引力の向きも変わり、
    /// これが毎フレーム繰り返されることで弧（円弧）が描かれる。
    /// </summary>
    private void ApplyAttractionForce()
    {
        if (soulWireEffect == null || soulWireEffect.NearestAnchor == null)
            return;

        // アンカーへ向かうベクトル（方向＋距離）
        Vector2 toAnchor = (Vector2)(soulWireEffect.NearestAnchor.position - transform.position);

        // 距離係数：遠いほど引力が強く、近すぎると引力が弱まり（軌道が安定する）
        // Clamp01で0〜1の間に収める
        float distanceFactor = Mathf.Clamp01(toAnchor.magnitude / 5f);

        // 引力ベクトル = アンカーへの単位ベクトル × 強さ × 距離係数
        Vector2 attractionVector = toAnchor.normalized * attractionStrength * distanceFactor;

        // 慣性ベクトルに引力を加算（この1行が円運動の本質）
        // fixedDeltaTime を掛けることでフレームレートに依存しない動きになる
        currentVelocity += attractionVector * Time.fixedDeltaTime;
    }

    // ──────────────────────────────────────────────
    //  ベクトル計算ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>
    /// スクリーン上のスワイプデルタからガスの噴射方向を計算する。
    /// スワイプした方向の「逆」がガスの出る方向（作用・反作用の法則）。
    /// </summary>
    private Vector2 CalculateThrustDirection(Vector2 dragDelta)
    {
        // スワイプ方向を正規化して逆転させる
        // （ゼロ除算を避けるため、magnitude が小さいときは Vector2.zero を返す）
        if (dragDelta.magnitude < 0.001f)
            return Vector2.zero;

        return -dragDelta.normalized;
    }

    /// <summary>
    /// ガスの推進力を速度ベクトルに加算する。
    /// スワイプが速いほど強い推進力になる。
    /// </summary>
    private void AddThrustForce(Vector2 thrustDirection, float swipeMagnitude)
    {
        // スワイプ量を0〜3の範囲に制限して推進力を計算
        float thrustPower = Mathf.Clamp(swipeMagnitude * thrustPowerPerPixel, 0f, 3f);
        currentVelocity += thrustDirection * thrustPower;
    }

    // ──────────────────────────────────────────────
    //  タイムスケール制御
    // ──────────────────────────────────────────────

    /// <summary>
    /// Time.timeScale をLerpで滑らかに変化させる。
    ///
    /// ホールド開始 → targetTimeScale = 0.3f → Lerpでスムーズにスロー化
    /// アンタップ   → targetTimeScale = 1.0f → Lerpでスムーズに通常速度へ復帰
    ///
    /// Time.fixedDeltaTimeも同時に更新しないと、物理演算が
    /// timeScaleと乖離してしまうため必ず一緒に更新する。
    /// </summary>
    private void UpdateTimeScale()
    {
        // unscaledDeltaTimeを使うことで、スロー中でも正確な補間速度になる
        Time.timeScale = Mathf.Lerp(
            Time.timeScale,
            targetTimeScale,
            timeScaleRecoverSpeed * Time.unscaledDeltaTime
        );

        // 物理演算のステップ時間もtimeScaleに合わせて更新
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
    }
}
