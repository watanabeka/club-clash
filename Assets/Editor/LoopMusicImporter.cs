using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>One-time PCM preparation and deterministic gap-free music import.</summary>
public static class LoopMusicImporter
{
    const string Original = "Assets/ArtSources/Audio/8BitBattleLoop.original.ogg";
    const string Prepared = "Assets/Resources/Audio/battle-loop.wav";

    [MenuItem("Club Clash/Prepare loop music")]
    public static void PrepareLoopMusic()
    {
        AssetDatabase.ImportAsset(Original, ImportAssetOptions.ForceUpdate);
        Configure(Original);
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Original);
        if (clip == null) throw new InvalidOperationException("Missing original CC0 battle loop.");
        clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) throw new InvalidOperationException("Unity could not decode original loop PCM.");
        string directory = Path.GetDirectoryName(Prepared);
        Directory.CreateDirectory(directory);
        WriteWave(Prepared, data, clip.frequency, clip.channels);
        AssetDatabase.ImportAsset(Prepared, ImportAssetOptions.ForceUpdate);
        Configure(Prepared);
        Debug.Log("LOOP_MUSIC_PCM " + clip.samples + " frames; " + clip.frequency + " Hz; " + clip.channels + " channels; " + clip.length + " seconds");
    }

    public static void Configure(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.quality = 1;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = false;
        importer.SaveAndReimport();
    }

    static void WriteWave(string path, float[] samples, int frequency, int channels)
    {
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(frequency);
            writer.Write(frequency * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            foreach (float sample in samples) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * 32767), -32768, 32767));
        }
    }
}

// Reimporting the prepared file later cannot accidentally add a Vorbis boundary gap.
public sealed class LoopMusicAssetPostprocessor : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (assetPath != "Assets/Resources/Audio/battle-loop.wav") return;
        var importer = (AudioImporter)assetImporter;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = false;
    }
}
