// ============================================================
// [GasThrustEffect.cs]
// 2Dトップダウン型アクションゲーム - ガス噴射エフェクト
// ============================================================
// 【役割】
// スワイプ中にガスを噴射している「火花」エフェクトを生成・制御する。
// AddComponent<ParticleSystem>() でコードだけでParticleSystemを完全構築し、
// 外部アセットや事前配置なしに動作する「完全プログラム完結型」エフェクト。
//
// 【ParticleSystemをコードで設定する仕組み】
// ParticleSystemの設定は「ModuleStructure」で管理される。
// main.startColor や emission.rateOverTime のように
// 各モジュールを変数に取ってからプロパティを変更する必要がある。
// （モジュールは値型なので、直接変更してから元の変数を「書き戻し」は不要。
//   しかし shape, colorOverLifetime などの一部は代入後に変更が反映されない
//   ケースがある点に注意 → emission/main モジュールで対処）
//
// 【パーティクルが「四角形」になる理由】
// ParticleSystemRenderer の renderMode を Mesh に変更し、
// PrimitiveType.Quad のメッシュを設定することで
// 丸い円ではなく「飛び散る四角い火花」のような見た目になる。
// ============================================================

using UnityEngine;

public class GasThrustEffect : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値
    // ──────────────────────────────────────────────

    [Header("噴射パーティクルの外観")]
    [Tooltip("1秒あたりに生成するパーティクル数（多いほどガスが濃くなる）")]
    public float emissionRate = 80f;

    [Tooltip("パーティクルの最小サイズ")]
    public float minParticleSize = 0.04f;

    [Tooltip("パーティクルの最大サイズ")]
    public float maxParticleSize = 0.14f;

    [Tooltip("噴射速度（最小）")]
    public float minParticleSpeed = 2f;

    [Tooltip("噴射速度（最大）")]
    public float maxParticleSpeed = 6f;

    [Tooltip("パーティクルの寿命（秒）：短いほどキビキビした火花らしさになる")]
    public float particleLifetime = 0.25f;

    [Tooltip("噴射の広がり角度（度）：小さいほど直線的、大きいほど広がる")]
    public float spreadAngle = 20f;

    [Header("色設定")]
    [Tooltip("火花の主色（明るいオレンジ）")]
    public Color sparkColorCore = new Color(1f, 0.85f, 0.2f, 1f);   // 明るい黄色

    [Tooltip("火花の外縁色（深みのあるオレンジ）")]
    public Color sparkColorEdge = new Color(1f, 0.35f, 0f, 0.6f);   // 燃えるオレンジ

    // ──────────────────────────────────────────────
    //  内部参照
    // ──────────────────────────────────────────────

    private ParticleSystem thrustParticleSystem;     // コードで生成するParticleSystem
    private ParticleSystemRenderer thrustRenderer;   // レンダリング設定用

    private bool isEmitting;                         // 現在Emission中かどうか

    // ──────────────────────────────────────────────
    //  初期化：ParticleSystemをコードで生成・設定
    // ──────────────────────────────────────────────

    private void Awake()
    {
        BuildThrustParticleSystem();
    }

    /// <summary>
    /// ParticleSystemを持つ子GameObjectをコードで生成し、
    /// 全パラメーターをスクリプトから設定する。
    ///
    /// 【なぜ子Objectに作るのか？】
    /// transform.rotation を変えて噴射方向を制御するため、
    /// プレイヤー本体とは別のTransformが必要になる。
    /// </summary>
    private void BuildThrustParticleSystem()
    {
        // ① 子GameObjectを作成
        GameObject psObject = new GameObject("GasThrustParticles");
        psObject.transform.SetParent(transform, false);
        psObject.transform.localPosition = Vector3.zero;

        // ② ParticleSystemコンポーネントを追加（AddComponentが生成の核心）
        thrustParticleSystem = psObject.AddComponent<ParticleSystem>();
        thrustRenderer = psObject.GetComponent<ParticleSystemRenderer>();

        // ③ 各モジュールをコードから設定
        ConfigureMainModule();
        ConfigureEmissionModule();
        ConfigureShapeModule();
        ConfigureColorOverLifetimeModule();
        ConfigureSizeOverLifetimeModule();
        ConfigureRendererModule();

        // ④ 初期状態では停止（Emission Off）
        thrustParticleSystem.Stop();
        SetEmissionRate(0f);
    }

    // ──────────────────────────────────────────────
    //  ParticleSystemモジュール設定
    // ──────────────────────────────────────────────

    /// <summary>
    /// MainModule：パーティクルの基本パラメーター（寿命、速度、サイズ、色）
    /// </summary>
    private void ConfigureMainModule()
    {
        var main = thrustParticleSystem.main;

        // ループ再生（Emission On中は常にパーティクルを出し続ける）
        main.loop = true;
        main.playOnAwake = false;

        // シミュレーション空間：ワールド空間で動く（プレイヤーが動いても軌跡が残る）
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // パーティクルの寿命：MinMaxCurveで最小〜最大のランダム値
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            particleLifetime * 0.7f,
            particleLifetime
        );

        // 噴射速度
        main.startSpeed = new ParticleSystem.MinMaxCurve(minParticleSpeed, maxParticleSpeed);

        // サイズ
        main.startSize = new ParticleSystem.MinMaxCurve(minParticleSize, maxParticleSize);

        // 初期色（ここではオレンジ系の中間色を設定、詳細はColorOverLifetimeで制御）
        main.startColor = sparkColorCore;

        // 重力なし（宇宙空間の噴射イメージ）
        main.gravityModifier = 0f;

        // 最大パーティクル数（これを超えると古いものが消える）
        main.maxParticles = 300;
    }

    /// <summary>
    /// EmissionModule：1秒あたりの生成数
    /// </summary>
    private void ConfigureEmissionModule()
    {
        var emission = thrustParticleSystem.emission;
        emission.enabled = true;

        // 初期は0（SetEmissionRateで制御）
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
    }

    /// <summary>
    /// ShapeModule：パーティクルが発生する「形状」
    /// Cone（円錐）を使い、先端から集中的に放射する
    /// </summary>
    private void ConfigureShapeModule()
    {
        var shape = thrustParticleSystem.shape;
        shape.enabled = true;

        // 円錐形の先端から放射（集中した噴射口のイメージ）
        shape.shapeType = ParticleSystemShapeType.Cone;

        // 広がり角度（小さいほど直線的な噴射）
        shape.angle = spreadAngle;

        // 発射口のサイズ（小さくすることでノズルからの噴射らしさが出る）
        shape.radius = 0.05f;
        shape.radiusThickness = 1f; // 全面から発生
    }

    /// <summary>
    /// ColorOverLifetimeModule：生まれてから消えるまでの色変化
    /// 誕生時：明るい黄白色 → 中間：オレンジ → 末期：透明な赤（消える）
    /// </summary>
    private void ConfigureColorOverLifetimeModule()
    {
        var colorOverLifetime = thrustParticleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;

        // GradientでLifetime全体の色変化を定義
        Gradient gradient = new Gradient();

        // GradientColorKey: 時刻0〜1での「色」の変化
        GradientColorKey[] colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),   // 誕生：明るい黄白
            new GradientColorKey(sparkColorCore,               0.3f), // 序盤：黄オレンジ
            new GradientColorKey(sparkColorEdge,               0.7f), // 終盤：深いオレンジ
            new GradientColorKey(new Color(0.8f, 0.1f, 0f),   1f),   // 末期：暗い赤
        };

        // GradientAlphaKey: 時刻0〜1での「不透明度」の変化
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[]
        {
            new GradientAlphaKey(1f,   0f),   // 誕生：完全不透明
            new GradientAlphaKey(0.8f, 0.5f), // 中間：少し透ける
            new GradientAlphaKey(0f,   1f),   // 末期：完全透明（フェードアウト）
        };

        gradient.SetKeys(colorKeys, alphaKeys);
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    /// <summary>
    /// SizeOverLifetimeModule：時間とともにサイズが小さくなる（消えていく演出）
    /// </summary>
    private void ConfigureSizeOverLifetimeModule()
    {
        var sizeOverLifetime = thrustParticleSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;

        // AnimationCurveで「大きく生まれ、小さく消える」曲線を定義
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);    // 誕生時：フルサイズ
        sizeCurve.AddKey(0.4f, 0.8f); // 40%地点：少し縮む
        sizeCurve.AddKey(1f, 0.1f);  // 末期：ほぼ消える

        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
    }

    /// <summary>
    /// RendererModule：パーティクルの見た目を「四角い火花」に設定する
    ///
    /// 【Meshモードについて】
    /// renderMode を Mesh にして Quad メッシュを使うと、
    /// 通常の丸いビルボードではなく「四角い板」としてレンダリングされる。
    /// これにより「機械的な火花」「金属が削れる粒子」のような見た目になる。
    /// </summary>
    private void ConfigureRendererModule()
    {
        // Meshモードで四角い火花に
        thrustRenderer.renderMode = ParticleSystemRenderMode.Mesh;

        // UnityのPrimitive Quadメッシュを使用（外部アセット不要）
        thrustRenderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        // 加算ブレンド：重なった部分が明るくなり、炎っぽい発光感が出る
        Material sparkMaterial = new Material(Shader.Find("Sprites/Default"));
        thrustRenderer.material = sparkMaterial;

        // レンダリングを半透明キューに（他のオブジェクトの上に描く）
        thrustRenderer.sortingOrder = 10;
    }

    // ──────────────────────────────────────────────
    //  パブリックAPI（TopDownPlayerから呼ばれる）
    // ──────────────────────────────────────────────

    /// <summary>
    /// ガスの噴射方向を設定する。
    /// ParticleSystemを持つ子Objectを回転させることで、
    /// コーン形の発射形状ごと向きを変える。
    /// </summary>
    public void SetThrustDirection(Vector2 direction)
    {
        if (direction.magnitude < 0.001f)
            return;

        // 2Dの方向ベクトルを角度に変換してZ軸回転で向きを変える
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        thrustParticleSystem.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// ガスの排出オン/オフを切り替える。
    /// EmissionRateを0にすることで「既存パーティクルを残しつつ新規生成だけ止める」
    /// </summary>
    public void SetEmissionActive(bool active)
    {
        if (isEmitting == active)
            return;

        isEmitting = active;

        if (active)
        {
            thrustParticleSystem.Play();
            SetEmissionRate(emissionRate);
        }
        else
        {
            SetEmissionRate(0f);
            // Stop()は呼ばない（既存パーティクルをフェードアウトさせるため）
        }
    }

    // ──────────────────────────────────────────────
    //  内部ヘルパー
    // ──────────────────────────────────────────────

    /// <summary>
    /// EmissionRateを変更する。モジュール変数のスコープ問題を回避するため
    /// 専用メソッドに切り出している（ParticleSystem.EmissionModuleの仕様）
    /// </summary>
    private void SetEmissionRate(float rate)
    {
        var emission = thrustParticleSystem.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
    }
}
