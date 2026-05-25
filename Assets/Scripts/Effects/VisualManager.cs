// ============================================================
// [VisualManager.cs]
// 異次元立体機動アリーナ - 完全プログラム完結型エフェクト管理
// ============================================================
//
// 【設計メモ：3つのエフェクトの仕組み】
//
// ① ワイヤーエフェクト（ホールド中）
//   LineRenderer を動的制御して「プレイヤー → アンカー」間に
//   HDR 青色の発光線を描く。Sin 波でアルファと太さを脈動させる。
//
// ② ガス噴射エフェクト（スワイプ中）
//   PlayerController.CurrentVelocity の逆方向へ ParticleSystem を向ける。
//   スワイプがあるフレームのみ emission.rateOverTime を上げる。
//
// ③ 時間差空間両断エフェクト（アンタップから 0.2 秒後）
//   PlayerController.OnDashStarted イベントで「軌跡の始点」を受け取る。
//   0.2 秒後に実際の終点を記録して始点〜終点を結ぶ LineRenderer を生成。
//   同時に軌跡上の複数点に ParticleSystem バーストを爆発させる。
//   LineRenderer の幅を AnimationCurve で「鋭いピーク→急速消滅」させる。
//
// 【完全内製の理由】
//   外部アセット（Effekseer、Shader Graph 等）は容量・ライセンスコストが大きい。
//   LineRenderer + ParticleSystem の AddComponent だけで実質的に同等の演出が可能。
//
// ============================================================

using System.Collections;
using UnityEngine;

