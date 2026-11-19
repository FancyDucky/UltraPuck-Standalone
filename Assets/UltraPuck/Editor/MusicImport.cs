using UnityEditor;
using UnityEngine;

namespace UltraPuck.Editor
{
    /// <summary>
    /// Songs dropped into Resources/UltraPuck/Music are set up to play straight off the disk (a whole song held in
    /// memory would be tens of megabytes and make the game slower to start). Nothing to tick by hand.
    /// </summary>
    internal sealed class MusicImport : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/UltraPuck/Music/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            var s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            importer.defaultSampleSettings = s;
        }
    }
}
