using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;
using UnityEngine.UI;

namespace MornLib
{
    /// <summary>
    /// PSDファイルのレイヤーをCanvas配下にUI Imageとして展開するエディタツール。
    /// PSDファイルを直接パースしてレイヤーの元座標を取得する。
    /// </summary>
    public static class MornPsdToUIConverter
    {
        /// <summary>レイヤーのセクション種別。</summary>
        private enum SectionType
        {
            Normal = 0,
            GroupOpen = 1,
            GroupClosed = 2,
            GroupEnd = 3,
        }

        private struct PsdLayerInfo
        {
            public string Name;
            public int Top;
            public int Left;
            public int Bottom;
            public int Right;
            public bool IsVisible;
            public SectionType Section;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private struct PsdHeader
        {
            public int Width;
            public int Height;
        }

        // ============================================================
        // Menu Items
        // ============================================================

        [MenuItem("Assets/PSD to UI/レイヤーをPNGエクスポート")]
        private static void ExportLayersToPng()
        {
            var assetPath = GetSelectedPsdPath();
            if (assetPath == null) return;

            var fullPath = Path.GetFullPath(assetPath);
            if (!TryParsePsd(fullPath, out _, out _))
            {
                EditorUtility.DisplayDialog("PSD to UI", "PSD ファイルの解析に失敗しました。", "OK");
                return;
            }

            var count = ExportSpritesToPng(assetPath);
            if (count > 0)
            {
                Debug.Log($"[PSD to UI] {count} レイヤーをPNGエクスポート完了");
            }
        }

        [MenuItem("Assets/PSD to UI/レイヤーをPNGエクスポート", true)]
        private static bool ValidateExportLayersToPng() => IsSelectedPsdWithMultipleSprite();

        [MenuItem("Assets/PSD to UI/レイヤーをPNGエクスポート + UIに展開")]
        private static void ExportAndBuildUI()
        {
            var assetPath = GetSelectedPsdPath();
            if (assetPath == null) return;

            var fullPath = Path.GetFullPath(assetPath);
            if (!TryParsePsd(fullPath, out var header, out var layers))
            {
                EditorUtility.DisplayDialog("PSD to UI", "PSD ファイルの解析に失敗しました。", "OK");
                return;
            }

            var pngSprites = ExportSpritesToPngAndLoad(assetPath);
            if (pngSprites == null) return;

            BuildUI(assetPath, header, layers, pngSprites, false);
        }

        [MenuItem("Assets/PSD to UI/レイヤーをPNGエクスポート + UIに展開", true)]
        private static bool ValidateExportAndBuildUI() => IsSelectedPsdWithMultipleSprite();

        [MenuItem("Assets/PSD to UI/レイヤーをPNGエクスポート + UIに展開 (階層あり)")]
        private static void ExportAndBuildUIHierarchy()
        {
            var assetPath = GetSelectedPsdPath();
            if (assetPath == null) return;

            var fullPath = Path.GetFullPath(assetPath);
            if (!TryParsePsd(fullPath, out var header, out var layers))
            {
                EditorUtility.DisplayDialog("PSD to UI", "PSD ファイルの解析に失敗しました。", "OK");
                return;
            }

            var pngSprites = ExportSpritesToPngAndLoad(assetPath);
            if (pngSprites == null) return;

            BuildUI(assetPath, header, layers, pngSprites, true);
        }

        [MenuItem("Assets/PSD to UI/レイヤーをPNGエクスポート + UIに展開 (階層あり)", true)]
        private static bool ValidateExportAndBuildUIHierarchy() => IsSelectedPsdWithMultipleSprite();

        [MenuItem("Assets/PSD to UI/UIに展開")]
        private static void ConvertSelectedPsd()
        {
            ConvertSelectedPsdInternal(false);
        }

        [MenuItem("Assets/PSD to UI/UIに展開", true)]
        private static bool ValidateConvertSelectedPsd() => IsSelectedPsdWithMultipleSprite();

        [MenuItem("Assets/PSD to UI/UIに展開 (階層あり)")]
        private static void ConvertSelectedPsdHierarchy()
        {
            ConvertSelectedPsdInternal(true);
        }

