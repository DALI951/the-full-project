using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerListUI : MonoBehaviour
{
    public static PlayerListUI Instance { get; private set; }

    [SerializeField] private Transform entryContainer;
    [SerializeField] private GameObject entryPrefab;
    [SerializeField] private float entrySpacing = 22f;
    [SerializeField] private Color allyColor = Color.green;
    [SerializeField] private Color enemyColor = Color.red;

    private readonly Dictionary<int, GameObject> entries = new Dictionary<int, GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureLayout();
    }

    private void EnsureLayout()
    {
        if (entryContainer == null) return;
        VerticalLayoutGroup vlg = entryContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = entryContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperRight;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.spacing = entrySpacing;
        vlg.padding = new RectOffset(4, 4, 4, 4);

        ContentSizeFitter csf = entryContainer.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = entryContainer.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void SyncPlayers(string[] names, int[] indices, int[] teams)
    {
        foreach (var kv in entries)
            if (kv.Value != null) Destroy(kv.Value);
        entries.Clear();

        for (int i = 0; i < names.Length && i < indices.Length && i < teams.Length; i++)
            AddOrUpdate(indices[i], names[i], teams[i]);
    }

    public void AddOrUpdate(int index, string name, int team = 0)
    {
        if (entries.TryGetValue(index, out GameObject existing))
        {
            TMP_Text text = existing.GetComponentInChildren<TMP_Text>();
            if (text) text.text = FormatEntry(index, name, team);
            return;
        }

        if (entryPrefab == null || entryContainer == null) return;
        GameObject go = Instantiate(entryPrefab, entryContainer);
        TMP_Text txt = go.GetComponentInChildren<TMP_Text>();
        if (txt)
        {
            txt.text = FormatEntry(index, name, team);
            bool isSelf = index == PlayerColorManager.LocalPlayerIndex;
            bool isAlly = team == GetLocalTeam();
            txt.color = isSelf ? Color.white : isAlly ? allyColor : enemyColor;
        }
        entries[index] = go;
    }

    private static string FormatEntry(int index, string name, int team)
    {
        return $"{name}  [T{team}]";
    }

    private static int GetLocalTeam()
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        return np != null ? np.teamIndex : 0;
    }

    public void RemovePlayer(int index)
    {
        if (entries.TryGetValue(index, out GameObject go))
        {
            if (go != null) Destroy(go);
            entries.Remove(index);
        }
    }

    public void Clear()
    {
        foreach (var kv in entries)
            if (kv.Value != null) Destroy(kv.Value);
        entries.Clear();
    }
}
