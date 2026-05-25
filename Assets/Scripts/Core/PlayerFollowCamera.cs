// ============================================================
// [PlayerFollowCamera.cs]
// 2Dトップダウン型アクションゲーム - プレイヤー追従カメラ
// ============================================================
// 【役割】
// カメラがプレイヤーをスムーズに追従するシンプルなスクリプト。
// GameBootstrapper によって自動的に Main Camera にアタッチされる。
//
// 【LateUpdateを使う理由】
// Update はすべてのスクリプトが処理を終えた後に呼ばれるわけではない。
// LateUpdate はすべてのUpdateが完了してから呼ばれるため、
// プレイヤーが動いた後にカメラを追従させるのに最適なタイミングになる。
// ============================================================

using UnityEngine;

public class PlayerFollowCamera : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値
    // ──────────────────────────────────────────────

    [Header("追従対象")]
    [Tooltip("追従するターゲット（プレイヤーのTransform）")]
    public Transform target;

    [Header("追従設定")]
    [Tooltip("追従の滑らかさ（大きいほど素早くプレイヤーに追いつく）")]
    public float smoothSpeed = 6f;

    [Tooltip("カメラのZ軸オフセット（2DではプレイヤーよりZ手前に置く）")]
    public float zOffset = -10f;

    // ──────────────────────────────────────────────
    //  メインループ（LateUpdateでプレイヤー移動後に実行）
    // ──────────────────────────────────────────────

    private void LateUpdate()
    {
        if (target == null)
            return;

        MoveTowardTarget();
    }

    // ──────────────────────────────────────────────
    //  追従処理
    // ──────────────────────────────────────────────

    /// <summary>
    /// カメラをプレイヤーのXY座標に向けてLerpで滑らかに移動させる。
    ///
    /// 【unscaledDeltaTimeを使う理由】
    /// TopDownPlayer がスローモーション中（Time.timeScale < 1）でも
    /// カメラはリアルタイムで追従し続けるようにする。
    /// </summary>
    private void MoveTowardTarget()
    {
        Vector3 desiredPosition = new Vector3(
            target.position.x,
            target.position.y,
            zOffset
        );

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            smoothSpeed * Time.unscaledDeltaTime
        );
    }
}
