using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class TTSButtonPlayer : MonoBehaviour
{
    public AudioSource audioSource;

    [Header("Azure OpenAI TTS 設定")]
    public string apiKey = "G6DfFeAbMSVZIGNlDm4VN29uK7ueS6yywh08vNknOWF9C19RDIuqJQQJ99BGACfhMk5XJ3w3AAAAACOGjDLA";
    public string endpoint = "https://s1133-mcx1dy2f-swedencentral.cognitiveservices.azure.com";
    public string deploymentName = "tts";  // TTS 部署名稱
    public string apiVersion = "2025-03-01-preview";
    public string defaultVoice = "shimmer"; // 可改為 shimmer / onyx / nova 等

    void Start()
    {
        StartCoroutine(RequestTTS("Hello from Unity, this is automatic playback."));
    }

    IEnumerator RequestTTS(string message)
    {
        string url = $"{endpoint}openai/deployments/{deploymentName}/audio/speech?api-version={apiVersion}";

        string jsonBody = $@"
{{
    ""input"": ""{message}"",
    ""voice"": ""{defaultVoice}"",
    ""model"": ""{deploymentName}"",
    ""response_format"": ""wav""
}}";

        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(jsonBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("api-key", apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            byte[] audioData = request.downloadHandler.data;
            WAV wav = new WAV(audioData);

            if (audioSource.clip != null)
                Destroy(audioSource.clip);

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
}
