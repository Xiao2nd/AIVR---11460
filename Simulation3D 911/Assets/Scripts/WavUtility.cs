using UnityEngine;
using System.IO;

public static class WavUtility
{
    const int HEADER_SIZE = 44;

    public static byte[] FromAudioClip(AudioClip clip)
    {
        MemoryStream stream = new MemoryStream();

        int sampleCount = clip.samples * clip.channels;
        float[] samples = new float[sampleCount];
        clip.GetData(samples, 0);

        byte[] wav = new byte[HEADER_SIZE + sampleCount * 2];
        int i = 0;

        // RIFF header
        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        riff.CopyTo(wav, i); i += 4;

        byte[] chunkSize = System.BitConverter.GetBytes(wav.Length - 8);
        chunkSize.CopyTo(wav, i); i += 4;

        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        wave.CopyTo(wav, i); i += 4;

        // fmt subchunk
        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        fmt.CopyTo(wav, i); i += 4;

        byte[] subChunk1 = System.BitConverter.GetBytes(16);
        subChunk1.CopyTo(wav, i); i += 4;

        ushort audioFormat = 1;
        byte[] audioFormatBytes = System.BitConverter.GetBytes(audioFormat);
        audioFormatBytes.CopyTo(wav, i); i += 2;

        byte[] numChannels = System.BitConverter.GetBytes((ushort)clip.channels);
        numChannels.CopyTo(wav, i); i += 2;

        byte[] sampleRate = System.BitConverter.GetBytes(clip.frequency);
        sampleRate.CopyTo(wav, i); i += 4;

        byte[] byteRate = System.BitConverter.GetBytes(clip.frequency * clip.channels * 2);
        byteRate.CopyTo(wav, i); i += 4;

        ushort blockAlign = (ushort)(clip.channels * 2);
        byte[] blockAlignBytes = System.BitConverter.GetBytes(blockAlign);
        blockAlignBytes.CopyTo(wav, i); i += 2;

        ushort bps = 16;
        byte[] bitsPerSample = System.BitConverter.GetBytes(bps);
        bitsPerSample.CopyTo(wav, i); i += 2;

        // data subchunk
        byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
        datastring.CopyTo(wav, i); i += 4;

        byte[] subChunk2 = System.BitConverter.GetBytes(sampleCount * 2);
        subChunk2.CopyTo(wav, i); i += 4;

        int sampleIndex = 0;
        while (sampleIndex < samples.Length)
        {
            short sample = (short)(samples[sampleIndex] * short.MaxValue);
            byte[] bytes = System.BitConverter.GetBytes(sample);
            bytes.CopyTo(wav, i);
            i += 2;
            sampleIndex++;
        }

        return wav;
    }
}
