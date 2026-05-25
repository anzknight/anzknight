// ============================================================
// [WaveManager.cs]
// 異次元立体機動アリーナ - ウェーブ管理（敵の波状攻撃システム）
// ============================================================
//
// 【設計メモ：if文なしのウェーブ進行】
//
//   ウェーブの状態は「生存敵数（aliveCount）」だけで管理する。
//   aliveCount が 0 になった瞬間に次ウェーブのカウントダウンを開始し、
//   カウントダウンが終わったら SpawnWave() を呼ぶだけ。
//
//   EnemyController.OnEnemyDied（static event）を購読することで、
//   WaveManager 自身は敵の死亡を polling せずイベント駆動で検知できる。
//
// 【ウェーブスケーリング（Lerp による自然な増加）】
//
//   ウェーブ番号に応じて敵の数・速度・HPを増加させる。
//   Mathf.Lerp(baseValue, maxValue, scaleFactor) を使い、
//   序盤はゆるやかに、終盤は急激に難しくなる「指数的カーブ」を実現する。
//
// 【スポーンエントランス演出】
//
//   敵が突然出現するのでなく、画面外から高速で飛び込んでくる。
//   スポーン位置をカメラ外（半径 15f）に設定し、
//   EnemyController の moveSpeed を一時的に 3 倍にして「飛来」させる。
//
// ============================================================

using System.Collections;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector 設定値
    // ──────────────────────────────────────────────

    [Header("ウェーブ設定")]
    [Tooltip("ウェーブ1の基本敵数")]
    public int baseEnemyCount = 3;

    [Tooltip("ウェーブごとに増加する敵数")]
    public int enemyCountIncrement = 2;

    [Tooltip("ウェーブ上限敵数")]
    public int maxEnemyCount = 15;

    [Tooltip("次ウェーブ開始までの待機時間（秒）")]
    public float waveCooldown = 3f;

    [Header("難易度スケーリング")]
    [Tooltip("ウェーブ1の敵移動速度")]
    public float baseEnemySpeed = 3.5f;

    [Tooltip("最大ウェーブ時の敵移動速度")]
    public float maxEnemySpeed = 7f;

    [Tooltip("難易度が最大に達するウェーブ番号")]
    public int difficultyMaxWave = 10;

    [Header("スポーン位置")]
    [Tooltip("スポーン円の半径（カメラ外）")]
    public float spawnRadius = 14f;

    // ──────────────────────────────────────────────
    //  プレイヤー参照（ArenaBootstrapper がセットする）
    // ──────────────────────────────────────────────

    /// <summary>敵の追跡目標となるプレイヤーの Transform</summary>
    public Transform playerTransform;

    // ──────────────────────────────────────────────
    //  公開プロパティ（ArenaUI が参照する）
    // ──────────────────────────────────────────────

    /// <summary>現在のウェーブ番号（1 始まり）</summary>
    public int CurrentWave { get; private set; } = 0;

    /// <summary>現在の生存敵数</summary>
    public int AliveEnemyCount { get; private set; } = 0;

    /// <summary>次ウェーブまでのカウントダウン（秒）</summary>
    public float WaveCooldownRemaining { get; private set; } = 0f;

    // ──────────────────────────────────────────────
    //  内部状態
    // ──────────────────────────────────────────────

    // ウェーブ間のクールダウン中かどうか
    private bool isWaitingForNextWave;

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void OnEnable()
    {
        EnemyController.OnEnemyDied += HandleEnemyDied;
    }

    private void OnDisable()
    {
        EnemyController.OnEnemyDied -= HandleEnemyDied;
    }

    private void Start()
    {
        // 最初のウェーブを即座に開始
        StartCoroutine(StartNextWave());
    }

    // ──────────────────────────────────────────────
    //  メインループ
    // ──────────────────────────────────────────────

    private void Update()
    {
        // カウントダウン残り時間を毎フレーム減算（UI 表示用）
        WaveCooldownRemaining = Mathf.Max(0f, WaveCooldownRemaining - Time.deltaTime);
    }

    // ──────────────────────────────────────────────
    //  敵撃破イベント受信
    // ──────────────────────────────────────────────

    /// <summary>
    /// 敵が1体死ぬたびに呼ばれる。
    /// 生存数が 0 になったら次ウェーブのカウントダウンを開始する。
    /// </summary>
    private void HandleEnemyDied(int coins)
    {
        AliveEnemyCount = Mathf.Max(0, AliveEnemyCount - 1);

        // 全滅 + クールダウン未実行のときだけ次ウェーブへ
        if (AliveEnemyCount == 0 && !isWaitingForNextWave)
            StartCoroutine(StartNextWave());
    }

    // ──────────────────────────────────────────────
    //  ウェーブ開始コルーチン
    // ──────────────────────────────────────────────

    /// <summary>
    /// waveCooldown 秒のカウントダウン後に次ウェーブをスポーンする。
    /// 最初のウェーブはカウントダウンをスキップする。
    /// </summary>
    private IEnumerator StartNextWave()
    {
        isWaitingForNextWave = true;

        // 最初のウェーブは即時スポーン、2ウェーブ目以降はクールダウン
        if (CurrentWave > 0)
        {
            WaveCooldownRemaining = waveCooldown;
            yield return new WaitForSeconds(waveCooldown);
        }

        CurrentWave++;
        SpawnWave(CurrentWave);

        isWaitingForNextWave = false;
    }

    // ──────────────────────────────────────────────
    //  ウェーブスポーン
    // ──────────────────────────────────────────────

    /// <summary>
    /// ウェーブ番号に応じた数・速度の敵を円周上にスポーンする。
    /// 難易度は Lerp で滑らかに増加する。
    /// </summary>
    private void SpawnWave(int wave)
    {
        // 難易度係数（0.0 = ウェーブ1、1.0 = difficultyMaxWave 以降）
        float difficulty = Mathf.Clamp01((float)(wave - 1) / difficultyMaxWave);

        // 敵の数（Lerp で自然な増加）
        int count = Mathf.RoundToInt(
            Mathf.Lerp(baseEnemyCount, maxEnemyCount, difficulty)
        ) + (wave - 1) * enemyCountIncrement;
        count = Mathf.Clamp(count, baseEnemyCount, maxEnemyCount);

        // 敵の速度（Lerp で段階的に強化）
        float speed = Mathf.Lerp(baseEnemySpeed, maxEnemySpeed, difficulty);

        // HP（ウェーブ5毎に1増加、最大5）
        int hp = Mathf.Clamp(1 + (wave - 1) / 5, 1, 5);

        AliveEnemyCount = count;

        for (int i = 0; i < count; i++)
        {
            // 円周上に等間隔 + わずかなランダムオフセットでスポーン
            float angle   = (360f / count) * i * Mathf.Deg2Rad;
            float jitter  = Random.Range(-0.3f, 0.3f);   // ばらつきを少し加える
            Vector3 spawnPos = new Vector3(
                Mathf.Cos(angle + jitter) * spawnRadius,
                Mathf.Sin(angle + jitter) * spawnRadius,
                0f
            );

            // プレイヤーの現在位置を中心にスポーン
            if (playerTransform != null)
                spawnPos += playerTransform.position;

            GameObject enemyObj = new GameObject("Enemy_W" + wave + "_" + i);
            enemyObj.transform.position = spawnPos;

            EnemyController ec = enemyObj.AddComponent<EnemyController>();
            ec.playerTransform = playerTransform;
            ec.moveSpeed       = speed;
            ec.maxHP           = hp;

            // コイン獲得量はウェーブが進むほど増やす
            ec.coinValue = 5 + (wave - 1) * 2;
        }
    }
}
