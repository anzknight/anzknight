// ============================================================
// [InputManager.cs]
// 異次元立体機動アリーナ - 入力制御・画面サイズ最適化レイヤー
// ============================================================
//
// 【設計メモ：なぜ「正規化ビューポート座標」を使うのか】
//
//   Input.mousePosition は「スクリーンピクセル座標」を返す。
//   例) iPhone 15 Pro (393×852 px) と Galaxy S24 (1080×2340 px) では
//   同じ場所を触っても全く違う数値になってしまう。
//
//   解決策：スクリーン座標 ÷ 画面サイズ = 0.0〜1.0 の正規化座標
//   どの機種・解像度でも「画面左端=0, 右端=1, 下端=0, 上端=1」になる。
//
// 【4つの入力フェーズ】
//   TapBegan  → 指が触れた瞬間（OnTapBegan イベント発火）
//   Holding   → 保持中（IsHolding = true, SwipeDeltaを毎フレーム更新）
//   Swiping   → Holdingかつ指が動いた状態（SwipeDelta ≠ Vector2.zero）
//   TapEnded  → 指が離れた瞬間（OnTapEnded イベント発火）
//
// 【PC ↔ スマホ切り替え】
//   #if UNITY_EDITOR || UNITY_STANDALONE → マウス
//   それ以外（iOS, Android）           → タッチ
//   ビルドターゲットを変えるだけで自動切り替わる。
// ============================================================

using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector 設定値
    // ──────────────────────────────────────────────

    [Header("スワイプ感度")]
    [Tooltip("スワイプのデルタ値に掛ける倍率。大きいほどガスが強く出る")]
    public float swipeSensitivity = 2.0f;

    [Tooltip("この正規化距離未満の指の揺れは「静止」とみなしてデルタを0にする")]
    public float swipeDeadZone = 0.004f;

    // ──────────────────────────────────────────────
    //  他コンポーネントへ公開するプロパティ（読み取り専用）
    // ──────────────────────────────────────────────

    /// <summary>現在タップ・ホールド中かどうか</summary>
    public bool IsHolding { get; private set; }

    /// <summary>タップを開始してからの経過時間（秒）。unscaledTime基準でスロー中でも正確</summary>
    public float HoldDuration { get; private set; }

    /// <summary>
    /// 今フレームの指の移動量（正規化座標の差分 × swipeSensitivity）。
    /// ガス推進ベクトルの計算に使う。デッドゾーン以下ならVector2.zero。
    /// </summary>
    public Vector2 SwipeDelta { get; private set; }

    /// <summary>タップ開始時点の正規化座標（0〜1）。斬撃の始点の記録に使う</summary>
    public Vector2 TouchStartNormalized { get; private set; }

    /// <summary>現在フレームの指の正規化座標。ワイヤー描画の終点などに使う</summary>
    public Vector2 TouchCurrentNormalized { get; private set; }

    // ──────────────────────────────────────────────
    //  イベント（他コンポーネントが購読して状態変化に反応する）
    // ──────────────────────────────────────────────

    /// <summary>タップした瞬間（スロー開始・ワイヤー表示などに使う）</summary>
    public event Action OnTapBegan;

    /// <summary>指を離した瞬間（ダッシュ・斬撃エフェクト開始などに使う）</summary>
    public event Action OnTapEnded;

    // ──────────────────────────────────────────────
    //  内部状態変数
    // ──────────────────────────────────────────────

    // 前フレームの正規化座標（SwipeDelta = 今 - 前 で差分を取る）
    private Vector2 previousNormalized;

    // ──────────────────────────────────────────────
    //  メインループ
    // ──────────────────────────────────────────────

    private void Update()
    {
        SampleInput();
    }

    // ──────────────────────────────────────────────
    //  入力サンプリング（PC/スマホ統合）
    // ──────────────────────────────────────────────

    /// <summary>
    /// PCマウス / スマホタッチを統一的にサンプリングし、
    /// 正規化座標に変換してプロパティ・イベントを更新する。
    /// </summary>
    private void SampleInput()
    {
        // ── プラットフォーム別の生入力取得 ──────────────────────────
        bool tapBegan  = false;
        bool tapHeld   = false;
        bool tapEnded  = false;
        Vector2 rawScreenPos = Vector2.zero;

#if UNITY_EDITOR || UNITY_STANDALONE
        // PC開発用：マウスボタン0を使用
        tapBegan    = Input.GetMouseButtonDown(0);
        tapHeld     = Input.GetMouseButton(0);
        tapEnded    = Input.GetMouseButtonUp(0);
        rawScreenPos = Input.mousePosition;
#else
        // スマホ用：最初の指（index 0）のみ追跡
        if (Input.touchCount > 0)
        {
            Touch touch  = Input.GetTouch(0);
            tapBegan     = touch.phase == TouchPhase.Began;
            tapHeld      = touch.phase == TouchPhase.Stationary
                        || touch.phase == TouchPhase.Moved;
            tapEnded     = touch.phase == TouchPhase.Ended
                        || touch.phase == TouchPhase.Canceled;
            rawScreenPos = touch.position;
        }
#endif

        // ── スクリーン座標 → 正規化ビューポート座標（0〜1）に変換 ──
        // Screen.width/height で割ることでどの解像度でも同じ値になる
        Vector2 normalizedPos = new Vector2(
            rawScreenPos.x / Screen.width,
            rawScreenPos.y / Screen.height
        );

        // ── 各フェーズの処理 ─────────────────────────────────────────

        if (tapBegan)
        {
            IsHolding              = true;
            HoldDuration           = 0f;
            TouchStartNormalized   = normalizedPos;
            TouchCurrentNormalized = normalizedPos;
            previousNormalized     = normalizedPos;
            SwipeDelta             = Vector2.zero;
            OnTapBegan?.Invoke();   // 購読者（PlayerController等）へ通知
        }
        else if (tapHeld && IsHolding)
        {
            // unscaledDeltaTime：スローモーション中でもリアルタイムで計測
            HoldDuration           += Time.unscaledDeltaTime;
            TouchCurrentNormalized =  normalizedPos;

            // 今フレームと前フレームの正規化差分 × 感度 = SwipeDelta
            Vector2 rawDelta = normalizedPos - previousNormalized;

            // デッドゾーン：指の微細な揺れをゼロとして扱い、静止判定を安定させる
            SwipeDelta = (rawDelta.magnitude > swipeDeadZone)
                ? rawDelta * swipeSensitivity
                : Vector2.zero;

            previousNormalized = normalizedPos;
        }
        else if (tapEnded && IsHolding)
        {
            IsHolding  = false;
            SwipeDelta = Vector2.zero;
            OnTapEnded?.Invoke();   // 購読者（PlayerController等）へ通知
        }
        else if (!tapHeld)
        {
            // タッチなし：すべてリセット
            IsHolding  = false;
            SwipeDelta = Vector2.zero;
        }
    }
}
