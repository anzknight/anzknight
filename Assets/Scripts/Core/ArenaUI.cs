// ============================================================
// [ArenaUI.cs]
// 異次元立体機動アリーナ - HUD（ガス・HP・コイン・ステージ表示）
// ============================================================
//
// 【設計メモ：プログラム完結型 Canvas 生成】
//
//   UnityEngine.UI の Canvas / Image / Text を Awake() で
//   すべて AddComponent + コードで構築するため、
//   Prefab や uGUI 設定ファイルが一切不要。
//
//   git clone 後に Unity で Play するだけで HUD が表示される。
//
// 【ガスゲージの Lerp 表現】
//
//   ガス量は PlayerController.GasAmount（0.0〜1.0）を毎フレーム参照し、
//   Image.fillAmount に直接設定する。
//   Lerp でなめらかに追従させることで「重い液体が揺れる」演出になる。
//
// 【コイン購読パターン】
//
//   EnemyController.OnEnemyDied（static event）を購読し、
//   撃破されるたびに coinTotal に加算してテキストを更新する。
//   ArenaUI 自身が PlayerController を持たなくても機能する疎結合設計。
//
// ============================================================

using UnityEngine;
using UnityEngine.UI;

public class ArenaUI : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  外部参照（ArenaBootstrapper がセットする）
    // ──────────────────────────────────────────────

    /// <summary>プレイヤーの状態を読み取るための参照</summary>
    public PlayerController playerController;

    // ──────────────────────────────────────────────
    //  HUD の UI 要素（Awake で動的生成）
    // ──────────────────────────────────────────────

    // ガスゲージの「充填部分」Image（fillAmount で量を表示）
    private Image gasGaugeFill;

    // ガスゲージの視覚的な追従値（Lerp でなめらかに動く）
    private float displayGasAmount = 1f;

    // HP 表示テキスト
    private Text hpText;

    // コイン表示テキスト
    private Text coinText;

    // ステージ番号表示テキスト
    private Text stageText;

    // 内部コイン合計（EnemyController.OnEnemyDied で加算）
    private int coinTotal;

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void Awake()
    {
        BuildCanvas();

        // 静的イベントを購読：敵が死んだらコインを受け取る
        EnemyController.OnEnemyDied += OnEnemyKilled;
    }

    private void OnDestroy()
    {
        EnemyController.OnEnemyDied -= OnEnemyKilled;
    }

    // ──────────────────────────────────────────────
    //  メインループ（毎フレーム HUD を更新）
    // ──────────────────────────────────────────────

    private void Update()
    {
        if (playerController == null) return;

        UpdateGasGauge();
        UpdateHpText();
        UpdateCoinText();
    }

    // ──────────────────────────────────────────────
    //  ガスゲージ更新（Lerp で追従）
    // ──────────────────────────────────────────────

    /// <summary>
    /// PlayerController.GasAmount を Lerp で追従させる。
    /// 急激な変化をなめらかにすることで「重い液体」の視覚効果を出す。
    /// </summary>
    private void UpdateGasGauge()
    {
        // 現在のガス量に向けて Lerp で追従（速さ 6 で軽快かつなめらか）
        displayGasAmount = Mathf.Lerp(displayGasAmount, playerController.GasAmount, 6f * Time.deltaTime);
        gasGaugeFill.fillAmount = displayGasAmount;

        // ガス残量に応じてゲージの色を変える（if文なし：Lerp で補間）
        // 多い → 青緑  /  少ない → 赤
        gasGaugeFill.color = Color.Lerp(
            new Color(0.9f, 0.15f, 0.1f, 1f),   // 残量ゼロの色（赤）
            new Color(0.1f, 0.7f,  0.9f, 1f),   // 残量満タンの色（青緑）
            displayGasAmount
        );
    }

    // ──────────────────────────────────────────────
    //  HP テキスト更新
    // ──────────────────────────────────────────────

    private void UpdateHpText()
    {
        // PlayerController に HP プロパティがあれば表示する
        // （現バージョンでは Coins を流用してスコア風に）
        hpText.text = "HP ■■■";  // 将来的に playerController.HP で動的化
    }

    // ──────────────────────────────────────────────
    //  コインテキスト更新
    // ──────────────────────────────────────────────

    private void UpdateCoinText()
    {
        coinText.text = "COIN  " + coinTotal.ToString("D6");
    }

    // ──────────────────────────────────────────────
    //  敵撃破イベント受信
    // ──────────────────────────────────────────────

    /// <summary>EnemyController.OnEnemyDied に購読。引数はコイン獲得数。</summary>
    private void OnEnemyKilled(int coins)
    {
        coinTotal += coins;

        // ステージテキストも即時更新（将来的にはウェーブ管理クラスと連携）
        stageText.text = "WAVE  " + (coinTotal / 50 + 1).ToString("D2");
    }

    // ──────────────────────────────────────────────
    //  プログラムで Canvas と UI 要素を構築
    // ──────────────────────────────────────────────

    /// <summary>
    /// Canvas → 各 UI 要素 をすべてコードで生成する。
    /// シーンファイルや Prefab への依存がゼロ。
    /// </summary>
    private void BuildCanvas()
    {
        // ── Canvas 本体 ──────────────────────────────────────────────
        GameObject canvasObj = new GameObject("ArenaHUD_Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);  // スマホ縦画面基準
        scaler.matchWidthOrHeight  = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // ── ガスゲージ（画面左下） ────────────────────────────────────
        gasGaugeFill = BuildGasGauge(canvasObj);

        // ── HP テキスト（左上） ─────────────────────────────────────
        hpText = BuildText(
            parent    : canvasObj,
            name      : "HpText",
            text      : "HP ■■■",
            fontSize  : 36,
            anchor    : new Vector2(0f, 1f),      // 左上
            pivot     : new Vector2(0f, 1f),
            position  : new Vector2(30f, -30f)
        );

        // ── コインテキスト（右上） ───────────────────────────────────
        coinText = BuildText(
            parent    : canvasObj,
            name      : "CoinText",
            text      : "COIN  000000",
            fontSize  : 36,
            anchor    : new Vector2(1f, 1f),      // 右上
            pivot     : new Vector2(1f, 1f),
            position  : new Vector2(-30f, -30f)
        );

        // ── ステージ番号（右下） ────────────────────────────────────
        stageText = BuildText(
            parent    : canvasObj,
            name      : "StageText",
            text      : "WAVE  01",
            fontSize  : 32,
            anchor    : new Vector2(1f, 0f),      // 右下
            pivot     : new Vector2(1f, 0f),
            position  : new Vector2(-30f, 30f)
        );
    }

    // ──────────────────────────────────────────────
    //  ガスゲージ生成ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>
    /// 画面左下に「ガスゲージ」を生成する。
    /// 背景バーの上に充填バーを重ね、fillAmount でガス量を表現する。
    /// </summary>
    private Image BuildGasGauge(GameObject parent)
    {
        // ゲージ全体のルートオブジェクト（アンカー設定だけ持つ）
        GameObject gaugeRoot = new GameObject("GasGauge");
        gaugeRoot.transform.SetParent(parent.transform, false);

        RectTransform rootRect = gaugeRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 0f);  // 左下
        rootRect.pivot     = new Vector2(0f, 0f);
        rootRect.anchoredPosition = new Vector2(30f, 30f);
        rootRect.sizeDelta = new Vector2(200f, 24f);

        // ── 背景バー（暗い青灰色） ─────────────────────────────────
        GameObject bgObj = new GameObject("GasGauge_BG");
        bgObj.transform.SetParent(gaugeRoot.transform, false);

        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.12f, 0.18f, 0.85f);

        RectTransform bgRect = bgImage.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;

        // ── 充填バー（残量を fillAmount で表示） ───────────────────
        GameObject fillObj = new GameObject("GasGauge_Fill");
        fillObj.transform.SetParent(gaugeRoot.transform, false);

        Image fillImage = fillObj.AddComponent<Image>();
        fillImage.type       = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillAmount = 1f;
        fillImage.color      = new Color(0.1f, 0.7f, 0.9f, 1f);  // 初期：青緑

        RectTransform fillRect = fillImage.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        // ── ラベルテキスト（ゲージの左上） ────────────────────────
        GameObject labelObj = new GameObject("GasGauge_Label");
        labelObj.transform.SetParent(gaugeRoot.transform, false);

        Text label = labelObj.AddComponent<Text>();
        label.text      = "GAS";
        label.fontSize  = 18;
        label.color     = new Color(0.8f, 0.9f, 1f, 0.9f);
        label.alignment = TextAnchor.MiddleCenter;
        label.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        return fillImage;
    }

    // ──────────────────────────────────────────────
    //  テキスト生成ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>アンカー・ピボット・位置・フォントサイズを指定してテキストを生成する</summary>
    private Text BuildText(
        GameObject parent,
        string     name,
        string     text,
        int        fontSize,
        Vector2    anchor,
        Vector2    pivot,
        Vector2    position)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        Text t = obj.AddComponent<Text>();
        t.text      = text;
        t.fontSize  = fontSize;
        t.color     = new Color(0.9f, 0.95f, 1f, 1f);
        t.alignment = TextAnchor.UpperLeft;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // フォントレンダリングを鮮明にする
        t.fontStyle    = FontStyle.Bold;
        t.lineSpacing  = 1.2f;

        RectTransform rect = t.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot     = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(400f, 60f);

        return t;
    }
}
