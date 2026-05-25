// ============================================================
// [EnemyController.cs]
// 異次元立体機動アリーナ - 敵AI（追跡型・斬撃で討伐）
// ============================================================
//
// 【設計メモ：if文なしの追跡移動】
//
//   敵の動きは「プレイヤーへの方向ベクトル × 速度」だけ。
//   currentVelocity = (playerPos - enemyPos).normalized × moveSpeed
//   これを毎フレーム Rigidbody2D に設定するだけで追跡が完成する。
//
//   Mathf.Lerp でなめらかに加速させることで、
//   「ヌルっと動き出す」本能的な危険感が生まれる。
//
// 【HP・ダメージの Lerp 表現】
//
//   ダメージを受けた瞬間スプライトを赤く光らせる演出は、
//   damageFlashTimer を 1.0 にセットして毎フレーム 0.0 へ Lerp する。
//   Color.Lerp(baseColor, flashColor, damageFlashTimer) で
//   if文なしに「ダメージ時は赤く、徐々に元の色に戻る」を表現できる。
//
// 【死亡時の演出】
//   Destroy(gameObject) の前にコインエフェクト（パーティクル）を生成し、
//   OnEnemyDied イベントで ArenaUI へ「コイン獲得」を通知する。
// ============================================================

using System;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector 設定値
    // ──────────────────────────────────────────────

    [Header("移動設定")]
    [Tooltip("プレイヤーを追跡する速度（units/秒）")]
    public float moveSpeed = 3.5f;

    [Tooltip("速度変化の Lerp 速度（大きいほど俊敏）")]
    public float accelerationLerp = 4f;

    [Header("HP設定")]
    [Tooltip("最大 HP（斬撃1回につき1ダメージ）")]
    public int maxHP = 3;

    [Header("コイン設定")]
    [Tooltip("撃破時に加算するコイン数")]
    public int coinValue = 5;

    [Header("外観")]
    [Tooltip("通常時の色")]
    public Color normalColor = new Color(0.9f, 0.2f, 0.25f, 1f);  // 赤

    [Tooltip("ダメージ時のフラッシュ色")]
    public Color damageFlashColor = new Color(1f, 1f, 0.8f, 1f);  // 白黄

    // ──────────────────────────────────────────────
    //  他コンポーネントへ公開するプロパティ・イベント
    // ──────────────────────────────────────────────

    /// <summary>撃破済みかどうか（重複ダメージを防ぐ）</summary>
    public bool IsDead { get; private set; }

    /// <summary>
    /// 敵が撃破された瞬間に発火するイベント。
    /// 引数は獲得コイン数。ArenaUI が購読してスコアに加算する。
    /// </summary>
    public static event Action<int> OnEnemyDied;

    // ──────────────────────────────────────────────
    //  内部状態変数
    // ──────────────────────────────────────────────

    // プレイヤーの Transform（ArenaBootstrapper がセットする）
    public Transform playerTransform;   // ArenaBootstrapper から public 代入

    private Rigidbody2D rb;
    private SpriteRenderer sr;

    // 現在の HP
    private int currentHP;

    // 現在の速度ベクトル（Lerp で加速）
    private Vector2 currentVelocity;

    // ダメージフラッシュの残り時間（1.0 → 0.0 へ毎フレーム減衰）
    private float damageFlashTimer;

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void Awake()
    {
        rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale   = 0f;
        rb.freezeRotation = true;

        // 敵の当たり判定（プレイヤーの斬撃ダメージ検出に使われる）
        CircleCollider2D col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = 0.4f;

        // プロシージャルな赤い円スプライトを生成
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite       = CreateEnemySprite();
        sr.sortingOrder = 4;
        transform.localScale = Vector3.one * 0.9f;

        currentHP = maxHP;
    }

    // ──────────────────────────────────────────────
    //  メインループ
    // ──────────────────────────────────────────────

    private void Update()
    {
        if (IsDead) return;

        ChasePlayer();
        UpdateDamageFlash();
        PulseScale();
    }

    // ──────────────────────────────────────────────
    //  プレイヤー追跡（if文なし）
    // ──────────────────────────────────────────────

    /// <summary>
    /// プレイヤーへの方向ベクトルを毎フレーム計算し、Lerp で速度を変化させる。
    /// playerTransform が null の場合は動かない（Lerp で自然に停止）。
    /// </summary>
    private void ChasePlayer()
    {
        // playerTransform が null でも toPlayer は Vector2.zero になるので
        // Lerp により自然に減速・停止する（if文なし）
        Vector2 toPlayer = playerTransform != null
            ? ((Vector2)playerTransform.position - (Vector2)transform.position).normalized
            : Vector2.zero;

        // 目標速度 = プレイヤー方向 × moveSpeed
        Vector2 targetVelocity = toPlayer * moveSpeed;

        // Lerp で加速：ヌルっとした「生き物っぽい」動きになる
        currentVelocity = Vector2.Lerp(currentVelocity, targetVelocity, accelerationLerp * Time.deltaTime);

        rb.linearVelocity = currentVelocity;
    }

    // ──────────────────────────────────────────────
    //  ダメージフラッシュ（Lerp で色変化）
    // ──────────────────────────────────────────────

    /// <summary>
    /// damageFlashTimer が 1.0 の時は白黄、0.0 の時は通常色になるよう Lerp する。
    /// TakeDamage() でタイマーを 1.0 にセットし、毎フレーム 0.0 に向けて減衰。
    /// </summary>
    private void UpdateDamageFlash()
    {
        // 毎フレーム減衰（Lerp の速さでフラッシュの持続感が変わる）
        damageFlashTimer = Mathf.Lerp(damageFlashTimer, 0f, 8f * Time.deltaTime);

        // タイマーに応じて色を補間（if文なし）
        sr.color = Color.Lerp(normalColor, damageFlashColor, damageFlashTimer);
    }

    // ──────────────────────────────────────────────
    //  ドキドキ脈動スケール（生命感の演出）
    // ──────────────────────────────────────────────

    /// <summary>
    /// Sin 波でスケールを微かに変動させ「呼吸している」ような生命感を出す。
    /// 速度が上がると脈動が激しくなる（追い詰められた感）。
    /// </summary>
    private void PulseScale()
    {
        // HP が少ないほど脈動が激しくなる（緊張感の増加）
        float hpRatio    = (float)currentHP / maxHP;             // 1.0 = 満タン, 0.0 = 瀕死
        float pulseSpeed = Mathf.Lerp(3f, 8f, 1f - hpRatio);    // 瀕死ほど速く脈打つ
        float pulseAmp   = Mathf.Lerp(0.02f, 0.08f, 1f - hpRatio); // 瀕死ほど大きく揺れる

        float pulse     = Mathf.Sin(Time.time * pulseSpeed) * pulseAmp;
        float baseScale = 0.9f + pulse;
        transform.localScale = Vector3.one * baseScale;
    }

    // ──────────────────────────────────────────────
    //  ダメージ受け（PlayerController から呼ばれる）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 斬撃ダメージを受ける。HP が 0 になったら Die() を呼ぶ。
    /// IsDead フラグで撃破済みの敵への二重ダメージを防ぐ。
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        currentHP -= amount;

        // ダメージフラッシュ開始（1.0 にセットすれば UpdateDamageFlash が処理する）
        damageFlashTimer = 1.0f;

        // HP が 0 以下になったら撃破
        if (currentHP <= 0)
            Die();
    }

    // ──────────────────────────────────────────────
    //  撃破処理
    // ──────────────────────────────────────────────

    /// <summary>
    /// 敵を撃破する。コインエフェクトを生成し、イベントで UI に通知してから破棄。
    /// </summary>
    private void Die()
    {
        IsDead = true;
        rb.linearVelocity = Vector2.zero;

        // 撃破エフェクト：その場で白い爆発パーティクルを生成
        SpawnDeathEffect();

        // ArenaUI にコイン獲得を通知（static イベント）
        OnEnemyDied?.Invoke(coinValue);

        // このゲームオブジェクトを破棄（0.1秒後にエフェクトが見えてから消す）
        Destroy(gameObject, 0.1f);
    }

    // ──────────────────────────────────────────────
    //  撃破時エフェクト（プログラム完結型）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 撃破時にその場で爆発パーティクルを生成する。
    /// 外部アセット不要・コードだけで表現。
    /// </summary>
    private void SpawnDeathEffect()
    {
        GameObject vfx = new GameObject("EnemyDeathVFX");
        vfx.transform.position = transform.position;

        ParticleSystem ps = vfx.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 10f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.4f, 0.1f, 1f),
            new Color(1f, 0.9f, 0.5f, 1f)
        );
        main.gravityModifier = 0f;
        main.maxParticles    = 60;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, 40)
        });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f),
                new GradientColorKey(new Color(0.9f, 0.3f, 0f), 0.6f),
                new GradientColorKey(new Color(0.5f, 0.1f, 0f), 1f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f,  0f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f,  1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Play();
        Destroy(vfx, 1.5f);
    }

    // ──────────────────────────────────────────────
    //  プロシージャルスプライト生成
    // ──────────────────────────────────────────────

    /// <summary>赤い丸い敵スプライトを Texture2D からプログラムで生成する</summary>
    private Sprite CreateEnemySprite()
    {
        int res    = 48;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float center = res * 0.5f;
        float radius = center - 1f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist  = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - (dist - (radius - 1.5f)));
                // 内側ほど明るい赤、外側ほど暗い赤のグラデーション
                float bright = Mathf.Clamp01(1f - dist / center);
                Color c = Color.Lerp(
                    new Color(0.5f, 0.05f, 0.05f),
                    new Color(1f,   0.2f,  0.2f),
                    bright
                );
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, alpha));
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
    }
}
