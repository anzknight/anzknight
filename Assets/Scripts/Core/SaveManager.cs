using UnityEngine;

/// <summary>
/// プレイヤーの中断状態を軽量に永続化する。
/// PlayerPrefs + JsonUtility でローカル保存し、起動時に復元する。
/// </summary>
public class SaveManager : MonoBehaviour
{
    private const string SaveKey = "ArenaSaveData";

    [Header("保存対象の参照")]
    [SerializeField] private Transform playerTransform;

    [Header("セーブ対象の値")]
    [SerializeField] private float currentGas = 100f;
    [SerializeField] private int currentCoins = 0;
    [SerializeField] private int currentBossStage = 1;

    private void Awake()
    {
        LoadState();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) SaveState();
    }

    private void OnApplicationQuit()
    {
        SaveState();
    }

    public void UpdateGameState(float gas, int coins, int bossStage)
    {
        currentGas = gas;
        currentCoins = coins;
        currentBossStage = bossStage;
    }

    public void SaveState()
    {
        if (playerTransform == null) return;

        SaveData data = new SaveData
        {
            position = playerTransform.position,
            gas = currentGas,
            coins = currentCoins,
            bossStage = currentBossStage
        };

        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(SaveKey, json);
        PlayerPrefs.Save();
        Debug.Log("[SaveManager] セーブ完了: " + json);
    }

    public bool LoadState()
    {
        string json = PlayerPrefs.GetString(SaveKey, string.Empty);
        bool hasData = string.IsNullOrEmpty(json) == false;
        if (!hasData || playerTransform == null) return false;

        SaveData data = JsonUtility.FromJson<SaveData>(json);
        playerTransform.position = data.position;
        currentGas = data.gas;
        currentCoins = data.coins;
        currentBossStage = data.bossStage;
        Debug.Log("[SaveManager] ロード完了: " + json);
        return true;
    }

    [System.Serializable]
    private struct SaveData
    {
        public Vector3 position;
        public float gas;
        public int coins;
        public int bossStage;
    }
}
