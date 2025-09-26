using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 把 Unity Console 訊息鏡射到 UI 的 Text (TMP) 上，方便在頭盔裡觀察。
/// - 監聽 Application.logMessageReceivedThreaded（支援多執行緒）
/// - 依等級加上前綴圖示與（可選）顏色
/// - 保留最近 N 行，避免爆記憶體
/// - 自動把 ScrollRect 捲到底（若存在）
/// - 也提供手動推送訊息的 API：ConsoleToUI.Log("...") / LogWarning / LogError
/// </summary>
public class ConsoleToUI : MonoBehaviour
{
    [Header("Target UI")]
    [Tooltip("指向你在 Canvas 裡的 Text (TMP)")]
    public TMP_Text consoleText;

    [Tooltip("可選：指向包住 Text 的 ScrollRect（會自動捲到底）")]
    public ScrollRect scrollRect;

    [Header("Display")]
    [Tooltip("最多顯示多少行（超過就丟掉最舊的）")]
    public int maxLines = 200;
    [Tooltip("是否顯示時間戳 (HH:mm:ss)")]
    public bool showTimestamp = true;
    [Tooltip("是否使用不同顏色顯示不同等級")]
    public bool colorizeByLevel = true;

    [Header("Performance & Limits")]
    [Tooltip("每幀最多從待處理佇列取出幾則訊息")]
    public int maxDequeuesPerFrame = 30;
    [Tooltip("單則訊息最大顯示字元數，超過會壓縮顯示（保留頭尾）")]
    public int maxCharsPerLine = 800;
    [Tooltip("整體最多顯示的總字元數（跨所有行），超過就移除最舊的行")]
    public int maxTotalChars = 20000;
    [Tooltip("把超長訊息切塊為多行（而不是壓縮），以便逐行閱讀")]
    public bool splitLongMessagesIntoChunks = false;
    [Tooltip("切塊大小（僅在 splitLongMessagesIntoChunks=true 時使用）")]
    public int chunkSize = 256;

    // 線程安全的 Queue -> 主執行緒拉出來顯示
    private readonly object _lock = new object();
    private readonly Queue<string> _pending = new Queue<string>();
    private readonly LinkedList<string> _lines = new LinkedList<string>();
    private StringBuilder _sb = new StringBuilder(4096);
    private int _currentTotalChars = 0;

    // 靜態入口（讓別的腳本可以 ConsoleToUI.Log(...)）
    public static ConsoleToUI Instance { get; private set; }
    public static void Log(string msg) => Instance?.Enqueue(msg, LogType.Log);
    public static void LogWarning(string msg) => Instance?.Enqueue(msg, LogType.Warning);
    public static void LogError(string msg) => Instance?.Enqueue(msg, LogType.Error);

    void Awake()
    {
        Instance = this;
        if (consoleText != null)
        {
            consoleText.enableWordWrapping = true;
            consoleText.overflowMode = TextOverflowModes.Overflow;
            consoleText.alignment = TextAlignmentOptions.BottomLeft; // 讓新訊息貼底顯示
        }
    }

    void OnEnable()
    {
        Application.logMessageReceivedThreaded += HandleLogThreaded;
    }

    void OnDisable()
    {
        Application.logMessageReceivedThreaded -= HandleLogThreaded;
        if (Instance == this) Instance = null;
    }

    void HandleLogThreaded(string logString, string stackTrace, LogType type)
    {
        // 🚫 忽略 Warning
        if (type == LogType.Warning) return;
        Enqueue(logString, type);
    }

    // 如果你想強制只用 ASCII，把 useAsciiIcons = true
    [SerializeField] bool useAsciiIcons = true;

    private string Compact(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        int head = Math.Max(16, max / 2);
        int tail = Math.Max(16, max - head);
        return $"{s.Substring(0, head)} … [len={s.Length}] … {s.Substring(s.Length - tail)}";
    }

    private IEnumerable<string> Chunk(string s, int size)
    {
        for (int i = 0; i < s.Length; i += size)
            yield return s.Substring(i, Math.Min(size, s.Length - i));
    }

    private void Enqueue(string msg, LogType type)
    {
        string prefix = useAsciiIcons
            ? (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? "[x] " :
               type == LogType.Warning ? "[!] " : "[i] ")
            : (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? "❌ " :
               type == LogType.Warning ? "⚠️ " : "ℹ️ ");

        string time = showTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : "";
        string baseLine = time + prefix;

        if (splitLongMessagesIntoChunks && msg.Length > maxCharsPerLine)
        {
            int idx = 1;
            foreach (var part in Chunk(msg, chunkSize))
            {
                lock (_lock) { _pending.Enqueue($"{baseLine}(part {idx++}) {part}"); }
            }
        }
        else
        {
            string line = baseLine + (msg.Length > maxCharsPerLine ? Compact(msg, maxCharsPerLine) : msg);
            lock (_lock) { _pending.Enqueue(line); }
        }
    }

    void Update()
    {
        bool changed = false;

        // 每幀節流處理
        int taken = 0;
        lock (_lock)
        {
            while (_pending.Count > 0 && taken < maxDequeuesPerFrame)
            {
                var line = _pending.Dequeue();
                _lines.AddLast(line);
                _currentTotalChars += line.Length + 1;
                taken++;
                changed = true;
            }
        }

        // 控制行數和總字元數
        while (_lines.Count > maxLines || _currentTotalChars > maxTotalChars)
        {
            var first = _lines.First.Value;
            _currentTotalChars -= (first.Length + 1);
            _lines.RemoveFirst();
            changed = true;
        }

        if (changed && consoleText != null)
        {
            _sb.Clear();
            foreach (var l in _lines) _sb.AppendLine(l);
            consoleText.text = _sb.ToString();

            consoleText.ForceMeshUpdate();

            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    // 快捷 API
    public void Info(string msg) => Enqueue(msg, LogType.Log);
    public void Warning(string msg) => Enqueue(msg, LogType.Warning);
    public void Error(string msg) => Enqueue(msg, LogType.Error);

    [ContextMenu("Clear")]
    public void Clear()
    {
        _lines.Clear();
        _pending.Clear();
        _currentTotalChars = 0;
        if (consoleText != null) consoleText.text = "";
    }
}
