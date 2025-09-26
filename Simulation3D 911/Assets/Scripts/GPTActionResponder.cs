using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

public class GPTActionResponder : MonoBehaviour
{
    [Header("Azure OpenAI 設定")]
    public string apiKey = "請填入你的金鑰";
    public string endpoint = "https://你的資源名稱.openai.azure.com/";
    public string deploymentName = "gpt35";
    public string apiVersion = "2024-03-01-preview";

    [TextArea(2, 5)]
    public string userPrompt = "我們一起跳舞吧！";

    [Header("動畫控制器")]
    public OneCharacterController characterController;

    private AudioSource audioSource;

    private Dictionary<string, string> actionTriggerMap = new Dictionary<string, string>
    {
        { "跳舞", "Action1" },
        { "揮手", "Action2" },
        { "鼓掌", "Action3" },
        { "敬禮", "Action4" },
        { "打招呼", "Action5" },
        { "游泳", "ArmStretch"},
        { "拳擊", "boxing" },
        { "擊倒", "defeated"}
    };

    public string[] validActions => actionTriggerMap.Keys.ToArray();

    string scenarioInstruction = "";
    string baseInstruction = "";

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        ResetToDefaultScenario();
    }

    public void SetScenario(string scenario)
    {
        if (string.IsNullOrEmpty(scenario)) return;

        if (scenario == "預設")
        {
            ResetToDefaultScenario();
        }
        else if (scenario == "面試")
        {
            scenarioInstruction = "你現在是模擬面試官，請自然回應使用者的問題，語氣要像正式面試情境。";
        }
        else if (scenario == "英文老師")
        {
            scenarioInstruction = "你是一位親切的英文老師，請用簡單易懂的英文與學生互動。";
        }
        else
        {
            scenarioInstruction = scenario;
        }

        Debug.Log("🧠 已套用情境：" + scenarioInstruction);
    }

    void ResetToDefaultScenario()
    {
        string actionList = string.Join("、", actionTriggerMap.Keys);

        baseInstruction = $@"
你是一個動畫角色的控制器。請嚴格按照以下格式回答，不得省略任何一部分：

回答格式：
說話內容（10 至 20 字之間）+ 空一格 + *動作*（從下列挑選一個）

說話內容必須是自然語句、符合人類對話風格，不可以是只有幾個字的命令或單詞。
動作請從這些中選一個：
{actionList}

⚠️ 注意：*動作* 是必填格式，若省略將視為錯誤並重試，請務必使用「*跳舞*」這樣的格式結尾。

範例：
你好啊～今天的陽光真是讓人心情愉快呢！ *打招呼*
";
    }

    public IEnumerator CallGPT(string prompt, int retryCount)
{
    string url = $"{endpoint}openai/deployments/{deploymentName}/chat/completions?api-version={apiVersion}";

    string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    string combinedPrompt = $@"
你現在的情境是：{scenarioInstruction}

請依照以下規則進行回應：
{baseInstruction}

使用者說：{prompt}
";

    string json = $@"
{{
    ""messages"": [
        {{""role"": ""user"", ""content"": ""{EscapeJson(combinedPrompt)}""}}
    ],
    ""temperature"": 0.7
}}";

    byte[] body = Encoding.UTF8.GetBytes(json);

    using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
    {
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("api-key", apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string rawJson = request.downloadHandler.text;
            string reply = ExtractGPTReply(rawJson);
            string action = ExtractAction(reply);
            string message = RemoveActionTag(reply);

            if (!IsValidAction(action))
            {
                Debug.LogWarning("⚠️ GPT 回應中找不到有效動作，重試中...");
                if (retryCount < 2)
                {
                    yield return StartCoroutine(CallGPT(prompt, retryCount + 1));
                    yield break;
                }
                else
                {
                    action = "打招呼";
                    message = "哈囉！很高興見到你！";
                    Debug.LogWarning("⚠️ 自動使用預設動作：打招呼");
                }
            }

            Debug.Log($"🗣 回應文字 👉 {message}");
            Debug.Log($"🎭 動作提示 👉 {action}");

            PlayAnimation(action);
            StartCoroutine(CallTTS(message));
        }
        else
        {
            Debug.LogError("❌ GPT 請求失敗：" + request.error);
            Debug.LogError("❌ 錯誤內容：" + request.downloadHandler.text);
        }
    }
}


    IEnumerator CallTTS(string message)
{
    string gender = GetCurrentCharacterGender();
    string voice = "nova"; // 預設語音

    // 根據性別選擇 Azure 語音模型
    if (gender == "woman") voice = "shimmer";
    else if (gender == "man") voice = "onyx";

    // Azure TTS API 資訊
    string azureTTSUrl = "https://s1133-mcx1dy2f-swedencentral.cognitiveservices.azure.com/openai/deployments/tts/audio/speech?api-version=2025-03-01-preview";
    string ttsModel = "tts"; // 你在 Azure Portal 設定的 TTS 部署名稱

    string jsonBody = $@"
{{
    ""input"": ""{message}"",
    ""voice"": ""{voice}"",
    ""model"": ""{ttsModel}"",
    ""response_format"": ""wav""
}}";

    byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

    UnityWebRequest request = new UnityWebRequest(azureTTSUrl, "POST");
    request.uploadHandler = new UploadHandlerRaw(jsonBytes);
    request.downloadHandler = new DownloadHandlerBuffer();
    request.SetRequestHeader("Content-Type", "application/json");
    request.SetRequestHeader("api-key", apiKey); // 共用 GPT 的 API Key

    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
    {
        byte[] audioData = request.downloadHandler.data;
        WAV wav = new WAV(audioData);

        AudioClip clip = AudioClip.Create("TTS", wav.SampleCount, 1, wav.Frequency, false);
        clip.SetData(wav.LeftChannel, 0);

        audioSource.clip = clip;
        audioSource.Play();
    }
    else
    {
        Debug.LogError("❌ Azure TTS 請求失敗：" + request.error);
        Debug.LogError("❌ 詳細錯誤：" + request.downloadHandler.text);
    }
}


    string GetCurrentCharacterGender()
    {
        if (characterController == null) return "woman";
        return characterController.GetCurrentCharacterGender();
    }

    string ExtractGPTReply(string json)
{
    try
    {
        ChatRoot root = JsonUtility.FromJson<ChatRoot>(json);
        return root.choices[0].message.content;
    }
    catch
    {
        Debug.LogWarning("⚠️ 無法解析 GPT 回應 JSON，回傳預設錯誤字串");
        return "[無法解析回應]";
    }
}

