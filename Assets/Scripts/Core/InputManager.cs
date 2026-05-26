using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 画面タッチ / マウス入力を抽象化して、
/// タップ・ホールド・スワイプ・アンタップを通知する。
/// New Input System に対応し、旧 Input クラスは一切使用しない。
/// </summary>
public class InputManager : MonoBehaviour
{
    [Header("入力検出パラメータ")]
    [SerializeField] private float swipeThreshold = 40f;
    [SerializeField] private float holdThreshold = 0.12f;

    public event Action OnTapBegin;
    public event Action OnHold;
    public event Action OnUntap;
    public event Action<Vector2> OnSwipe;

    public Vector2 CurrentScreenPosition { get; private set; }
    public Vector2 DeltaScreenPosition { get; private set; }
    public bool IsHolding { get; private set; }

    private Vector2 _touchStartPos;
    private Vector2 _prevPos;
    private float _holdTimer;
    private bool _isPressed;
    private bool _swipeFired;

    private void Update()
    {
        Vector2 position = ReadPointerPosition();
        bool pressedNow = ReadPressed();
        bool started = ReadPressedStarted();
        bool ended = ReadPressedEnded();

        DeltaScreenPosition = _isPressed ? position - _prevPos : Vector2.zero;
        CurrentScreenPosition = position;
        _prevPos = position;

        if (started) StartTouch(position);
        if (_isPressed && pressedNow) UpdateHold(position);
        if (ended && _isPressed) EndTouch();
    }

    private void StartTouch(Vector2 position)
    {
        _isPressed = true;
        _swipeFired = false;
        _holdTimer = 0f;
        IsHolding = false;
        _touchStartPos = position;
        _prevPos = position;
        OnTapBegin?.Invoke();
    }

    private void UpdateHold(Vector2 position)
    {
        _holdTimer += Time.unscaledDeltaTime;
        bool shouldHold = !IsHolding && _holdTimer >= holdThreshold;
        IsHolding |= _holdTimer >= holdThreshold;
        if (shouldHold) OnHold?.Invoke();

        Vector2 totalDelta = position - _touchStartPos;
        bool swipeReady = !_swipeFired && totalDelta.sqrMagnitude >= swipeThreshold * swipeThreshold;
        if (swipeReady)
        {
            _swipeFired = true;
            OnSwipe?.Invoke(totalDelta.normalized);
        }
    }

    private void EndTouch()
    {
        _isPressed = false;
        IsHolding = false;
        OnUntap?.Invoke();
    }

    private bool ReadPressed()
    {
        return Pointer.current?.press?.isPressed == true;
    }

    private bool ReadPressedStarted()
    {
        return Pointer.current?.press?.wasPressedThisFrame == true;
    }

    private bool ReadPressedEnded()
    {
        return Pointer.current?.press?.wasReleasedThisFrame == true;
    }

    private Vector2 ReadPointerPosition()
    {
        var pointer = Pointer.current;
        return pointer != null ? pointer.position.ReadValue() : CurrentScreenPosition;
    }
}
