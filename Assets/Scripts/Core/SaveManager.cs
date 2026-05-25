// ============================================================
// [SaveManager.cs]
// 異次元立体機動アリーナ - 中断・続きからプレイ機能
// ============================================================
//
// 【設計メモ：なぜ PlayerPrefs + JsonUtility の組み合わせなのか】
//
//   PlayerPrefs は「キーと値のペア」でローカルに保存する Unity 標準機能。
//   int/float/string の3型しか扱えないが、JsonUtility.ToJson() を使えば
//   任意のクラスを JSON 文字列（string）に変換できるので、
//   PlayerPrefs の string 型スロットに丸ごと詰め込む戦略。
//
//   メリット：
//   ・外部ライブラリ不要（Unity 標準のみ）
//   ・iOS/Android/PC すべてで動作
//   ・セーブデータの構造変更が楽（SaveData クラスを追加するだけ）
//
//   デメリット：
//   ・PlayerPrefs はレジストリや plist ファイルに保存されるため
//     ユーザーが直接編集できる（チート対策が必要な場合はファイル暗号化が必要）
//
// 【シングルトンパターンについて】
//   SaveManager.Instance でどのスクリプトからもアクセス可能にする。
//   DontDestroyOnLoad でシーン遷移後もデータを保持する。
//
// 【自動セーブのタイミング】
//   OnApplicationPause(true) → スマホでホームボタンを押した瞬間
//   OnApplicationQuit        → PCでゲームを終了した瞬間
//   どちらでも自動的にセーブされる。
// ============================================================

using UnityEngine;

// ──────────────────────────────────────────────
//  セーブデータ構造体（JsonUtility で JSON 化するため [Serializable] 必須）
// ──────────────────────────────────────────────

[System.Serializable]
public class SaveData
{
    // プレイヤーの最後の位置（Vector3 は JsonUtility が直接扱えないため XYZ を分割）
    public float posX;
    public float posY;
    public float posZ;

    // 現在のガス残量（0.0〜1.0 の正規化値で保存。最大量は PlayerController 側で定義）
    public float gasAmount;

    // 所持コイン数
    public int coins;

    // 到達した最大ボスステージ番号（1〜N）
    public int bossStageNumber;

    // セーブデータが実際に存在するかのフラグ（初回起動判定用）
    public bool hasSaveData;

    // ── 便利プロパティ：Vector3 に変換して返す ──────────────────────
    /// <summary>保存された座標を Vector3 として返す</summary>
    public Vector3 Position => new Vector3(posX, posY, posZ);
}

// ──────────────────────────────────────────────
//  SaveManager 本体
// ──────────────────────────────────────────────

