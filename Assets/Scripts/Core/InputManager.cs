// ============================================================
// [InputManager.cs]
// 2Dトップダウン型立体機動アクションゲーム - 入力制御マネージャー
// ============================================================
//
// 【役割】
// マウス（エディタ開発時）とタッチ（スマホ実機テスト時）の入力を抽象化し、
// 他スクリプトへ以下の4つの状態をC#イベントで通知する:
//   ① OnTapBegin  ─ 指が触れた瞬間（1フレームのみ）
//   ② OnHold      ─ 押し続けている間（毎フレーム）
//   ③ OnUntap     ─ 指が離れた瞬間（1フレームのみ）
//   ④ OnSwipe     ─ スワイプ閾値を超えた瞬間（1回のみ、正規化方向を送出）
//
// 【設計思想】
// プリプロセッサ命令（#if）でプラットフォームを分岐させ、
// 呼び出し側（PlayerController等）は一切プラットフォームを意識しない。
// 座標はスクリーン空間(ピクセル)のまま扱い、ワールド変換は受け取り側に委ねる
// （単一責任の原則）。
// スマホ縦横問わず、画面上のどこでも同じ感度になるよう「移動量」で判定する。
//
// ============================================================

using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    [Header("スワイプ判定")]
    [Tooltip("スワイプと認識するための最小移動距離（スクリーンピクセル）")]
    [SerializeField] private float swipeThreshold = 40f;

    [Header("ホールド判定")]
    [Tooltip("ホールドと認識するための最小押し続け時間（秒・実時間）\nTime.timeScaleに影響されないよう unscaledDeltaTime で計測する")]
    [SerializeField] private float holdThreshold = 0.12f;

    /// <summary>タップ開始：指が画面に触れた瞬間、1フレームのみ発火する</summary>
    public event Action OnTapBegin;

    /// <summary>ホールド中：holdThreshold を超えた後、毎フレーム発火し続ける</summary>
    public event Action OnHold;

    /// <summary>アンタップ：指が画面から離れた瞬間、1フレームのみ発火する</summary>
    public event Action OnUntap;

    /// <summary>スワイプ確定：総移動量が swipeThreshold を超えた瞬間、1回のみ発火。引数は正規化方向</summary>
    public event Action<Vector2> OnSwipe;

    /// <summary>現在のスクリーン座標（ピクセル）。タッチがない場合は最終位置を維持する</summary>
    public Vector2 CurrentScreenPosition { get; private set; }

    /// <summary>前フレームからのスクリーン座標変化量（デルタ・ピクセル）。押していない間はゼロ</summary>
    public Vector2 DeltaScreenPosition { get; private set; }

    /// <summary>ホールド判定が成立し、かつ今もタッチ中かどうか</summary>
    public bool IsHolding { get; private set; }

    private Vector2 _touchStartPos;  // このタッチセッションの開始スクリーン座標
    private Vector2 _prevPos;        // 前フレームのスクリーン座標（デルタ計算用）
    private float   _holdTimer;      // ホールド経過時間（実時間・秒）
    private bool    _isPressed;      // 現在タッチ中/クリック中かどうか
    private bool    _swipeFired;     // このタッチセッションでスワイプを発火済みかどうか

    private void Update()
    {
        Vector2 pos          = GetCurrentScreenPosition();
        bool    pressedStart = GetPressedThisFrame();
        bool    pressedEnd   = GetReleasedThisFrame();
        bool    pressedNow   = GetIsDown();

        // デルタ計算：押している間だけ有効（離した後はゼロにリセット）
        DeltaScreenPosition   = _isPressed ? pos - _prevPos : Vector2.zero;
        CurrentScreenPosition = pos;

        if (pressedStart)               HandlePressStart(pos);
        if (_isPressed && pressedNow)   HandlePressHold(pos);
        if (pressedEnd && _isPressed)   HandlePressEnd();

        _prevPos = pos;
    }

    /// <summary>フェーズ①：押し始め ─ 状態を初期化してOnTapBeginを発火する</summary>
    private void HandlePressStart(Vector2 pos)
    {
        _isPressed     = true;
        _swipeFired    = false;
        _holdTimer     = 0f;
        IsHolding      = false;
        _touchStartPos = pos;
        _prevPos       = pos;
        OnTapBegin?.Invoke();
    }

    /// <summary>
    /// フェーズ②：押し続け ─ ホールドタイマーを進め、スワイプ判定を行う。
    /// unscaledDeltaTime を使うことで timeScale 0.3f のスロー中も正確に計測できる。
    /// </summary>
    private void HandlePressHold(Vector2 pos)
    {
        _holdTimer += Time.unscaledDeltaTime;

        if (_holdTimer >= holdThreshold)
        {
            IsHolding = true;
            OnHold?.Invoke(); // ホールド判定後は毎フレーム発火し続ける
        }

        // スワイプ判定：このタッチセッションで1回のみ発火
        if (!_swipeFired)
        {
            Vector2 totalDelta = pos - _touchStartPos;
            if (totalDelta.magnitude >= swipeThreshold)
            {
                _swipeFired = true;
                OnSwipe?.Invoke(totalDelta.normalized);
            }
        }
    }

    /// <summary>フェーズ③：離した瞬間 ─ 状態をリセットしてOnUntapを発火する</summary>
    private void HandlePressEnd()
    {
        _isPressed = false;
        IsHolding  = false;
        OnUntap?.Invoke();
    }

    // ── プラットフォーム抽象化レイヤー（#if で完全分岐）──
    // UNITY_EDITOR / UNITY_STANDALONE → マウス入力
    // それ以外（iOS / Android）        → タッチ入力

    private bool GetPressedThisFrame()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButtonDown(0);
#else
        return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
#endif
    }

    private bool GetReleasedThisFrame()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButtonUp(0);
#else
        if (Input.touchCount == 0) return false;
        var phase = Input.GetTouch(0).phase;
        return phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
#endif
    }

    private bool GetIsDown()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.GetMouseButton(0);
#else
        return Input.touchCount > 0;
#endif
    }

    /// <summary>タッチがない場合は前フレーム値を維持し、受け取り側のnullチェックを不要にする</summary>
    private Vector2 GetCurrentScreenPosition()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        return Input.mousePosition;
#else
        if (Input.touchCount > 0)
            return Input.GetTouch(0).position;
        return CurrentScreenPosition;
#endif
    }
}