        [MenuItem("Assets/PSD to UI/UIに展開 (階層あり)", true)]
        private static bool ValidateConvertSelectedPsdHierarchy() => IsSelectedPsdWithMultipleSprite();

        private static void ConvertSelectedPsdInternal(bool keepHierarchy)
        {
            var assetPath = GetSelectedPsdPath();
            if (assetPath == null) return;

            var fullPath = Path.GetFullPath(assetPath);
            if (!TryParsePsd(fullPath, out var header, out var layers))
            {
                EditorUtility.DisplayDialog("PSD to UI", "PSD ファイルの解析に失敗しました。", "OK");
                return;
            }

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var spriteByName = allAssets.OfType<Sprite>().ToDictionary(s => s.name);

            var parent = FindUIParent();
            if (parent == null) return;

            var rootObj = CreateRootObject(assetPath, header, parent);
            var canvasW = (float)header.Width;
            var canvasH = (float)header.Height;

            int count;
            if (keepHierarchy)
            {
                count = BuildLayersHierarchy(rootObj.transform, layers, spriteByName, canvasW, canvasH);
            }
            else
            {
                count = BuildLayersFlat(rootObj.transform, layers, spriteByName, canvasW, canvasH);
            }

            Selection.activeGameObject = rootObj;
            Debug.Log(
                $"[PSD to UI] {Path.GetFileNameWithoutExtension(assetPath)}: {count} レイヤーを展開 (PSD: {header.Width}x{header.Height})");
        }

        // ============================================================
        // PNG Export (Unity完結)
        // ============================================================

        /// <summary>PSDのサブスプライトをPNGファイルとして書き出す。</summary>
        private static int ExportSpritesToPng(string assetPath)
        {
            var psdName = Path.GetFileNameWithoutExtension(assetPath);
            var outputDir = Path.GetDirectoryName(assetPath);
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var sprites = allAssets.OfType<Sprite>().ToList();

            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("PSD to UI",
                    "PSD にスプライトが見つかりません。\nImport Settingsで Sprite Mode を Multiple に設定してください。", "OK");
                return 0;
            }

            var count = 0;
            foreach (var sprite in sprites)
            {
                var safeName = SanitizeFileName(sprite.name);
                var pngPath = Path.Combine(outputDir, $"{psdName}_{safeName}.png");
                var pngBytes = ExtractSpritePixels(sprite);
                if (pngBytes == null) continue;

                File.WriteAllBytes(Path.GetFullPath(pngPath), pngBytes);
                count++;
            }

            AssetDatabase.Refresh();
            return count;
        }

        /// <summary>PNGエクスポートしてSpriteを読み込んで返す。</summary>
        private static Dictionary<string, Sprite> ExportSpritesToPngAndLoad(string assetPath)
        {
            var psdName = Path.GetFileNameWithoutExtension(assetPath);
            var outputDir = Path.GetDirectoryName(assetPath);
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var sprites = allAssets.OfType<Sprite>().ToList();

            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("PSD to UI",
                    "PSD にスプライトが見つかりません。\nImport Settingsで Sprite Mode を Multiple に設定してください。", "OK");
                return null;
            }

            var pngPaths = new Dictionary<string, string>();
            foreach (var sprite in sprites)
            {
                var safeName = SanitizeFileName(sprite.name);
                var pngAssetPath = Path.Combine(outputDir, $"{psdName}_{safeName}.png");
                var pngBytes = ExtractSpritePixels(sprite);
                if (pngBytes == null) continue;

                File.WriteAllBytes(Path.GetFullPath(pngAssetPath), pngBytes);
                pngPaths[sprite.name] = pngAssetPath;
            }

            AssetDatabase.Refresh();

            // Spriteインポート設定
            foreach (var pngPath in pngPaths.Values)
            {
                var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
                if (importer == null) continue;
                if (importer.textureType == TextureImporterType.Sprite) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            var result = new Dictionary<string, Sprite>();
            foreach (var kvp in pngPaths)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(kvp.Value);
                if (sprite != null) result[kvp.Key] = sprite;
            }

