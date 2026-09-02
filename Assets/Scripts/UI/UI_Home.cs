using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 极简 Home：列表展示关卡，点击进入。无真实地图 Prefab 也能跑逻辑。
/// </summary>
public class UI_Home : MonoBehaviour
{
    public Text infoText;
    public Text levelListText;   // 纯文本列表示例
    public Button btnLevel1;
    public Button btnLevel2;
    public Button btnLevel3;
    public Button btnRefresh;
    public InputField inputLevelId;
    public Button btnEnterLevel;

    void OnEnable()
    {
        Wire(btnLevel1, 1);
        Wire(btnLevel2, 2);
        Wire(btnLevel3, 3);

        if (btnEnterLevel != null)
        {
            btnEnterLevel.onClick.RemoveAllListeners();
            btnEnterLevel.onClick.AddListener(() =>
            {
                int id = 1;
                if (inputLevelId != null) int.TryParse(inputLevelId.text, out id);
                EnterLevel(id);
            });
        }

        if (btnRefresh != null)
        {
            btnRefresh.onClick.RemoveAllListeners();
            btnRefresh.onClick.AddListener(Refresh);
        }

        if (AppFlowController.Instance != null)
        {
            AppFlowController.Instance.OnStateChanged += OnFlowChanged;
            if (AppFlowController.Instance.State == AppState.Home)
                Refresh();
        }
    }

    void OnDisable()
    {
        if (AppFlowController.Instance != null)
            AppFlowController.Instance.OnStateChanged -= OnFlowChanged;
    }

    void OnFlowChanged(AppState prev, AppState next)
    {
        if (next == AppState.Home) Refresh();
    }

    void Wire(Button btn, int levelId)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => EnterLevel(levelId));
    }

    public void Refresh()
    {
        var flow = AppFlowController.Instance;
        if (flow == null) return;

        var nick = flow.Player.Profile?.nickname ?? "-";
        var lv = flow.Player.Profile?.level ?? 0;
        if (infoText != null)
            infoText.text = $"玩家: {nick}  Lv.{lv}  已解锁: {flow.Progress.MaxUnlocked}";

        if (levelListText != null)
        {
            var sb = new StringBuilder();
            foreach (var def in LevelCatalog.All)
            {
                var unlocked = flow.Progress.IsUnlocked(def.id);
                var stars = flow.Progress.GetStars(def.id);
                sb.AppendLine($"[{(unlocked ? "开" : "锁")}] {def.id} {def.name}  ★{stars}  Ch.{def.chapter}");
            }
            levelListText.text = sb.ToString();
        }
    }

    public void EnterLevel(int levelId)
    {
        var flow = AppFlowController.Instance;
        if (flow == null) return;

        if (!flow.Progress.IsUnlocked(levelId))
        {
            Debug.LogWarning($"关卡 {levelId} 未解锁");
            return;
        }

        flow.Gameplay.StartLevel(levelId);
        flow.Goto(AppState.Game);
    }
}
