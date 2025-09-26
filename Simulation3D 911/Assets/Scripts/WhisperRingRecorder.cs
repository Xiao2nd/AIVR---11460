using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;

[RequireComponent(typeof(AudioSource))]
public class WhisperRingRecorder : MonoBehaviour
{
    public int sampleRate = 44100;
    public float silenceThreshold = 0.01f;
    public float silenceDuration = 1.0f;

    public string azureWhisperApiKey = "G6DfFeAbMSVZIGNlDm4VN29uK7ueS6yywh08vNknOWF9C19RDIuqJQQJ99BGACfhMk5XJ3w3AAAAACOGjDLA";
    public string azureRegion = "swedencentral"; // 根據你建立服務時的區域修改
    public GPTActionResponder gptResponder;

    private AudioClip micClip;
    private string filePath;
    private bool isRecordingSegment = false;
    private int segmentStartPosition = 0;
    private float silenceTimer = 0f;

    void Start()
    {
        filePath = Path.Combine(Application.persistentDataPath, "auto_record.wav");
        micClip = Microphone.Start(null, true, 60, sampleRate);
        Debug.Log("🎧 背景錄音已啟動");
        StartCoroutine(MonitorMicrophone());
    }

    IEnumerator MonitorMicrophone()
    {
        while (true)
        {
            int currentPos = Microphone.GetPosition(null);
            if (currentPos < 0) { yield return null; continue; }

            int checkLength = 1024;
            int start = currentPos - checkLength;
            if (start < 0) start += micClip.samples;

            float[] samples = new float[checkLength];
            micClip.GetData(samples, start);

            float maxVolume = 0f;
            foreach (var s in samples)
                maxVolume = Mathf.Max(maxVolume, Mathf.Abs(s));

            if (!isRecordingSegment)
            {
                if (maxVolume > silenceThreshold)
                {
                    segmentStartPosition = start;
                    isRecordingSegment = true;
                    silenceTimer = 0f;
                    Debug.Log("🎙 偵測到聲音，開始錄音段");
                }
            }
            else
            {
                if (maxVolume < silenceThreshold)
                {
                    silenceTimer += 0.1f;
                    if (silenceTimer >= silenceDuration)
                    {
                        int segmentEndPosition = Microphone.GetPosition(null);
                        SaveAndSendSegment(segmentStartPosition, segmentEndPosition);
                        isRecordingSegment = false;
                        silenceTimer = 0f;
                        Debug.Log("🛑 錄音段結束");
                    }
                }
                else
                {
                    silenceTimer = 0f;
                }
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    void SaveAndSendSegment(int startPos, int endPos)
    {
        int totalSamples = endPos - startPos;
        if (totalSamples < 0) totalSamples += micClip.samples;

        float[] data = new float[totalSamples * micClip.channels];
        micClip.GetData(data, startPos);

        AudioClip clip = AudioClip.Create("segment", totalSamples, micClip.channels, micClip.frequency, false);
        clip.SetData(data, 0);

        byte[] wav = WavUtility.FromAudioClip(clip);
        File.WriteAllBytes(filePath, wav);
        Debug.Log("💾 錄音儲存完成：" + filePath);

        StartCoroutine(SendToAzureWhisper(filePath));
    }

    IEnumerator SendToAzureWhisper(string path)
    {
        Debug.Log("📤 傳送至 Azure Whisper...");
        byte[] audioBytes = File.ReadAllBytes(path);

        UnityWebRequest request = UnityWebRequest.Put($"https://{azureRegion}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language=zh-TW", audioBytes);
        request.method = UnityWebRequest.kHttpVerbPOST;
        request.SetRequestHeader("Content-Type", "audio/wav; codecs=audio/pcm; samplerate=44100");
        request.SetRequestHeader("Ocp-Apim-Subscription-Key", azureWhisperApiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ Azure Whisper 錯誤：" + request.responseCode);
            Debug.LogError("❌ 錯誤內容：" + request.downloadHandler.text);
            isRecordingSegment = false;
            yield break;
        }

        string resultJson = request.downloadHandler.text;
        Debug.Log("✅ Azure Whisper 回應：" + resultJson);

        string whisperText = "";
        try
        {
            var match = Regex.Match(resultJson, "\"DisplayText\"\\s*:\\s*\"(.*?)\"");
            if (match.Success)
                whisperText = match.Groups[1].Value;
        }
        catch
        {
            whisperText = "[Azure Whisper 回應解析失敗]";
        }

        if (gptResponder != null && !string.IsNullOrEmpty(whisperText))
        {
            StartCoroutine(gptResponder.CallGPT(whisperText, 0));
        }

        isRecordingSegment = false;
    }
}
