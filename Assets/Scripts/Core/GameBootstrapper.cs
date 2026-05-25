// ============================================================
// [GameBootstrapper.cs]
// 2Dトップダウン型アクションゲーム - シーン自動構築ブートストラッパー
// ============================================================
// 【役割】
// git clone 直後にUnityで開いてもすぐPlayできるよう、
// シーン内の全GameObjectをコードで動的生成する。
//
// 【なぜコードでシーンを構築するのか？】
// Unityの .unity ファイルはスクリプトをGUID（.metaファイルのID）で参照する。
// git cloneした環境では .meta が揃っていればGUIDは一致するが、
// アタッチ済みコンポーネントのフィールド参照（Inspector接続）が
// 切れることを完全に防ぐためにはコード生成が最も確実。
//
// 【生成されるオブジェクト】
//   [Player]
//     ├─ Rigidbody2D（重力なし・回転ロック）
//     ├─ SpriteRenderer（白い円・プロシージャル生成）
//     ├─ LineRenderer（SoulWireEffectが使用）
//     ├─ SoulWireEffect
//     ├─ GasThrustEffect
//     ├─ SlashTrailEffect
//     └─ TopDownPlayer（上記全てへの参照を接続）
//
//   [AnchorPoint_0〜N]（シーン四方に配置）
//     ├─ SpriteRenderer（小さな円）
//     └─ AnchorPoint
//
//   [Main Camera] は既存のものを再利用し、PlayerFollowCameraを追加
// ============================================================

using UnityEngine;

public class GameBootstrapper : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値
    // ──────────────────────────────────────────────

    [Header("プレイヤー外観")]
    [Tooltip("プレイヤーの色")]
    public Color playerColor = Color.white;

    [Tooltip("プレイヤーのスプライト半径（Unitsサイズ）")]
    public float playerRadius = 0.4f;

    [Header("アンカー設定")]
    [Tooltip("配置するアンカーの数")]
    public int anchorCount = 4;

    [Tooltip("プレイヤーからアンカーまでの距離（Units）")]
    public float anchorSpread = 5f;

    [Tooltip("アンカーの色")]
    public Color anchorColor = new Color(0.3f, 0.8f, 1f, 0.8f);

    [Header("カメラ設定")]
    [Tooltip("カメラの正射影サイズ（大きいほど広い視野）")]
    public float cameraOrthographicSize = 7f;

    // ──────────────────────────────────────────────
    //  エントリポイント
    // ──────────────────────────────────────────────

    private void Start()
    {
        SetupCamera();
        GameObject player = CreatePlayer();
        CreateAnchorPoints();
        SetupCameraFollow(player);
    }

    // ──────────────────────────────────────────────
    //  カメラ設定（既存のMain Cameraを調整）
    // ──────────────────────────────────────────────

    private void SetupCamera()
    {
        if (Camera.main == null)
            return;

        Camera cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = cameraOrthographicSize;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f, 1f);
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    // ──────────────────────────────────────────────
    //  プレイヤー生成
    // ──────────────────────────────────────────────

    private GameObject CreatePlayer()
    {
        GameObject player = new GameObject("Player");
        player.transform.position = Vector3.zero;

        // ── SpriteRenderer（プロシージャル円スプライト）────────────
        SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite(64, playerColor);
        sr.sortingOrder = 5;
        // スプライトは1x1単位なのでlocalScaleでradius相当にスケール
        player.transform.localScale = Vector3.one * (playerRadius * 2f);

        // ── Rigidbody2D ──────────────────────────────────────────────
        Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.linearDamping = 0.5f;

        // ── SoulWireEffect（LineRenderer必須）────────────────────────
        // LineRendererはSoulWireEffectの[RequireComponent]で自動追加されるが、
        // 明示的に先に追加して設定を保証する
        player.AddComponent<LineRenderer>();
        SoulWireEffect soulWire = player.AddComponent<SoulWireEffect>();

        // ── GasThrustEffect ──────────────────────────────────────────
        GasThrustEffect gasThrust = player.AddComponent<GasThrustEffect>();

        // ── SlashTrailEffect ─────────────────────────────────────────
        SlashTrailEffect slashTrail = player.AddComponent<SlashTrailEffect>();

        // ── TopDownPlayer（全エフェクトへの参照を接続）──────────────
        TopDownPlayer playerCtrl = player.AddComponent<TopDownPlayer>();
        playerCtrl.soulWireEffect  = soulWire;
        playerCtrl.gasThrustEffect = gasThrust;
        playerCtrl.slashTrailEffect = slashTrail;

        return player;
    }

    // ──────────────────────────────────────────────
    //  アンカーポイント生成
    // ──────────────────────────────────────────────

    private void CreateAnchorPoints()
    {
        for (int i = 0; i < anchorCount; i++)
        {
            // アンカーを円周上に等間隔配置
            float angle = (360f / anchorCount) * i * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(
                Mathf.Cos(angle) * anchorSpread,
                Mathf.Sin(angle) * anchorSpread,
                0f
            );

            GameObject anchor = new GameObject("AnchorPoint_" + i);
            anchor.transform.position = pos;

            // ── SpriteRenderer（小さな円）─────────────────────────
            SpriteRenderer sr = anchor.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(32, anchorColor);
            sr.sortingOrder = 3;
            anchor.transform.localScale = Vector3.one * 0.3f;

            // ── AnchorPoint コンポーネント ─────────────────────────
            anchor.AddComponent<AnchorPoint>();
        }
    }

    // ──────────────────────────────────────────────
    //  カメラ追従設定
    // ──────────────────────────────────────────────

    private void SetupCameraFollow(GameObject player)
    {
        if (Camera.main == null)
            return;

        PlayerFollowCamera followCam = Camera.main.GetComponent<PlayerFollowCamera>();
        if (followCam == null)
            followCam = Camera.main.gameObject.AddComponent<PlayerFollowCamera>();

        followCam.target = player.transform;
        followCam.smoothSpeed = 6f;
        followCam.zOffset = -10f;
    }

    // ──────────────────────────────────────────────
    //  プロシージャル円スプライト生成
    // ──────────────────────────────────────────────

    /// <summary>
    /// Texture2Dを使い、アンチエイリアス付きの円スプライトを生成する。
    /// 外部テクスチャ不要・実行時生成なのでどの環境でも動作する。
    /// </summary>
    private Sprite CreateCircleSprite(int resolution, Color color)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float center = resolution * 0.5f;
        float radius = center - 1f;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));

                // アンチエイリアス：境界付近でアルファを0〜1にグラデーション
                float alpha = Mathf.Clamp01(1f - (dist - (radius - 1f)));
                Color pixelColor = new Color(color.r, color.g, color.b, color.a * alpha);

                tex.SetPixel(x, y, pixelColor);
            }
        }

        tex.Apply();

        return Sprite.Create(
            tex,
            new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f),
            resolution  // PPU = resolution → スプライトが1Unity単位になる
        );
    }
}
