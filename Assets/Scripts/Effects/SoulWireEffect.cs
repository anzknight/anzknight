// ============================================================
// [SoulWireEffect.cs]
// 2Dトップダウン型アクションゲーム - 魂のワイヤーエフェクト
// ============================================================
// 【役割】
// プレイヤーと最寄りのAnchorPointの間に「青く発光するワイヤー」を描く。
// Unity標準のLineRendererをスクリプトから完全制御する。
//
// 【LineRendererの仕組み】
// LineRendererは「positionsで指定した複数の点を繋ぐ線」を描くコンポーネント。
// 今回は points[0] = プレイヤー位置、points[1] = アンカー位置 の2点を
// 毎フレーム更新することで「プレイヤーとアンカーを繋ぐ線」が動的に描かれる。
//
// 【発光表現の仕組み】
// UnityのHDRカラー（HDR有効のColorプロパティ）は輝度が1.0を超えられる。
// これにより、Post-Processingの「Bloom」があれば本物のグロウが発生する。
// Bloomがない場合でも、Width（幅）をSin波でアニメーションすることで
// 「脈打つ光」のように見せることができる。
// ============================================================

using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SoulWireEffect : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値
    // ──────────────────────────────────────────────

    [Header("ワイヤー外観")]
    [Tooltip("ワイヤーの最小幅（脈動の底）")]
    public float minWireWidth = 0.05f;

    [Tooltip("ワイヤーの最大幅（脈動の頂点）")]
    public float maxWireWidth = 0.15f;

    [Tooltip("脈動の速さ（Hz：1秒に何回脈打つか）")]
    public float pulseFrequency = 3f;

    [Tooltip("ワイヤーの基本色（青い発光）")]
    public Color wireColor = new Color(0.3f, 0.6f, 1f, 1f);

    [Tooltip("ワイヤーの発光強度（HDR色の輝度倍率。Bloomがある場合に効果的）")]
    public float glowIntensity = 2.5f;

    [Header("アンカー検索")]
    [Tooltip("この距離内のアンカーのみ対象にする（0 = 距離制限なし）")]
    public float maxSearchDistance = 20f;

    // ──────────────────────────────────────────────
    //  公開プロパティ（TopDownPlayerからアクセスされる）
    // ──────────────────────────────────────────────

    /// <summary>現在ワイヤーが繋がっている最寄りのアンカー。nullの場合はなし。</summary>
    public Transform NearestAnchor { get; private set; }

    // ──────────────────────────────────────────────
    //  内部状態変数
    // ──────────────────────────────────────────────

    private LineRenderer wireLineRenderer;    // ワイヤーを描くLineRenderer
    private Transform playerTransform;        // プレイヤーのTransform（毎フレーム参照）
    private bool isWireActive;                // ワイヤーが表示中かどうか
    private float activationTime;             // ActivateWire が呼ばれた時刻（脈動位相計算用）

    // ──────────────────────────────────────────────
    //  初期化：LineRendererをコードで設定
    // ──────────────────────────────────────────────

    private void Awake()
    {
        wireLineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
    }

    /// <summary>
    /// LineRendererの見た目をコードから設定する。
    /// 外部マテリアルを使わず、UnityのDefault-Lineマテリアルを流用する。
    /// </summary>
    private void ConfigureLineRenderer()
    {
        // ライン描画点数を2点（プレイヤー → アンカー）に固定
        wireLineRenderer.positionCount = 2;

        // ワールド空間で位置を計算する（ローカル空間ではない）
        wireLineRenderer.useWorldSpace = true;

        // 線の先端を丸くする（視覚的に滑らか）
        wireLineRenderer.numCapVertices = 4;

        // デフォルトのLineマテリアルを使い、加算ブレンドで発光感を出す
        // Resources.Load は使わず、Unityの標準スプライトを利用
        Material wireMaterial = new Material(Shader.Find("Sprites/Default"));
        wireMaterial.renderQueue = 3000; // Transparent キュー
        wireLineRenderer.material = wireMaterial;

        // 初期状態では非表示
        wireLineRenderer.enabled = false;
    }

    // ──────────────────────────────────────────────
    //  メインループ
    // ──────────────────────────────────────────────

    private void Update()
    {
        if (!isWireActive)
            return;

        UpdateWirePositions();  // ワイヤーの両端点を更新
        UpdateWireAppearance(); // 幅と色を脈動させる
    }

    // ──────────────────────────────────────────────
    //  ワイヤー制御のパブリックAPI
    // ──────────────────────────────────────────────

    /// <summary>
    /// ワイヤーをアクティブにする。最寄りのアンカーを検索して表示を開始する。
    /// TopDownPlayer.OnTouchBegan から呼ばれる。
    /// </summary>
    public void ActivateWire(Transform player)
    {
        playerTransform = player;
        NearestAnchor = FindNearestAnchor(player.position);

        // アンカーが見つかった場合のみ表示する
        if (NearestAnchor != null)
        {
            isWireActive = true;
            wireLineRenderer.enabled = true;
            activationTime = Time.unscaledTime; // 脈動の位相をリセット
        }
    }

    /// <summary>
    /// ワイヤーを非アクティブにして非表示にする。
    /// TopDownPlayer.OnTouchEnded から呼ばれる。
    /// </summary>
    public void DeactivateWire()
    {
        isWireActive = false;
        wireLineRenderer.enabled = false;
        NearestAnchor = null;
    }

    // ──────────────────────────────────────────────
    //  内部更新処理
    // ──────────────────────────────────────────────

    /// <summary>
    /// LineRendererの両端点をプレイヤーとアンカーの現在位置に合わせて更新する。
    /// 毎フレーム呼ばれることで、動くプレイヤーにワイヤーが追随する。
    /// </summary>
    private void UpdateWirePositions()
    {
        if (playerTransform == null || NearestAnchor == null)
            return;

        // Z軸は0に固定（2Dゲームなのでワイヤーも同一平面上に描く）
        Vector3 playerPos = new Vector3(playerTransform.position.x, playerTransform.position.y, 0f);
        Vector3 anchorPos = new Vector3(NearestAnchor.position.x, NearestAnchor.position.y, 0f);

        wireLineRenderer.SetPosition(0, playerPos);
        wireLineRenderer.SetPosition(1, anchorPos);
    }

    /// <summary>
    /// Sin波を使ってワイヤーの太さを脈動させ、生命感のある表現にする。
    ///
    /// 【Sin波の使い方】
    /// Mathf.Sin(time * frequency) は -1〜+1 の波を返す。
    /// InverseLerp で 0〜1 に正規化し、Lerp で minWidth〜maxWidth に変換する。
    /// </summary>
    private void UpdateWireAppearance()
    {
        // アクティブになってからの経過時間で位相を計算
        float elapsed = Time.unscaledTime - activationTime;

        // Sin波：-1〜1 の値 → 0〜1 に正規化
        float sinValue = Mathf.Sin(elapsed * pulseFrequency * Mathf.PI * 2f);
        float normalizedPulse = (sinValue + 1f) * 0.5f;

        // ワイヤー幅を脈動させる
        float currentWidth = Mathf.Lerp(minWireWidth, maxWireWidth, normalizedPulse);
        wireLineRenderer.startWidth = currentWidth;
        wireLineRenderer.endWidth = currentWidth * 0.5f; // 末端は細くしてテーパー状に

        // HDRカラーで発光強度も脈動させる
        // glowIntensity倍を掛けることでBloomエフェクトがあれば本物の光彩になる
        float currentGlow = Mathf.Lerp(glowIntensity * 0.7f, glowIntensity, normalizedPulse);
        Color glowColor = wireColor * currentGlow;
        wireLineRenderer.startColor = glowColor;
        wireLineRenderer.endColor = new Color(glowColor.r, glowColor.g, glowColor.b, glowColor.a * 0.5f);
    }

    // ──────────────────────────────────────────────
    //  最寄りアンカーの検索
    // ──────────────────────────────────────────────

    /// <summary>
    /// シーン内のすべてのAnchorPointを検索し、最も近いものを返す。
    /// パフォーマンスのため、ActivateWire時（ホールド開始時）の1回のみ呼ぶ。
    /// </summary>
    private Transform FindNearestAnchor(Vector3 playerPosition)
    {
        // シーン内のすべてのAnchorPointコンポーネントを取得
        AnchorPoint[] allAnchors = FindObjectsByType<AnchorPoint>(FindObjectsSortMode.None);

        Transform nearestTransform = null;
        float nearestDistance = float.MaxValue;

        foreach (AnchorPoint anchor in allAnchors)
        {
            float distance = Vector3.Distance(playerPosition, anchor.transform.position);

            // 検索距離制限内で、かつ今まで見つかった中で最も近いかチェック
            bool withinRange = maxSearchDistance <= 0f || distance <= maxSearchDistance;
            if (withinRange && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTransform = anchor.transform;
            }
        }

        return nearestTransform;
    }
}
