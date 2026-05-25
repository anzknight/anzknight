// ============================================================
// [SlashTrailEffect.cs]
// 2Dトップダウン型アクションゲーム - 時間差斬撃エフェクト
// ============================================================
// 【役割】
// プレイヤーがダッシュした軌跡（直線）を記憶し、
// 通過した約0.2秒後に「白い閃光ライン＋火花爆発」で
// 空間が切り裂かれたような時間差演出を再現する。
//
// 【エフェクトの演出フロー】
//   ① TopDownPlayer から TriggerSlashEffect(start, end) が呼ばれる
//   ② コルーチンが開始され、0.2秒間（リアルタイム）待機
//   ③ 待機後、LineRendererを「白く太い線」として一瞬表示
//   ④ 同時に、軌跡上の複数地点から火花パーティクルを爆発生成
//   ⑤ LineRendererの幅をLerpでゼロに縮小しながらフェードアウト
//   ⑥ すべてが消えたら生成したGameObjectを破棄してクリーンアップ
//
// 【LineRendererの幅アニメーション】
// 「斬撃の閃光」らしさは、幅が一瞬で最大になってから素早く細くなる
// 「鋭いフラッシュ」のカーブで表現する。AnimationCurveを使って
// 細かく制御することで、剣を振った瞬間の白い残像を再現できる。
//
// 【パーティクルの「爆発」はバースト設定で実現】
// 通常のEmission（連続放出）ではなく、EmissionModule の Burst を使うと
// 「特定のタイミングに一度に大量放出」ができる。
// これにより「バン！」という瞬間的な火花爆発が作れる。
// ============================================================

using System.Collections;
using UnityEngine;