public class VisualManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector 設定値
    // ──────────────────────────────────────────────

    [Header("参照")]
    [Tooltip("入力状態を受け取る InputManager")]
    public InputManager inputManager;

    [Tooltip("速度・アンカー情報を受け取る PlayerController")]
    public PlayerController playerController;

    [Header("ワイヤーエフェクト（ホールド中）")]
    [Tooltip("ワイヤーの最小幅（脈動の底）")]
    public float wireMinWidth = 0.04f;

    [Tooltip("ワイヤーの最大幅（脈動の頂点）")]
    public float wireMaxWidth = 0.18f;

    [Tooltip("脈動の周波数（Hz：1秒に何回脈打つか）")]
    public float wirePulseFrequency = 3.5f;

    [Tooltip("ワイヤーの基本色（青系）")]
    public Color wireColor = new Color(0.2f, 0.6f, 1f, 1f);

    [Tooltip("HDR 発光強度（Bloom があるとグロウが発生する）")]
    public float wireGlowIntensity = 2.8f;

    [Header("ガス噴射エフェクト（スワイプ中）")]
    [Tooltip("1秒あたりのパーティクル数（スワイプ中のみ増加）")]
    public float gasEmissionRate = 90f;

    [Tooltip("ガスパーティクルの最大速度")]
    public float gasMaxSpeed = 7f;

    [Tooltip("ガスパーティクルの寿命（秒）")]
    public float gasLifetime = 0.22f;

    [Header("空間両断エフェクト（アンタップ 0.2秒後）")]
    [Tooltip("閃光の最大幅（大きいほど派手）")]
    public float slashMaxWidth = 0.5f;

    [Tooltip("閃光のフェードアウト時間（秒）")]
    public float slashFadeDuration = 0.3f;

    [Tooltip("軌跡上に配置する爆発点の数")]
    public int slashBurstPointCount = 6;

    [Tooltip("各爆発点から飛び散る火花の数")]
    public int sparksPerBurstPoint = 30;

    [Tooltip("アンタップから閃光が出るまでの「時間差」（秒）")]
    public float slashDelay = 0.2f;

    // ──────────────────────────────────────────────
    //  内部参照（Awake でコードから生成）
    // ──────────────────────────────────────────────

    private LineRenderer wireLineRenderer;

    private ParticleSystem gasParticleSystem;
    private ParticleSystemRenderer gasRenderer;

    // ワイヤーアクティブフラグと脈動の位相追跡
    private bool isWireActive;
    private float wireActivationTime;

    // ダッシュ開始時に記録された始点（斬撃の始点）
    private Vector2 dashOrigin;

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void Awake()
    {
        BuildWireRenderer();
        BuildGasParticleSystem();
    }

    private void OnEnable()
    {
        if (inputManager != null)
        {
            inputManager.OnTapBegan += HandleTapBegan;
            inputManager.OnTapEnded += HandleTapEnded;
        }
        if (playerController != null)
        {
            // OnDashStarted：(始点, 暫定終点) を受け取り斬撃コルーチン開始
            playerController.OnDashStarted += HandleDashStarted;
        }
    }

    private void OnDisable()
    {
        if (inputManager != null)
        {
            inputManager.OnTapBegan -= HandleTapBegan;
            inputManager.OnTapEnded -= HandleTapEnded;
        }
        if (playerController != null)
        {
            playerController.OnDashStarted -= HandleDashStarted;
        }
    }

    // ──────────────────────────────────────────────
    //  メインループ（ワイヤー & ガスを毎フレーム更新）
    // ──────────────────────────────────────────────

    private void Update()
    {
        if (isWireActive)
        {
            UpdateWirePositions();
            UpdateWirePulse();
        }

        UpdateGasEmission();
    }

    // ──────────────────────────────────────────────
    //  ① ワイヤーエフェクト
    // ──────────────────────────────────────────────

    /// <summary>ワイヤー描画に使う LineRenderer をコードで生成・設定する</summary>
    private void BuildWireRenderer()
    {
        // ワイヤー専用の子 GameObject を作成（プレイヤーと分離することでZ座標を制御しやすい）
        GameObject wireObj = new GameObject("WireEffect");
        wireObj.transform.SetParent(transform, false);

        wireLineRenderer = wireObj.AddComponent<LineRenderer>();
        wireLineRenderer.positionCount = 2;
        wireLineRenderer.useWorldSpace = true;
        wireLineRenderer.numCapVertices = 5;        // 端点を丸くする
        wireLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        wireLineRenderer.sortingOrder = 8;
        wireLineRenderer.enabled = false;           // 初期は非表示
    }

    /// <summary>タップ開始：ワイヤーを表示してアクティブにする</summary>
    private void HandleTapBegan()
    {
        if (playerController == null || playerController.NearestAnchor == null) return;

        isWireActive       = true;
        wireActivationTime = Time.unscaledTime;     // 脈動の位相をリセット
        wireLineRenderer.enabled = true;
    }

    /// <summary>アンタップ：ワイヤーを非表示にする</summary>
    private void HandleTapEnded()
    {
        isWireActive = false;
        wireLineRenderer.enabled = false;
    }

    /// <summary>LineRenderer の両端点をプレイヤーとアンカーの位置に毎フレーム合わせる</summary>
    private void UpdateWirePositions()
    {
        if (playerController == null || playerController.NearestAnchor == null) return;

        Vector3 playerPos = new Vector3(
            playerController.transform.position.x,
            playerController.transform.position.y,
            0f
        );
        Vector3 anchorPos = new Vector3(
            playerController.NearestAnchor.position.x,
            playerController.NearestAnchor.position.y,
            0f
        );

        wireLineRenderer.SetPosition(0, playerPos);
        wireLineRenderer.SetPosition(1, anchorPos);
    }

    /// <summary>
    /// Sin 波でワイヤーの幅と発光色を脈動させ「生命感のある光」を表現する。
    ///
    /// sinValue: -1〜1 → normalizedPulse: 0〜1 に変換
    /// wireWidth: Lerp(minWidth, maxWidth, normalizedPulse)
    /// glowColor: wireColor × Lerp(低輝度, 高輝度, normalizedPulse)
    /// </summary>
    private void UpdateWirePulse()
    {
        float elapsed         = Time.unscaledTime - wireActivationTime;
        float sinValue        = Mathf.Sin(elapsed * wirePulseFrequency * Mathf.PI * 2f);
        float normalizedPulse = (sinValue + 1f) * 0.5f;  // -1〜1 を 0〜1 に正規化

        // 幅を脈動
        float width = Mathf.Lerp(wireMinWidth, wireMaxWidth, normalizedPulse);
        wireLineRenderer.startWidth = width;
        wireLineRenderer.endWidth   = width * 0.4f;  // 末端を細くしてテーパー状に

        // 発光色を脈動（HDR 輝度倍率で明滅させる）
        float glow  = Mathf.Lerp(wireGlowIntensity * 0.6f, wireGlowIntensity, normalizedPulse);
        Color glowC = wireColor * glow;
        wireLineRenderer.startColor = glowC;
        wireLineRenderer.endColor   = new Color(glowC.r, glowC.g, glowC.b, glowC.a * 0.4f);
    }

    // ──────────────────────────────────────────────
    //  ② ガス噴射エフェクト
    // ──────────────────────────────────────────────

    /// <summary>ガス ParticleSystem を子 GameObject に生成する</summary>
    private void BuildGasParticleSystem()
    {
        GameObject gasObj = new GameObject("GasEffect");
        gasObj.transform.SetParent(transform, false);
        gasObj.transform.localPosition = Vector3.zero;

        gasParticleSystem = gasObj.AddComponent<ParticleSystem>();
        gasRenderer       = gasObj.GetComponent<ParticleSystemRenderer>();

        // ── MainModule ────────────────────────────────────────────
        var main = gasParticleSystem.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // ワールド空間で軌跡を残す
        main.startLifetime   = new ParticleSystem.MinMaxCurve(gasLifetime * 0.6f, gasLifetime);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(gasMaxSpeed * 0.4f, gasMaxSpeed);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.13f);
        main.startColor      = new Color(1f, 0.8f, 0.2f, 1f);         // 黄橙色
        main.gravityModifier = 0f;
        main.maxParticles    = 400;

        // ── EmissionModule：初期は 0（SetGasEmission で制御）────────
        var emission = gasParticleSystem.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        // ── ShapeModule：円錐形の先端から集中噴射 ────────────────
        var shape = gasParticleSystem.shape;
        shape.enabled    = true;
        shape.shapeType  = ParticleSystemShapeType.Cone;
        shape.angle      = 18f;
        shape.radius     = 0.04f;

        // ── ColorOverLifetime：黄白 → オレンジ → 赤 → 消える ────
        var col = gasParticleSystem.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1f, 0.96f, 0.7f), 0f),
                new GradientColorKey(new Color(1f, 0.5f,  0.1f), 0.5f),
                new GradientColorKey(new Color(0.8f, 0.1f, 0f),  1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f,   0f),
                new GradientAlphaKey(0.7f, 0.5f),
                new GradientAlphaKey(0f,   1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── SizeOverLifetime：生まれて大きく、消えて小さく ───────
        var sol = gasParticleSystem.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sc = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 0.05f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sc);

        // ── RendererModule：四角い火花 ──────────────────────────
        gasRenderer.renderMode  = ParticleSystemRenderMode.Mesh;
        gasRenderer.material    = new Material(Shader.Find("Sprites/Default"));
        gasRenderer.sortingOrder = 12;

        // Quad メッシュ取得（Unity 6 では Quad.fbx が廃止されたのでフォールバック）
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (quad == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        gasRenderer.mesh = quad;

        gasParticleSystem.Stop();
    }

    /// <summary>
    /// スワイプがある（SwipeDelta ≠ zero）かつガスがある間、ガスを噴射する。
    /// スワイプの逆方向に ParticleSystem ノズルを向けることでリアルな反作用感を出す。
    /// </summary>
    private void UpdateGasEmission()
    {
        if (inputManager == null || playerController == null) return;

        Vector2 swipeDelta = inputManager.SwipeDelta;
        bool shouldEmit    = inputManager.IsHolding
                          && swipeDelta.magnitude > 0.001f
                          && playerController.GasAmount > 0f;

        if (shouldEmit)
        {
            // ノズルの向き = スワイプ方向（ガスはスワイプと同じ方向に噴射される）
            float angle = Mathf.Atan2(swipeDelta.y, swipeDelta.x) * Mathf.Rad2Deg;
            gasParticleSystem.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // 噴射 ON
            if (!gasParticleSystem.isPlaying) gasParticleSystem.Play();
            var em = gasParticleSystem.emission;
            em.rateOverTime = gasEmissionRate;
        }
        else
        {
            // 噴射 OFF（既存パーティクルはフェードアウトさせる→Stopは呼ばない）
            var em = gasParticleSystem.emission;
            em.rateOverTime = 0f;
        }
    }

    // ──────────────────────────────────────────────
    //  ③ 時間差空間両断エフェクト
    // ──────────────────────────────────────────────

    /// <summary>ダッシュ開始イベントを受け取り、始点を記録してコルーチン開始</summary>
    private void HandleDashStarted(Vector2 origin, Vector2 _)
    {
        dashOrigin = origin;
        StartCoroutine(SlashEffectSequence());
    }

    /// <summary>
    /// 時間差空間両断のメインシーケンス：
    ///   1. slashDelay 秒（リアルタイム）待機
    ///   2. 現在位置を終点として決定
    ///   3. 閃光 LineRenderer を生成
    ///   4. 軌跡上に爆発パーティクルを配置
    ///   5. 閃光をフェードアウトして破棄
    /// </summary>
    private IEnumerator SlashEffectSequence()
    {
        // WaitForSecondsRealtime: timeScale に影響されない実時間待機
        yield return new WaitForSecondsRealtime(slashDelay);

        // ダッシュが始まってから 0.2 秒後の位置を「終点」として確定
        Vector2 dashEnd = (playerController != null)
            ? (Vector2)playerController.transform.position
            : dashOrigin + Vector2.right;

        // 軌跡が極端に短い場合はエフェクトをスキップ（無駄な演出を防ぐ）
        if ((dashEnd - dashOrigin).magnitude < 0.4f) yield break;

        // 閃光ラインを生成
        LineRenderer flash = CreateSlashFlash(dashOrigin, dashEnd);

        // 軌跡上の複数点に爆発パーティクルを生成
        SpawnBurstSparks(dashOrigin, dashEnd);

        // 閃光をアニメーションでフェードアウト
        yield return AnimateSlashFade(flash);

        // 使い終わった GameObject を破棄（ヒエラルキーを汚さない）
        if (flash != null) Destroy(flash.gameObject);
    }

    /// <summary>
    /// 斬撃の閃光ラインを生成する。
    /// HDR 白色で描き、sortingOrder を最前面（20）に設定。
    /// </summary>
    private LineRenderer CreateSlashFlash(Vector2 start, Vector2 end)
    {
        GameObject obj = new GameObject("SlashFlash");
        LineRenderer lr = obj.AddComponent<LineRenderer>();

        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, new Vector3(start.x, start.y, 0f));
        lr.SetPosition(1, new Vector3(end.x,   end.y,   0f));
        lr.numCapVertices = 8;
        lr.startWidth     = slashMaxWidth;
        lr.endWidth       = slashMaxWidth;

        // HDR 白（Bloom があると本物の光彩になる）
        Color hdrWhite = Color.white * 4.5f;
        lr.startColor = hdrWhite;
        lr.endColor   = hdrWhite;

        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.sortingOrder  = 20;

        return lr;
    }

    /// <summary>
    /// 軌跡の始点〜終点を等分割し、各点に爆発 ParticleSystem を生成する。
    /// Lerp(start, end, t) で等間隔の座標を取り出す。
    /// </summary>
    private void SpawnBurstSparks(Vector2 start, Vector2 end)
    {
        for (int i = 0; i < slashBurstPointCount; i++)
        {
            // t: 0.0〜1.0 を等分割（始点・終点も含む）
            float t = (slashBurstPointCount <= 1)
                ? 0.5f
                : (float)i / (slashBurstPointCount - 1);

            Vector2 burstPos = Vector2.Lerp(start, end, t);
            SpawnOneBurst(burstPos);
        }
    }

    /// <summary>
    /// 1点に全方向爆発 ParticleSystem を生成して即 Play する。
    /// EmissionModule の Burst 設定で「一瞬に全数放出」を実現する。
    /// </summary>
    private void SpawnOneBurst(Vector2 pos)
    {
        GameObject obj = new GameObject("BurstSpark");
        obj.transform.position = new Vector3(pos.x, pos.y, 0f);

        ParticleSystem ps       = obj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer pr = obj.GetComponent<ParticleSystemRenderer>();

        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.11f);
        main.startColor      = new Color(1f, 0.92f, 0.6f, 1f);
        main.gravityModifier = 0f;
        main.maxParticles    = 200;

        // Burst：time=0 の瞬間に sparksPerBurstPoint 個を一気に放出
        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, sparksPerBurstPoint)
        });

        // 全方向に爆発する円形シェイプ
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.08f;

        // 色：白黄 → オレンジ → 赤 → 透明
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f),
                new GradientColorKey(new Color(1f, 0.5f,  0.1f), 0.55f),
                new GradientColorKey(new Color(0.8f, 0.2f, 0f),  1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f,  0f),
                new GradientAlphaKey(0.6f, 0.6f),
                new GradientAlphaKey(0f,  1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Renderer：四角い火花
        pr.renderMode    = ParticleSystemRenderMode.Mesh;
        pr.material      = new Material(Shader.Find("Sprites/Default"));
        pr.sortingOrder  = 16;

        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (quad == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        pr.mesh = quad;

        ps.Play();

        // 寿命 + 余裕 0.5 秒後に自動破棄（ヒエラルキーを汚さない）
        Destroy(obj, 0.45f + 0.5f);
    }

    /// <summary>
    /// 閃光ラインの幅を「鋭いピーク → 急速消滅」のカーブでアニメーションする。
    ///
    /// AnimationCurve:
    ///   t=0.0 → 幅 0（始まり）
    ///   t=0.1 → 幅 1.0（一瞬で最大：衝撃の瞬間）
    ///   t=1.0 → 幅 0（完全消滅）
    /// この「鋭いピーク」があることで剣技の「切れ味」が視覚化される。
    /// </summary>
    private IEnumerator AnimateSlashFade(LineRenderer flash)
    {
        if (flash == null) yield break;

        // 鋭いピークカーブを定義
        AnimationCurve widthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);
        widthCurve.AddKey(new Keyframe(0.08f, 1f));  // t=0.08 で最大幅（鋭い立ち上がり）

        float elapsed = 0f;

        while (elapsed < slashFadeDuration)
        {
            float progress = elapsed / slashFadeDuration;
            float widthMul = widthCurve.Evaluate(progress);
            float w        = slashMaxWidth * widthMul;

            if (flash != null)
            {
                flash.startWidth = w;
                flash.endWidth   = w * 0.5f;

                // アルファもフェードアウト
                float alpha = Mathf.Lerp(1f, 0f, progress);
                Color c     = Color.white * (4.5f * alpha);
                flash.startColor = c;
                flash.endColor   = c;
            }

            // unscaledDeltaTime: timeScale 影響を受けずリアルタイムで消える
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 確実にゼロ幅で終了
        if (flash != null)
        {
            flash.startWidth = 0f;
            flash.endWidth   = 0f;
        }
    }
}