// 對應 JSON 結構的類別
[System.Serializable]
public class ChatRoot
{
    public Choice[] choices;
}

[System.Serializable]
public class Choice
{
    public GPTMessage message;
}

[System.Serializable]
public class GPTMessage
{
    public string role;
    public string content;
}


    string ExtractAction(string text)
    {
        var match = Regex.Match(text, @"\*(.*?)\*");
        return match.Success ? match.Groups[1].Value : "";
    }

    string RemoveActionTag(string text)
    {
        return Regex.Replace(text, @"\*.*?\*", "").Trim();
    }

    bool IsValidAction(string action)
    {
        return actionTriggerMap.ContainsKey(action);
    }

    void PlayAnimation(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
        {
            Debug.LogWarning("❗ 未指定動作名稱");
            return;
        }

        string trigger = MapActionToTrigger(actionName);

        if (!string.IsNullOrEmpty(trigger) && characterController != null)
        {
            characterController.TriggerCurrentCharacterAnimation(trigger);
        }
        else
        {
            Debug.LogWarning($"❌ 找不到對應動畫觸發器或未設定角色控制器！");
        }
    }

    string MapActionToTrigger(string action)
    {
        if (actionTriggerMap.TryGetValue(action, out string trigger))
        {
            return trigger;
        }
        return null;
    }
}