            Debug.Log($"[PSD to UI] {pngPaths.Count} レイヤーをPNGエクスポート完了");
            return result;
        }

        /// <summary>SpriteのピクセルデータをPNGとして取得する。</summary>
        private static byte[] ExtractSpritePixels(Sprite sprite)
        {
            var rect = sprite.textureRect;
            var srcTex = sprite.texture;

            // テクスチャが読み取り不可の場合、RenderTextureにコピーして読み出す
            var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(srcTex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            var x = Mathf.RoundToInt(rect.x);
            var y = Mathf.RoundToInt(rect.y);
            var w = Mathf.RoundToInt(rect.width);
            var h = Mathf.RoundToInt(rect.height);

            var readTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readTex.ReadPixels(new Rect(x, y, w, h), 0, 0);
            readTex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var pngBytes = readTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(readTex);
            return pngBytes;
        }

        // ============================================================
        // UI Build
        // ============================================================

        private static void BuildUI(string assetPath, PsdHeader header, List<PsdLayerInfo> layers,
            Dictionary<string, Sprite> spriteByName, bool keepHierarchy)
        {
            var parent = FindUIParent();
            if (parent == null) return;

            var rootObj = CreateRootObject(assetPath, header, parent);
            var canvasW = (float)header.Width;
            var canvasH = (float)header.Height;

            int count;
            if (keepHierarchy)
            {
                count = BuildLayersHierarchy(rootObj.transform, layers, spriteByName, canvasW, canvasH);
            }
            else
            {
                count = BuildLayersFlat(rootObj.transform, layers, spriteByName, canvasW, canvasH);
            }

            Selection.activeGameObject = rootObj;
            Debug.Log(
                $"[PSD to UI] {Path.GetFileNameWithoutExtension(assetPath)}: {count} レイヤーをUI展開 (PSD: {header.Width}x{header.Height})");
        }

        private static int BuildLayersFlat(Transform root, List<PsdLayerInfo> layers,
            Dictionary<string, Sprite> spriteByName, float canvasW, float canvasH)
        {
            var count = 0;
            foreach (var layer in layers)
            {
                if (layer.Section != SectionType.Normal) continue;
                if (!spriteByName.TryGetValue(layer.Name, out var sprite)) continue;
                CreateLayerImage(root, layer, sprite, canvasW, canvasH);
                count++;
            }

            return count;
        }

        /// <summary>
        /// PSDのグループ構造をGameObject階層として再現する。
        /// PSDレイヤーはボトムアップ順で格納されている:
        ///   GroupEnd(type3) → 子レイヤー → GroupOpen(type1/2, グループ名)
        /// </summary>
        private static int BuildLayersHierarchy(Transform root, List<PsdLayerInfo> layers,
            Dictionary<string, Sprite> spriteByName, float canvasW, float canvasH)
        {
            var parentStack = new Stack<Transform>();
            parentStack.Push(root);
            var pendingGroups = new Stack<List<(PsdLayerInfo layer, Sprite sprite, Transform groupChild)>>();
            var count = 0;

            foreach (var layer in layers)
            {
                switch (layer.Section)
                {
                    case SectionType.GroupEnd:
                        // グループ境界の終端マーカー → 新しいグループのコンテキストを開始
                        pendingGroups.Push(new List<(PsdLayerInfo, Sprite, Transform)>());
                        break;

                    case SectionType.GroupOpen:
                    case SectionType.GroupClosed:
                        // グループヘッダー → グループGameObjectを作成し、溜めた子を配置
                        var currentParent = parentStack.Peek();
                        var groupObj = new GameObject(layer.Name, typeof(RectTransform));
                        groupObj.transform.SetParent(currentParent, false);
                        var groupRt = groupObj.GetComponent<RectTransform>();
                        groupRt.anchorMin = Vector2.zero;
                        groupRt.anchorMax = Vector2.one;
                        groupRt.offsetMin = Vector2.zero;
                        groupRt.offsetMax = Vector2.zero;
                        groupObj.SetActive(layer.IsVisible);

                        if (pendingGroups.Count > 0)
                        {
                            var children = pendingGroups.Pop();
                            // PSDはボトムアップ順 = 追加順そのままでUnityのSibling順（後が手前）と一致
                            for (var i = 0; i < children.Count; i++)
                            {
                                var (childLayer, childSprite, childGroup) = children[i];
                                if (childGroup != null)
                                {
                                    // サブグループ: 既に作成済みのGameObjectを移動
                                    childGroup.SetParent(groupObj.transform, false);
                                }
                                else
                                {
                                    CreateLayerImage(groupObj.transform, childLayer, childSprite, canvasW, canvasH);
                                    count++;
                                }
                            }
                        }

                        // このグループ自体を親のpendingに登録
                        if (pendingGroups.Count > 0)
                        {
                            pendingGroups.Peek().Add((layer, null, groupObj.transform));
                        }

                        break;

                    default:
                        // 通常レイヤー: スプライトが存在する場合のみ生成
                        if (!spriteByName.TryGetValue(layer.Name, out var sprite)) break;

                        if (pendingGroups.Count > 0)
                        {
                            // グループ内 → 後でまとめて配置するため溜める
                            pendingGroups.Peek().Add((layer, sprite, null));
                        }
                        else
                        {
                            // トップレベル
                            CreateLayerImage(root, layer, sprite, canvasW, canvasH);
                            count++;
                        }

                        break;
                }
            }

            return count;
        }

        // ============================================================
        // Shared Helpers
        // ============================================================

        private static GameObject CreateRootObject(string assetPath, PsdHeader header, Transform parent)
        {
            var rootName = Path.GetFileNameWithoutExtension(assetPath);
            var rootObj = new GameObject(rootName, typeof(RectTransform));
            rootObj.transform.SetParent(parent, false);
            var rootRt = rootObj.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(header.Width, header.Height);
            Undo.RegisterCreatedObjectUndo(rootObj, "PSD to UI");
            return rootObj;
        }

        private static void CreateLayerImage(Transform parent, PsdLayerInfo layer, Sprite sprite,
            float canvasW, float canvasH)
        {
            var imgObj = new GameObject(layer.Name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imgObj.transform.SetParent(parent, false);

            var image = imgObj.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;

            var rt = imgObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            var w = layer.Width;
            var h = layer.Height;
            if ((w <= 0 || h <= 0) && sprite != null)
            {
                // 非表示レイヤーなどboundsが0の場合、スプライトサイズを使用
                w = Mathf.RoundToInt(sprite.rect.width);
                h = Mathf.RoundToInt(sprite.rect.height);
            }

            if (sprite != null)
            {
                image.SetNativeSize();
            }

            var centerX = layer.Left + w * 0.5f - canvasW * 0.5f;
            var centerY = -(layer.Top + h * 0.5f - canvasH * 0.5f);
            rt.anchoredPosition = new Vector2(centerX, centerY);
            rt.sizeDelta = new Vector2(w, h);
            imgObj.SetActive(layer.IsVisible);
        }

        private static bool IsSelectedPsd()
        {
            if (Selection.activeObject == null) return false;
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSelectedPsdWithMultipleSprite()
        {
            if (!IsSelectedPsd()) return false;
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var importer = AssetImporter.GetAtPath(path) as PSDImporter;
            return importer != null;
        }

        private static string GetSelectedPsdPath()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("PSD to UI", "Projectウィンドウで PSD ファイルを選択してください。", "OK");
                return null;
            }

            var assetPath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("PSD to UI", "PSD ファイルを選択してください。", "OK");
                return null;
            }

            return assetPath;
        }

