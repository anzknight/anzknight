// ============================================================
// [PlayerController.cs]
// 異次元立体機動アリーナ - 物理旋回・加速移動・プロシージャルアニメーション
// ============================================================
//
// 【設計メモ①：立体機動の物理の仕組み（if文なし）】
//
//   立体機動装置の動きは「慣性 + 引力 + 推進力」の3つのベクトル合成で表現する。
//
//   currentVelocity（慣性ベクトル）
//     ↑ += attractionVector（アンカーへの引力）→ 弧を描く軌道になる
//     ↑ += thrustVector（ガスの反作用）         → スワイプ逆方向への加速
//
//   これを毎フレーム accumulate するだけで、
//   if文なしに「旋回しながら加速する立体機動」が自動で生まれる。
//
// 【設計メモ②：プロシージャルアニメーションの仕組み】
//
//   外部アニメーション素材なし、コードだけで「生きた動き」を再現する：
//
//   回転：Atan2(vy, vx) で速度方向の角度を求め、スプライトの Z 回転に適用
//         → 進行方向に常に「頭」が向く（360度全方向対応）
//
//   前傾：速度の大きさを 0〜1 に正規化し、Y スケールを縮め X スケールを伸ばす
//         → 高速時に進行方向へ引き伸ばされる「スピード感の視覚化」
//         → スケール変化で面積を保存するため不自然な肥大化が起きない
//
// 【設計メモ③：ガスリソース管理の Lerp 設計】
//
//   gasAmount は 0.0〜1.0 の正規化値で管理。
//   スワイプ中は毎フレーム gasAmount -= 消費速度 * deltaTime で減少。
//   放置 or アンカー到達で gasAmount += 回復速度 * deltaTime で回復。
//   UI ゲージは gasAmount をそのまま表示すれば OK（再計算不要）。
//
// ============================================================

