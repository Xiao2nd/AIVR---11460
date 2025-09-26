using UnityEngine;

public class WAV
{
    public float[] LeftChannel { get; private set; }
    public int SampleCount { get; private set; }
    public int Frequency { get; private set; }

    public WAV(byte[] wav)
    {
        Frequency = System.BitConverter.ToInt32(wav, 24);
        int pos = 44;
        int samples = (wav.Length - 44) / 2;

        LeftChannel = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short sample = System.BitConverter.ToInt16(wav, pos);
            LeftChannel[i] = sample / 32768f;
            pos += 2;
        }

        SampleCount = samples;
    }
}