public class SlashTrailEffect : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector設定値
    // ──────────────────────────────────────────────

    [Header("閃光ライン設定")]
    [Tooltip("閃光の最大幅（数値が大きいほど派手な斬撃になる）")]
    public float flashMaxWidth = 0.4f;

    [Tooltip("閃光が消えるまでの時間（秒）")]
    public float flashDuration = 0.35f;

    [Tooltip("閃光の発光色（HDR白）")]
    public Color flashColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("閃光の発光強度（Bloomがあると光彩になる）")]
    public float flashGlowIntensity = 4f;

    [Header("火花パーティクル設定")]
    [Tooltip("軌跡に沿って配置する火花の発生点の数")]
    public int sparkBurstPointCount = 5;

    [Tooltip("各発生点から爆発する火花の数")]
    public int sparksPerBurstPoint = 25;

    [Tooltip("火花の飛び散る速度（最大）")]
    public float sparkMaxSpeed = 8f;

    [Tooltip("火花の持続時間（秒）")]
    public float sparkLifetime = 0.4f;

    [Tooltip("時間差の待機時間（秒）：これが「時間差演出」の核心")]
    public float delayBeforeFlash = 0.2f;

    // ──────────────────────────────────────────────
    //  パブリックAPI
    // ──────────────────────────────────────────────

    /// <summary>
    /// 斬撃エフェクトを発火する。
    /// TopDownPlayer の TriggerSlashAfterDash コルーチンから呼ばれる。
    /// </summary>
    public void TriggerSlashEffect(Vector2 slashStart, Vector2 slashEnd)
    {
        // 軌跡の長さが極端に短い場合はエフェクトをスキップ
        if ((slashEnd - slashStart).magnitude < 0.3f)
            return;

        // コルーチンで「時間差演出」を開始
        StartCoroutine(SlashEffectSequence(slashStart, slashEnd));
    }

    // ──────────────────────────────────────────────
    //  斬撃エフェクトのメインシーケンス（コルーチン）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 時間差斬撃エフェクトの全シーケンスを制御するコルーチン。
    ///
    /// 【WaitForSecondsRealtime を使う理由】
    /// WaitForSeconds は Time.timeScale の影響を受ける。
    /// アンタップ後は timeScale が 1.0 に戻る途中なので、
    /// WaitForSecondsRealtime を使って実時間で0.2秒を正確に計測する。
    /// </summary>
    private IEnumerator SlashEffectSequence(Vector2 slashStart, Vector2 slashEnd)
    {
        // ── ① 時間差待機：「まだ何も起きていない」ように見せる ──
        yield return new WaitForSecondsRealtime(delayBeforeFlash);

        // ── ② 閃光LineRendererを生成・設定 ──
        LineRenderer flashLine = CreateFlashLineRenderer(slashStart, slashEnd);

        // ── ③ 軌跡上の各点に火花パーティクルを爆発生成 ──
        SpawnBurstSparksAlongTrail(slashStart, slashEnd);

        // ── ④ 閃光を最大幅から徐々に細く縮小してフェードアウト ──
        yield return AnimateFlashFadeOut(flashLine);

        // ── ⑤ LineRendererのGameObjectを破棄（クリーンアップ） ──
        if (flashLine != null)
            Destroy(flashLine.gameObject);
    }

    // ──────────────────────────────────────────────
    //  閃光LineRendererの生成
    // ──────────────────────────────────────────────

    /// <summary>
    /// 斬撃軌跡に沿った白い閃光ラインをLineRendererで生成する。
    /// </summary>
    private LineRenderer CreateFlashLineRenderer(Vector2 start, Vector2 end)
    {
        // ランタイムにGameObjectを生成（既存のヒエラルキーを汚さない）
        GameObject flashObject = new GameObject("SlashFlashLine");
        flashObject.transform.position = Vector3.zero;

        LineRenderer lr = flashObject.AddComponent<LineRenderer>();

        // ワールド空間で2点を結ぶ
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, new Vector3(start.x, start.y, 0f));
        lr.SetPosition(1, new Vector3(end.x, end.y, 0f));

        // 線の先端を丸くしてなめらかに見せる
        lr.numCapVertices = 8;

        // 初期幅（最大値）をセット
        lr.startWidth = flashMaxWidth;
        lr.endWidth = flashMaxWidth;

        // HDRカラーで白い発光色を設定
        Color glowWhite = flashColor * flashGlowIntensity;
        lr.startColor = glowWhite;
        lr.endColor = glowWhite;

        // マテリアル（Sprites/Defaultで加算ブレンドに近い見た目）
        Material flashMat = new Material(Shader.Find("Sprites/Default"));
        lr.material = flashMat;
        lr.sortingOrder = 20; // 最前面に描画

        return lr;
    }

    // ──────────────────────────────────────────────
    //  火花パーティクルの爆発生成
    // ──────────────────────────────────────────────

    /// <summary>
    /// 軌跡の直線上を等間隔に分割し、各点にバースト（一斉放出）パーティクルを生成する。
    ///
    /// 【軌跡上の点の計算】
    /// Vector2.Lerp(start, end, t) で t=0.0〜1.0 の値を変えると
    /// 直線上のどこでも点を取れる。t=0 が始点、t=1 が終点、t=0.5 が中点。
    /// </summary>
    private void SpawnBurstSparksAlongTrail(Vector2 trailStart, Vector2 trailEnd)
    {
        for (int i = 0; i < sparkBurstPointCount; i++)
        {
            // 等間隔に分割した軌跡上の座標を計算
            // i=0 → 始点付近、i=max-1 → 終点付近
            float t = (sparkBurstPointCount <= 1)
                ? 0.5f
                : (float)i / (sparkBurstPointCount - 1);

            Vector2 burstPosition = Vector2.Lerp(trailStart, trailEnd, t);

            // 各点に爆発パーティクルを生成
            SpawnSingleBurstParticle(burstPosition, trailStart, trailEnd);
        }
    }

    /// <summary>
    /// 指定した座標にバーストパーティクルシステムを1つ生成する。
    /// ParticleSystem.EmitParams の Burst を使って「一瞬で全部放出」する。
    /// </summary>
    private void SpawnSingleBurstParticle(Vector2 position, Vector2 trailStart, Vector2 trailEnd)
    {
        // ──  子GameObjectにParticleSystemを動的生成 ──
        GameObject burstObject = new GameObject("SlashBurstSpark");
        burstObject.transform.position = new Vector3(position.x, position.y, 0f);

        ParticleSystem burstPS = burstObject.AddComponent<ParticleSystem>();
        ParticleSystemRenderer burstRenderer = burstObject.GetComponent<ParticleSystemRenderer>();

        // ── MainModule ──
        var main = burstPS.main;
        main.loop = false;                        // ループなし（1回放出で終わり）
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(sparkLifetime * 0.5f, sparkLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(sparkMaxSpeed * 0.4f, sparkMaxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
        main.startColor = new Color(1f, 0.9f, 0.6f, 1f); // 明るい黄白色
        main.gravityModifier = 0f;
        main.maxParticles = 200;

        // ── EmissionModule：バースト設定 ──
        // 「Burst」とは特定の時刻に一度に大量放出する設定。
        // time=0 の瞬間に sparkCount 個のパーティクルを一気に放出する。
        var emission = burstPS.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f; // 通常の連続放出は0

        // バースト：time=0, count=sparksPerBurstPoint（1回限り）
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, sparksPerBurstPoint)
        });

        // ── ShapeModule：全方向に放射（爆発エフェクト） ──
        var shape = burstPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f; // 小さな円から全方向に爆発

        // ── ColorOverLifetime：誕生時は白黄、末期は消える ──
        var colorOverLifetime = burstPS.colorOverLifetime;
        colorOverLifetime.enabled = true;

        Gradient gradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f),
            new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.6f),
            new GradientColorKey(new Color(0.8f, 0.2f, 0f), 1f),
        };
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(0.5f, 0.7f),
            new GradientAlphaKey(0f, 1f),
        };
        gradient.SetKeys(colorKeys, alphaKeys);
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // ── Renderer：四角い火花 ──
        burstRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        Mesh burstQuadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (burstQuadMesh == null)
        {
            GameObject tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            burstQuadMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempQuad);
        }
        burstRenderer.mesh = burstQuadMesh;
        burstRenderer.material = new Material(Shader.Find("Sprites/Default"));
        burstRenderer.sortingOrder = 15;

        // ── 再生開始 ──
        burstPS.Play();

        // ── 一定時間後にGameObjectを自動破棄（クリーンアップ） ──
        Destroy(burstObject, sparkLifetime + 0.5f);
    }

    // ──────────────────────────────────────────────
    //  閃光フェードアウトアニメーション
    // ──────────────────────────────────────────────

    /// <summary>
    /// 閃光ラインの幅を0に向けてアニメーションするコルーチン。
    ///
    /// 【AnimationCurveによる「鋭い閃光」の制御】
    /// 最初の20%で最大幅まで跳ね上がり（衝撃感）、
    /// 残り80%でゆっくり細くなる（余韻）カーブを使う。
    /// Evaluateで時間（0〜1）から値（0〜1）を取り出す。
    /// </summary>
    private IEnumerator AnimateFlashFadeOut(LineRenderer flashLine)
    {
        if (flashLine == null)
            yield break;

        // 「鋭い閃光」らしいカーブを定義
        // t=0.0 → 幅0（始まり）
        // t=0.1 → 幅1.0（最大：瞬間的に弾ける）
        // t=1.0 → 幅0（完全に消える）
        AnimationCurve widthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);
        widthCurve.AddKey(new Keyframe(0.1f, 1f)); // t=0.1で最大になる鋭いピーク

        float elapsed = 0f;
        float startAlpha = flashColor.a * flashGlowIntensity;

        while (elapsed < flashDuration)
        {
            // flashDuration に対する進捗（0〜1）
            float progress = elapsed / flashDuration;

            // AnimationCurveから現在の幅倍率を取得
            float widthMultiplier = widthCurve.Evaluate(progress);
            float currentWidth = flashMaxWidth * widthMultiplier;

            if (flashLine != null)
            {
                flashLine.startWidth = currentWidth;
                flashLine.endWidth = currentWidth * 0.6f;

                // 色の透明度も同時にフェードアウト
                float alpha = Mathf.Lerp(startAlpha, 0f, progress);
                Color currentColor = flashColor * alpha;
                flashLine.startColor = currentColor;
                flashLine.endColor = currentColor;
            }

            // unscaledDeltaTimeを使ってtimeScaleの影響を受けないようにする
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 確実に幅を0にして終了
        if (flashLine != null)
        {
            flashLine.startWidth = 0f;
            flashLine.endWidth = 0f;
        }
    }
}
