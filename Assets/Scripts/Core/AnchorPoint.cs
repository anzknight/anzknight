// ============================================================
// [AnchorPoint.cs]
// 2Dトップダウン型アクションゲーム - アンカーポイント（旋回支点）
// ============================================================
// 【役割】
// シーン内に配置する「旋回の支点」となるオブジェクトにアタッチするマーカー。
// TopDownPlayer.cs が AnchorPoint[] を FindObjectsByType で検索し、
// 最寄りのアンカーに向かって引力を計算するために使う。
//
// このスクリプト自体はシンプルだが、UnityエディタのGizmosで
// 「どこにアンカーがあるか」を視覚的に確認できるように実装している。
// ============================================================

using UnityEngine;

public class AnchorPoint : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値
    // ──────────────────────────────────────────────

    [Header("アンカー設定")]
    [Tooltip("引力の有効範囲（この半径内のプレイヤーにのみ引力が働く）")]
    public float influenceRadius = 10f;

    [Tooltip("このアンカーの引力の強さ（TopDownPlayerのattractionStrengthと掛け合わされる）")]
    public float attractionMultiplier = 1f;

    // ──────────────────────────────────────────────
    //  Gizmos（エディタ上での視覚的補助）
    // ──────────────────────────────────────────────

    /// <summary>
    /// エディタのScene/Game画面でアンカーの影響範囲を円で表示する。
    /// ゲームの実行には影響しない（デバッグ用）。
    /// </summary>
    private void OnDrawGizmos()
    {
        // 通常時はシアン色の円で影響範囲を表示
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
        Gizmos.DrawSphere(transform.position, influenceRadius);

        // アンカー中心点を白い十字で表示
        Gizmos.color = Color.white;
        float crossSize = 0.3f;
        Gizmos.DrawLine(transform.position - Vector3.right * crossSize,
                        transform.position + Vector3.right * crossSize);
        Gizmos.DrawLine(transform.position - Vector3.up * crossSize,
                        transform.position + Vector3.up * crossSize);
    }

    /// <summary>
    /// このアンカーが選択されているときは、より鮮やかに表示する。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, influenceRadius);
    }
}