public class SaveManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  シングルトン
    // ──────────────────────────────────────────────

    /// <summary>どこからでも SaveManager.Instance でアクセスできる唯一のインスタンス</summary>
    public static SaveManager Instance { get; private set; }

    // ──────────────────────────────────────────────
    //  定数・プロパティ
    // ──────────────────────────────────────────────

    // PlayerPrefs に保存するキー名（ユニークな文字列であれば何でもよい）
    private const string SAVE_KEY = "ArenaGameSave_v1";

    /// <summary>現在メモリ上に読み込まれているセーブデータ</summary>
    public SaveData CurrentData { get; private set; }

    /// <summary>セーブデータが存在するかどうか（起動時のニューゲーム/続きから判定に使う）</summary>
    public bool HasSaveData => CurrentData != null && CurrentData.hasSaveData;

    // ──────────────────────────────────────────────
    //  初期化
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // シングルトン保証：2つ目のインスタンスが生成されたら即座に破棄
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // シーン遷移しても破棄されないようにする
        DontDestroyOnLoad(gameObject);

        // 起動時に既存のセーブデータをメモリに読み込む
        LoadGame();
    }

    // ──────────────────────────────────────────────
    //  セーブ
    // ──────────────────────────────────────────────

    /// <summary>
    /// 現在のゲーム状態を PlayerPrefs に保存する。
    /// PlayerController が OnApplicationPause/Quit で呼び出す。
    /// </summary>
    /// <param name="position">プレイヤーの現在座標</param>
    /// <param name="gas">現在のガス残量（0.0〜1.0）</param>
    /// <param name="coins">所持コイン数</param>
    /// <param name="bossStage">到達ボスステージ番号</param>
    public void SaveGame(Vector3 position, float gas, int coins, int bossStage)
    {
        // SaveData オブジェクトに詰める
        CurrentData = new SaveData
        {
            posX            = position.x,
            posY            = position.y,
            posZ            = position.z,
            gasAmount       = gas,
            coins           = coins,
            bossStageNumber = bossStage,
            hasSaveData     = true
        };

        // SaveData → JSON 文字列 → PlayerPrefs に保存
        string json = JsonUtility.ToJson(CurrentData);
        PlayerPrefs.SetString(SAVE_KEY, json);

        // PlayerPrefs.Save() を明示的に呼ぶことで即座にディスクに書き込む
        // （呼ばないとアプリ終了時にまとめて書き込まれるが、クラッシュで失われることがある）
        PlayerPrefs.Save();

        Debug.Log($"[SaveManager] セーブ完了 - 座標:{position} ガス:{gas:F2} コイン:{coins} ボス:{bossStage}");
    }

    // ──────────────────────────────────────────────
    //  ロード
    // ──────────────────────────────────────────────

    /// <summary>
    /// PlayerPrefs から JSON を読み込み、SaveData を復元する。
    /// Awake() で自動呼び出しされる。
    /// </summary>
    public void LoadGame()
    {
        if (PlayerPrefs.HasKey(SAVE_KEY))
        {
            // JSON 文字列 → SaveData オブジェクトに復元
            string json = PlayerPrefs.GetString(SAVE_KEY);
            CurrentData = JsonUtility.FromJson<SaveData>(json);
            Debug.Log($"[SaveManager] ロード完了 - 座標:{CurrentData.Position} ボス:{CurrentData.bossStageNumber}");
        }
        else
        {
            // 初回起動：空のセーブデータを生成
            CurrentData = new SaveData
            {
                posX            = 0f,
                posY            = 0f,
                posZ            = 0f,
                gasAmount       = 1f,   // ガス満タンから開始
                coins           = 0,
                bossStageNumber = 1,    // ステージ1から開始
                hasSaveData     = false
            };
            Debug.Log("[SaveManager] セーブデータなし - 新規ゲームとして開始");
        }
    }

    // ──────────────────────────────────────────────
    //  セーブ削除（タイトル画面の「最初から」ボタン用）
    // ──────────────────────────────────────────────

    /// <summary>セーブデータを完全に消去する</summary>
    public void DeleteSave()
    {
        PlayerPrefs.DeleteKey(SAVE_KEY);
        PlayerPrefs.Save();
        CurrentData = new SaveData { hasSaveData = false };
        Debug.Log("[SaveManager] セーブデータを削除しました");
    }

    // ──────────────────────────────────────────────
    //  自動セーブ（アプリのライフサイクルフック）
    // ──────────────────────────────────────────────

    /// <summary>
    /// スマホでホームボタンを押した / タスクスイッチした瞬間に呼ばれる。
    /// pause=true の時のみセーブする（resume 時は不要）。
    /// PlayerController から最新データを取得してセーブする。
    /// </summary>
    private void OnApplicationPause(bool pause)
    {
        if (pause) AutoSave();
    }

    /// <summary>PC でゲームウィンドウを閉じた瞬間に呼ばれる</summary>
    private void OnApplicationQuit()
    {
        AutoSave();
    }

    /// <summary>
    /// PlayerController から現在状態を取得して自動セーブする。
    /// PlayerController が存在しない場合は前回のデータをそのまま保持する。
    /// </summary>
    private void AutoSave()
    {
        // シーン内の PlayerController を探して現在状態を取得
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            SaveGame(
                player.transform.position,
                player.GasAmount,
                player.Coins,
                CurrentData?.bossStageNumber ?? 1
            );
        }
        else if (CurrentData != null && CurrentData.hasSaveData)
        {
            // プレイヤーが見つからなくても前回データを維持
            SaveGame(
                CurrentData.Position,
                CurrentData.gasAmount,
                CurrentData.coins,
                CurrentData.bossStageNumber
            );
        }
    }
}