using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector 設定値
    // ──────────────────────────────────────────────

    [Header("移動物理")]
    [Tooltip("通常時の最大速度（units/秒）")]
    public float maxSpeed = 10f;

    [Tooltip("アンタップ後のダッシュ速度倍率（maxSpeed に掛ける）")]
    public float dashSpeedMultiplier = 2.8f;

    [Tooltip("アンカーへの引力の強さ。大きいほど小さい円軌道を描く")]
    public float attractionStrength = 16f;

    [Tooltip("スワイプ 1単位あたりの推進力。swipeDelta.magnitude と掛け合わせる")]
    public float thrustPowerPerUnit = 10f;

    [Header("タイムスケール")]
    [Tooltip("ホールド中のスローモーション倍率（1.0が通常速度）")]
    public float slowTimeScale = 0.3f;

    [Tooltip("TimeScale が目標値へ向かう Lerp の速度（大きいほど素早く切り替わる）")]
    public float timeScaleLerpSpeed = 7f;

    [Header("ガスリソース")]
    [Tooltip("ガスの最大量（UI 表示は gasAmount/maxGas で割合を出す）")]
    public float maxGas = 1.0f;

    [Tooltip("スワイプ推進 1回あたりのガス消費量")]
    public float gasConsumptionPerThrust = 0.08f;

    [Tooltip("放置時のガス自動回復速度（/秒）")]
    public float gasRecoveryRate = 0.15f;

    [Header("プロシージャルアニメーション")]
    [Tooltip("スプライト回転が速度方向へ追従する Lerp 速度")]
    public float rotationLerpSpeed = 12f;

    [Tooltip("最高速時の前傾スケール比（1.0 = なし、0.75 = 25%圧縮）")]
    [Range(0.5f, 1.0f)]
    public float maxLeanScaleY = 0.78f;

    [Header("参照")]
    [Tooltip("入力を受け取る InputManager")]
    public InputManager inputManager;

    [Tooltip("回転・前傾アニメーションを適用するスプライトの Transform（プレイヤー本体から分離）")]
    public Transform spriteTransform;

    // ──────────────────────────────────────────────
    //  他コンポーネントへ公開するプロパティ
    // ──────────────────────────────────────────────

    /// <summary>現在の速度ベクトル（VisualManager がガス方向に使う）</summary>
    public Vector2 CurrentVelocity => currentVelocity;

    /// <summary>現在のガス残量（0.0〜1.0）。SaveManager や UI ゲージが参照する</summary>
    public float GasAmount { get; private set; }

    /// <summary>所持コイン数（SaveManager が参照する）</summary>
    public int Coins { get; private set; }

    /// <summary>現在ワイヤーが繋がっているアンカー（VisualManager がワイヤー描画に使う）</summary>
    public Transform NearestAnchor { get; private set; }

    /// <summary>ダッシュの開始地点（斬撃エフェクトの始点として VisualManager に渡す）</summary>
    public Vector2 DashStartPosition { get; private set; }

    // ──────────────────────────────────────────────
    //  イベント
    // ──────────────────────────────────────────────

    /// <summary>
    /// アンタップ後ダッシュが始まった瞬間に発火。
    /// 引数：(始点, 終点予定位置) → VisualManager が0.2秒後の斬撃エフェクトに使う。
    /// </summary>
    public event Action<Vector2, Vector2> OnDashStarted;

    // ──────────────────────────────────────────────
    //  内部状態変数
    // ──────────────────────────────────────────────

    private Rigidbody2D rb;

    // 現在の速度ベクトル（慣性。毎フレーム引力・推進力が加算される）
    private Vector2 currentVelocity;

    // TimeScale の目標値（Lerp の終点。ホールド中=slowTimeScale、通常=1.0f）
    private float targetTimeScale = 1f;

    // ダッシュ中フラグ（速度が maxSpeed×倍率 を下回ったら解除）
    private bool isDashing;

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 2Dトップダウン：重力なし・物理的な回転もなし（スプライトはコードで回す）
        rb.gravityScale   = 0f;
        rb.freezeRotation = true;

        // ガスをフル充填から開始
        GasAmount = maxGas;
    }

    private void Start()
    {
        // 続きからプレイ：SaveManager にデータがあれば座標を復元
        if (SaveManager.Instance != null && SaveManager.Instance.HasSaveData)
        {
            SaveData data = SaveManager.Instance.CurrentData;
            transform.position = data.Position;
            GasAmount          = data.gasAmount * maxGas; // 正規化値 → 実数値に変換
            Coins              = data.coins;
        }
    }

    private void OnEnable()
    {
        // InputManager のイベントを購読（コンポーネントが有効な間のみ）
        if (inputManager == null) return;
        inputManager.OnTapBegan += HandleTapBegan;
        inputManager.OnTapEnded += HandleTapEnded;
    }

    private void OnDisable()
    {
        // 購読解除（メモリリーク防止）
        if (inputManager == null) return;
        inputManager.OnTapBegan -= HandleTapBegan;
        inputManager.OnTapEnded -= HandleTapEnded;
    }

    // ──────────────────────────────────────────────
    //  メインループ
    // ──────────────────────────────────────────────

    private void Update()
    {
        // ホールド中はスワイプデルタをガス推進ベクトルとして速度に加算
        if (inputManager != null && inputManager.IsHolding)
            ApplyGasThrust();

        // ガスの自動回復（ホールドしていない時間に回復）
        RecoverGas();

        // TimeScale を目標値へ Lerp で滑らかに変化させる
        UpdateTimeScale();

        // スプライトを速度方向に回転・前傾させる（プロシージャルアニメーション）
        UpdateProceduralAnimation();
    }

    private void FixedUpdate()
    {
        // 引力の適用と Rigidbody2D への速度反映は FixedUpdate（物理タイミング）で行う
        ApplyAttractionAndMove();
    }

    // ──────────────────────────────────────────────
    //  イベントハンドラー（タップ開始 / 終了）
    // ──────────────────────────────────────────────

    /// <summary>タップ開始：スローモーション開始、最寄りアンカーを探す</summary>
    private void HandleTapBegan()
    {
        targetTimeScale  = slowTimeScale;
        DashStartPosition = rb.position;
        NearestAnchor    = FindNearestAnchor();
    }

    /// <summary>アンタップ：TimeScale 復帰、ダッシュ突進を開始する</summary>
    private void HandleTapEnded()
    {
        targetTimeScale = 1f;

        // 速度がほぼゼロの場合は進行方向がないため、デフォルト方向（右）を仮設定
        if (currentVelocity.magnitude < 0.1f)
            currentVelocity = Vector2.right * 0.1f;

        // 速度の方向を保ちつつ maxSpeed × 倍率 まで一気に引き上げ
        currentVelocity = currentVelocity.normalized * maxSpeed * dashSpeedMultiplier;
        isDashing = true;

        // VisualManager へ「ダッシュ開始 + 現在の終点（暫定）」を通知
        // VisualManager 側で 0.2 秒後に実際の到達位置を終点として閃光を描く
        OnDashStarted?.Invoke(DashStartPosition, rb.position);

        // 斬撃ダメージ判定を遅延コルーチンで実行
        // （ダッシュが移動を完了するまでの 0.2 秒後に判定する）
        StartCoroutine(DetectSlashDamage());
    }

    // ──────────────────────────────────────────────
    //  斬撃ダメージ判定（コルーチン）
    // ──────────────────────────────────────────────

    /// <summary>
    /// ダッシュ開始位置から到達位置まで LinecastAll を行い、
    /// 通過した EnemyController にダメージを与える。
    /// WaitForSecondsRealtime でタイムスケールの影響を受けずに遅延する。
    /// </summary>
    private IEnumerator DetectSlashDamage()
    {
        // ダッシュ移動が収まるまで待つ（タイムスケール非依存）
        yield return new WaitForSecondsRealtime(0.2f);

        Vector2 slashEnd = rb.position;

        // ダッシュ開始点 → 到達点の線分上にいる全コライダーを取得
        RaycastHit2D[] hits = Physics2D.LinecastAll(DashStartPosition, slashEnd);

        foreach (RaycastHit2D hit in hits)
        {
            EnemyController enemy = hit.collider?.GetComponent<EnemyController>();
            if (enemy != null)
                enemy.TakeDamage(1);
        }
    }

    // ──────────────────────────────────────────────
    //  ガス推進（スワイプの逆方向へ加速）
    // ──────────────────────────────────────────────

    /// <summary>
    /// スワイプデルタの「逆方向」を推進方向として速度に加算する。
    /// ガスが残っていない場合は推進しない。
    /// </summary>
    private void ApplyGasThrust()
    {
        Vector2 swipeDelta = inputManager.SwipeDelta;

        // スワイプなし or ガス切れは何もしない
        if (swipeDelta.magnitude < 0.001f || GasAmount <= 0f) return;

        // スワイプした方向の「逆向き」が推進方向（作用・反作用の法則）
        Vector2 thrustDir = -swipeDelta.normalized;

        // スワイプの速さに比例した推進力を速度ベクトルに加算（if文なし）
        currentVelocity += thrustDir * thrustPowerPerUnit * swipeDelta.magnitude;

        // ガスを消費（Clamp で 0.0 未満にならないようにする）
        GasAmount = Mathf.Clamp(GasAmount - gasConsumptionPerThrust, 0f, maxGas);
    }

    // ──────────────────────────────────────────────
    //  ガス自動回復
    // ──────────────────────────────────────────────

    /// <summary>
    /// ホールドしていない（IsHolding = false）間は毎フレーム少しずつガスを回復する。
    /// Mathf.Min で maxGas を超えないようにクランプ。
    /// </summary>
    private void RecoverGas()
    {
        if (inputManager != null && inputManager.IsHolding) return;
        GasAmount = Mathf.Min(GasAmount + gasRecoveryRate * Time.unscaledDeltaTime, maxGas);
    }

    // ──────────────────────────────────────────────
    //  引力の適用と Rigidbody2D への反映（FixedUpdate）
    // ──────────────────────────────────────────────

    /// <summary>
    /// ホールド中のみアンカーへの引力を速度に加算し、
    /// 速度に上限クランプをかけてから Rigidbody2D に反映する。
    /// </summary>
    private void ApplyAttractionAndMove()
    {
        if (inputManager != null && inputManager.IsHolding && NearestAnchor != null)
        {
            // アンカーへ向かうベクトル（大きさ = 距離）
            Vector2 toAnchor = (Vector2)(NearestAnchor.position - transform.position);

            // 距離係数：近すぎると引力が弱まり自然な円軌道が安定する
            // （距離 0〜6 を 0〜1 に正規化。6 以上は引力フル）
            float distanceFactor = Mathf.Clamp01(toAnchor.magnitude / 6f);

            // 引力ベクトル = 方向 × 強さ × 距離係数
            Vector2 attraction = toAnchor.normalized * attractionStrength * distanceFactor;

            // 速度に加算（1行で円運動が完成する核心）
            currentVelocity += attraction * Time.fixedDeltaTime;
        }

        // ダッシュ中かどうかで速度上限を切り替え
        float speedCap = isDashing ? maxSpeed * dashSpeedMultiplier : maxSpeed;
        currentVelocity = Vector2.ClampMagnitude(currentVelocity, speedCap);

        // Rigidbody2D に反映（Unity 6 API: linearVelocity）
        rb.linearVelocity = currentVelocity;

        // ダッシュ解除判定：速度が 通常最大速 の 1.1 倍を下回ったら通常モードに戻る
        if (isDashing && currentVelocity.magnitude < maxSpeed * 1.1f)
        {
            isDashing = false;
            currentVelocity *= 0.35f; // 急減速（ブレーキ感）
        }
    }

    // ──────────────────────────────────────────────
    //  TimeScale 制御（Lerp でスムーズに）
    // ──────────────────────────────────────────────

    /// <summary>
    /// Time.timeScale を targetTimeScale へ Lerp で近づける。
    /// fixedDeltaTime も同期して更新しないと物理演算が乖離する。
    /// unscaledDeltaTime を使うことでスロー中でも正確な補間になる。
    /// </summary>
    private void UpdateTimeScale()
    {
        Time.timeScale = Mathf.Lerp(
            Time.timeScale,
            targetTimeScale,
            timeScaleLerpSpeed * Time.unscaledDeltaTime
        );

        // 物理演算ステップ時間を timeScale に同期（これがないと物理がバグる）
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
    }

    // ──────────────────────────────────────────────
    //  プロシージャルアニメーション（外部アセット不要）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 速度ベクトルからスプライトの回転角度と前傾スケールを計算して適用する。
    ///
    /// 【回転の計算】
    ///   Atan2(vy, vx) → -180〜180 度の角度 → -90 補正で「上向きが0度」に統一
    ///   LerpAngle を使うことで 359° → 1° の最短経路回転が保証される
    ///
    /// 【前傾スケールの計算】
    ///   speedRatio = currentSpeed / maxSpeed（0〜1）
    ///   scaleY = Lerp(1.0, maxLeanScaleY, speedRatio) → 後方を圧縮
    ///   scaleX = 1.0 / scaleY                         → 面積を保存（太らない）
    /// </summary>
    private void UpdateProceduralAnimation()
    {
        if (spriteTransform == null) return;
        if (currentVelocity.magnitude < 0.1f) return; // 停止時は姿勢を維持

        // ── 回転：速度方向へスプライトを向ける ──────────────────────
        // Atan2 で速度ベクトルを角度に変換（ラジアン → 度）
        float targetAngle = Mathf.Atan2(currentVelocity.y, currentVelocity.x)
                            * Mathf.Rad2Deg
                            - 90f; // -90°補正：Unity の0°=右向き を 上向きに揃える

        // LerpAngle：360°→0°の折り返しを正しく補間する
        float currentAngle = spriteTransform.eulerAngles.z;
        float smoothAngle  = Mathf.LerpAngle(
            currentAngle,
            targetAngle,
            rotationLerpSpeed * Time.deltaTime
        );
        spriteTransform.rotation = Quaternion.Euler(0f, 0f, smoothAngle);

        // ── 前傾スケール：速度に比例して進行方向へ引き伸ばす ────────
        // speedRatio：現在速度を 0〜1 に正規化（最高速で 1.0）
        float speedRatio = Mathf.Clamp01(currentVelocity.magnitude / maxSpeed);

        // Y（進行方向の奥行）を圧縮 → 前に体重がかかる前傾の視覚表現
        float scaleY = Mathf.Lerp(1f, maxLeanScaleY, speedRatio);

        // X（横幅）は Y の逆数で面積を保存（肥大化・萎縮を防ぐ）
        float scaleX = 1f / scaleY;

        spriteTransform.localScale = new Vector3(scaleX, scaleY, 1f);
    }

    // ──────────────────────────────────────────────
    //  最寄りアンカー検索
    // ──────────────────────────────────────────────

    /// <summary>
    /// シーン内の AnchorPoint をすべて取得し、
    /// このプレイヤーに最も近いものの Transform を返す。
    /// ホールド開始時の1回のみ呼ぶ（毎フレーム呼ぶと重い）。
    /// </summary>
    private Transform FindNearestAnchor()
    {
        AnchorPoint[] anchors = FindObjectsByType<AnchorPoint>(FindObjectsSortMode.None);
        Transform nearest = null;
        float nearestDist = float.MaxValue;

        foreach (AnchorPoint anchor in anchors)
        {
            float dist = Vector2.Distance(transform.position, anchor.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest     = anchor.transform;
            }
        }

        return nearest;
    }

    // ──────────────────────────────────────────────
    //  コイン取得（外部から呼ぶ）
    // ──────────────────────────────────────────────

    /// <summary>コインを加算する（コインオブジェクトの OnTriggerEnter2D から呼ぶ）</summary>
    public void AddCoins(int amount)
    {
        Coins += amount;
    }
}
