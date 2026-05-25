// ============================================================
// [ArenaBootstrapper.cs]
// 異次元立体機動アリーナ - シーン自動構築（新システム対応版）
// ============================================================
//
// 【役割】
// InputManager / PlayerController / VisualManager / SaveManager /
// EnemyController / ArenaUI を持つ全オブジェクトを
// コードで動的生成し、git clone 直後でも即 Play 可能にする。
//
// 【生成するオブジェクト】
//   [ArenaCamera]     → PlayerFollowCamera（プレイヤー追従）
//   [InputReceiver]   → InputManager（入力抽象化）
//   [Player]          → PlayerController + VisualManager + 体スプライト
//   [AnchorPoint_0〜N]→ AnchorPoint（旋回支点・墓石）
//   [Enemy_0〜N]      → EnemyController（追跡型の敵）
//   [ArenaUI]         → ガスゲージ・HP・コイン・ステージ表示
//
// 【SaveManager との連携】
//   SaveManager はシングルトンなので Start() で Instance を参照し、
//   続きからプレイのデータがあればプレイヤー座標を復元する。
// ============================================================

using UnityEngine;

public class ArenaBootstrapper : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector 設定値
    // ──────────────────────────────────────────────

    [Header("プレイヤー設定")]
    [Tooltip("プレイヤーの描画半径（Units）")]
    public float playerRadius = 0.45f;

    [Tooltip("プレイヤーのスプライト色")]
    public Color playerColor = new Color(0.9f, 0.95f, 1f, 1f);

    [Header("アンカー設定（墓石）")]
    [Tooltip("配置するアンカーの数")]
    public int anchorCount = 5;

    [Tooltip("プレイヤーを中心としたアンカーの配置半径（Units）")]
    public float anchorRadius = 6f;

    [Tooltip("アンカーのスプライト色（墓石の色）")]
    public Color anchorColor = new Color(0.55f, 0.6f, 0.7f, 1f);

    [Header("敵の初期配置")]
    [Tooltip("アリーナ開始時に生成する敵の数")]
    public int initialEnemyCount = 3;

    [Tooltip("敵がスポーンするプレイヤーからの最小距離（Units）")]
    public float enemySpawnRadius = 9f;

    [Header("カメラ設定")]
    [Tooltip("カメラの正射影サイズ（広いほど広い視野）")]
    public float cameraSize = 8f;

    // ──────────────────────────────────────────────
    //  エントリポイント
    // ──────────────────────────────────────────────

    private void Start()
    {
        // 生成順序が重要：依存関係に注意
        // 1. SaveManager（シングルトン → まだなければ生成）
        EnsureSaveManager();

        // 2. InputManager（PlayerController が依存する）
        InputManager inputMgr = CreateInputManager();

        // 3. Player（PlayerController + VisualManager を持つ）
        GameObject player = CreatePlayer(inputMgr);

        // 4. AnchorPoint（PlayerController.FindNearestAnchor が使う）
        CreateAnchorPoints();

        // 5. WaveManager（EnemyController.OnEnemyDied を購読し敵をウェーブ管理する）
        //    SpawnEnemies は WaveManager が担うため、旧 SpawnEnemies() は呼ばない
        CreateWaveManager(player);

        // 6. Camera（Player の Transform が必要）
        SetupCamera(player);

        // 7. UI（PlayerController を参照）
        CreateArenaUI(player);

        // 8. セーブデータで座標を復元
        RestoreSaveData(player);
    }

    // ──────────────────────────────────────────────
    //  SaveManager 確保
    // ──────────────────────────────────────────────

    private void EnsureSaveManager()
    {
        // SaveManager.Instance が null の場合のみ新規生成
        if (SaveManager.Instance == null)
        {
            GameObject smObj = new GameObject("SaveManager");
            smObj.AddComponent<SaveManager>();
        }
    }

    // ──────────────────────────────────────────────
    //  InputManager 生成
    // ──────────────────────────────────────────────

    private InputManager CreateInputManager()
    {
        GameObject obj = new GameObject("InputReceiver");
        return obj.AddComponent<InputManager>();
    }

    // ──────────────────────────────────────────────
    //  プレイヤー生成
    // ──────────────────────────────────────────────

    private GameObject CreatePlayer(InputManager inputMgr)
    {
        // ── ルート GameObject（物理・スクリプト群を持つ）──────────
        GameObject player = new GameObject("Player");
        player.transform.position = Vector3.zero;

        // Rigidbody2D：AddComponent する前に PlayerController.Awake が走るため
        // PlayerController より先に追加する必要がある
        Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale   = 0f;
        rb.freezeRotation = true;
        rb.linearDamping  = 0.4f;

        // CircleCollider2D：敵との当たり判定に使う
        CircleCollider2D col = player.AddComponent<CircleCollider2D>();
        col.radius = playerRadius * 0.8f;

        // ── スプライト専用の子 GameObject（回転アニメーションをここに適用）──
        GameObject spriteObj = new GameObject("PlayerSprite");
        spriteObj.transform.SetParent(player.transform, false);

        SpriteRenderer sr = spriteObj.AddComponent<SpriteRenderer>();
        sr.sprite       = CreateCircleSprite(64, playerColor);
        sr.sortingOrder = 5;
        // 1x1 の Sprite を playerRadius のサイズにスケール
        spriteObj.transform.localScale = Vector3.one * (playerRadius * 2f);

        // ── 各システムコンポーネントを追加 ──────────────────────
        // LineRenderer（SoulWire / VisualManager が使う）
        player.AddComponent<LineRenderer>();

        // VisualManager（先に追加して PlayerController のイベントに購読させる）
        VisualManager vm = player.AddComponent<VisualManager>();

        // PlayerController（最後に追加：Awake で rb を GetComponent する）
        PlayerController pc = player.AddComponent<PlayerController>();

        // Inspector でつなぐはずだったフィールドをコードで接続
        pc.inputManager    = inputMgr;
        pc.spriteTransform = spriteObj.transform; // 回転アニメーション対象

        vm.inputManager    = inputMgr;
        vm.playerController = pc;

        return player;
    }

    // ──────────────────────────────────────────────
    //  アンカーポイント生成（墓石）
    // ──────────────────────────────────────────────

    private void CreateAnchorPoints()
    {
        for (int i = 0; i < anchorCount; i++)
        {
            // 円周上に等間隔配置
            float angle = (360f / anchorCount) * i * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(
                Mathf.Cos(angle) * anchorRadius,
                Mathf.Sin(angle) * anchorRadius,
                0f
            );

            GameObject anchor = new GameObject("AnchorPoint_" + i);
            anchor.transform.position = pos;

            // 小さな四角い墓石スプライト（灰色）
            SpriteRenderer sr = anchor.AddComponent<SpriteRenderer>();
            sr.sprite       = CreateSquareSprite(anchorColor);
            sr.sortingOrder = 3;
            anchor.transform.localScale = Vector3.one * 0.5f;

            // Collider2D（プレイヤーが衝突検出に使える）
            anchor.AddComponent<BoxCollider2D>();

            // AnchorPoint コンポーネント（PlayerController.FindNearestAnchor で検索される）
            anchor.AddComponent<AnchorPoint>();
        }
    }

    // ──────────────────────────────────────────────
    //  WaveManager 生成（敵管理を委譲）
    // ──────────────────────────────────────────────

    private void CreateWaveManager(GameObject player)
    {
        GameObject waveObj = new GameObject("WaveManager");
        WaveManager wm = waveObj.AddComponent<WaveManager>();
        wm.playerTransform  = player.transform;
        wm.spawnRadius      = enemySpawnRadius;
        wm.baseEnemyCount   = initialEnemyCount;
    }

    // ──────────────────────────────────────────────
    //  カメラ設定
    // ──────────────────────────────────────────────

    private void SetupCamera(GameObject player)
    {
        // Main Camera が既にシーンにあれば再利用、なければ生成
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camObj = new GameObject("ArenaCamera");
            camObj.tag = "MainCamera";
            cam = camObj.AddComponent<Camera>();
        }

        cam.orthographic     = true;
        cam.orthographicSize = cameraSize;
        cam.backgroundColor  = new Color(0.04f, 0.04f, 0.08f, 1f); // 深い夜空色
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        // PlayerFollowCamera：プレイヤーを滑らかに追跡
        PlayerFollowCamera follow = cam.GetComponent<PlayerFollowCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<PlayerFollowCamera>();
        follow.target      = player.transform;
        follow.smoothSpeed = 5f;
        follow.zOffset     = -10f;
    }

    // ──────────────────────────────────────────────
    //  ArenaUI 生成
    // ──────────────────────────────────────────────

    private void CreateArenaUI(GameObject player)
    {
        GameObject uiObj = new GameObject("ArenaUI");
        ArenaUI ui = uiObj.AddComponent<ArenaUI>();
        ui.playerController = player.GetComponent<PlayerController>();
    }

    // ──────────────────────────────────────────────
    //  セーブデータで座標を復元
    // ──────────────────────────────────────────────

    private void RestoreSaveData(GameObject player)
    {
        // PlayerController.Start() でロードしているが、
        // Bootstrapper 側でも確認して位置を補正する
        if (SaveManager.Instance != null && SaveManager.Instance.HasSaveData)
        {
            player.transform.position = SaveManager.Instance.CurrentData.Position;
        }
    }

    // ──────────────────────────────────────────────
    //  プロシージャルスプライト生成（アセット不要）
    // ──────────────────────────────────────────────

    /// <summary>アンチエイリアス付きの円スプライトを Texture2D から生成する</summary>
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
                float dist  = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - (dist - (radius - 1f)));
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
    }

    /// <summary>シンプルな白塗り四角スプライトを Texture2D から生成する（墓石用）</summary>
    private Sprite CreateSquareSprite(Color color)
    {
        Texture2D tex = new Texture2D(32, 48, TextureFormat.RGBA32, false);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 32; x++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 32, 48), new Vector2(0.5f, 0.5f), 32);
    }
}