        private static Transform FindUIParent()
        {
            var activeTransform = Selection.activeTransform;
            if (activeTransform != null && activeTransform.GetComponentInParent<Canvas>() != null)
            {
                return activeTransform;
            }

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("PSD to UI", "シーンに Canvas が見つかりません。", "OK");
                return null;
            }

            return canvas.transform;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return sb.ToString();
        }

        // ============================================================
        // PSD Parser
        // ============================================================

        private static bool TryParsePsd(string filePath, out PsdHeader header, out List<PsdLayerInfo> layers)
        {
            header = default;
            layers = new List<PsdLayerInfo>();

            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);

                var signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (signature != "8BPS") return false;

                var version = ReadInt16BE(reader);
                if (version != 1) return false;

                reader.ReadBytes(6);
                ReadInt16BE(reader);
                header.Height = ReadInt32BE(reader);
                header.Width = ReadInt32BE(reader);
                ReadInt16BE(reader);
                ReadInt16BE(reader);

                var colorModeLen = ReadInt32BE(reader);
                reader.ReadBytes(colorModeLen);

                var imageResourceLen = ReadInt32BE(reader);
                reader.ReadBytes(imageResourceLen);

                var layerMaskLen = ReadInt32BE(reader);
                if (layerMaskLen == 0) return true;

                var layerInfoLen = ReadInt32BE(reader);
                if (layerInfoLen == 0) return true;

                var layerCount = ReadInt16BE(reader);
                if (layerCount < 0) layerCount = (short)-layerCount;

                for (var i = 0; i < layerCount; i++)
                {
                    var info = new PsdLayerInfo
                    {
                        Top = ReadInt32BE(reader),
                        Left = ReadInt32BE(reader),
                        Bottom = ReadInt32BE(reader),
                        Right = ReadInt32BE(reader),
                        Section = SectionType.Normal,
                    };

                    var channelCount = ReadInt16BE(reader);
                    reader.ReadBytes(channelCount * 6);

                    Encoding.ASCII.GetString(reader.ReadBytes(4));
                    reader.ReadBytes(4);
                    reader.ReadByte();
                    reader.ReadByte();
                    var flags = reader.ReadByte();
                    info.IsVisible = (flags & 0x02) == 0;
                    reader.ReadByte();

                    var extraDataLen = ReadInt32BE(reader);
                    var extraDataEnd = stream.Position + extraDataLen;

                    var maskLen = ReadInt32BE(reader);
                    if (maskLen > 0) reader.ReadBytes(maskLen);

                    var blendRangeLen = ReadInt32BE(reader);
                    if (blendRangeLen > 0) reader.ReadBytes(blendRangeLen);

                    var nameLen = reader.ReadByte();
                    var nameBytes = reader.ReadBytes(nameLen);
                    info.Name = Encoding.UTF8.GetString(nameBytes);
                    var totalNameBytes = 1 + nameLen;
                    var padding = (4 - totalNameBytes % 4) % 4;
                    if (padding > 0) reader.ReadBytes(padding);

                    while (stream.Position < extraDataEnd - 12)
                    {
                        var sig = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        if (sig != "8BIM" && sig != "8B64")
                        {
                            stream.Position -= 4;
                            break;
                        }

                        var key = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        var dataLen = ReadInt32BE(reader);
                        var dataEnd = stream.Position + dataLen;
                        if (dataLen % 2 != 0) dataEnd++;

                        if (key == "luni")
                        {
                            var unicodeLen = ReadInt32BE(reader);
                            var unicodeBytes = reader.ReadBytes(unicodeLen * 2);
                            info.Name = Encoding.BigEndianUnicode.GetString(unicodeBytes);
                        }
                        else if (key == "lsct" || key == "lsdk")
                        {
                            // レイヤーセクション区切り: 0=通常, 1=グループ(開), 2=グループ(閉), 3=境界マーカー
                            var sectionType = ReadInt32BE(reader);
                            if (sectionType >= 0 && sectionType <= 3)
                            {
                                info.Section = (SectionType)sectionType;
                            }
                        }

                        stream.Position = dataEnd;
                    }

                    stream.Position = extraDataEnd;
                    layers.Add(info);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PSD to UI] PSD parse error: {e.Message}");
                return false;
            }
        }

        private static short ReadInt16BE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(2);
            return (short)((bytes[0] << 8) | bytes[1]);
        }

        private static int ReadInt32BE(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }
    }
}
