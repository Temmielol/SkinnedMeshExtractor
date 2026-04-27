using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace MeshTools
{
    public class SkinnedMeshExtractorWindow : EditorWindow
    {
        private GameObject _target;
        private string _outputFolder = "Assets/Mesh";
        private bool _rebuildInScene = false;
        private bool _savePrefab = true;
        private bool _exportFbx = false;
        private FbxRigType _rigType = FbxRigType.MatchSource;
        private bool _keepOriginalNames = false;
        private string _lastRunStamp;
        private bool _isRunning = false;
        private string _statusText = "";
        private float _progress = 0f;
        private AddRequest _fbxInstallRequest;
        private IEnumerator _extractionEnumerator;

        [MenuItem("Tools/Skinned Mesh Extractor")]
        public static void Open()
        {
            var w = GetWindow<SkinnedMeshExtractorWindow>("Mesh Extractor");
            w.minSize = new Vector2(500, 520);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Skinned Mesh Extractor", EditorStyles.boldLabel);

            DrawDropZone();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_isRunning))
            {
                _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
                _rebuildInScene = EditorGUILayout.Toggle("Rebuild In Scene", _rebuildInScene);
                _savePrefab = EditorGUILayout.Toggle("Save Prefab Asset", _savePrefab);
                _exportFbx = EditorGUILayout.Toggle("Export FBX", _exportFbx);
                using (new EditorGUI.DisabledScope(!_exportFbx))
                {
                    _rigType = (FbxRigType)EditorGUILayout.EnumPopup(
                        new GUIContent("FBX Rig Type",
                            "Which rig type to set on the exported FBX. 'Match Source' copies whatever " +
                            "the source avatar's FBX was using, which is usually what you want so existing " +
                            "animations keep working. Pick Humanoid explicitly only if the source is Generic " +
                            "but you want a humanoid rig on the output."),
                        _rigType);

                    _keepOriginalNames = EditorGUILayout.Toggle(
                        new GUIContent("Keep Original Names",
                            "Don't append '_extracted' to the FBX file, root GameObject, or prefab name. " +
                            "Useful when animations reference objects by specific names."),
                        _keepOriginalNames);
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_target == null || _isRunning))
            {
                if (GUILayout.Button(_isRunning ? "Running..." : "Extract", GUILayout.Height(34)))
                    StartExtraction();
            }

            using (new EditorGUI.DisabledScope(_isRunning))
            {
                if (GUILayout.Button("Clear"))
                {
                    _lastRunStamp = null;
                    _target = null;
                }
            }

            if (_isRunning || _progress > 0f)
            {
                EditorGUILayout.Space();
                var progressRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(progressRect, _progress, _statusText);
            }

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(_lastRunStamp))
                EditorGUILayout.LabelField("Last run: " + _lastRunStamp);
        }

        private void DrawDropZone()
        {
            var rect = GUILayoutUtility.GetRect(0, 90, GUILayout.ExpandWidth(true));
            rect.x += 4; rect.width -= 8;

            var evt = Event.current;
            bool hovering = rect.Contains(evt.mousePosition);

            var bg = new Color(0.15f, 0.15f, 0.15f, 1f);
            if (hovering && evt.type == EventType.DragUpdated) bg = new Color(0.2f, 0.35f, 0.25f);
            EditorGUI.DrawRect(rect, bg);
            GUI.Box(rect, GUIContent.none);

            string label = _target != null
                ? $"Target: {_target.name}\n(drop another to replace)"
                : "Drop a GameObject from the scene or a prefab here";
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                wordWrap = true
            };
            labelStyle.normal.textColor = _target != null ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(rect, label, labelStyle);

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!hovering) break;

                    GameObject candidate = null;
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is GameObject go) { candidate = go; break; }
                    }

                    DragAndDrop.visualMode = candidate != null
                        ? DragAndDropVisualMode.Copy
                        : DragAndDropVisualMode.Rejected;

                    if (evt.type == EventType.DragPerform && candidate != null)
                    {
                        DragAndDrop.AcceptDrag();
                        _target = candidate;
                        Repaint();
                    }
                    evt.Use();
                    break;

                case EventType.MouseDown:
                    if (hovering && _target != null)
                    {
                        EditorGUIUtility.PingObject(_target);
                        evt.Use();
                    }
                    break;
            }
        }

        private void StartExtraction()
        {
            if (_exportFbx && !FbxExporter.IsFbxPackageInstalled())
            {
                _statusText = "Installing FBX Exporter package...";
                _isRunning = true;
                _progress = 0.05f;
                _fbxInstallRequest = Client.Add("com.unity.formats.fbx");
                EditorApplication.update += WaitForFbxInstall;
                Repaint();
                return;
            }
            BeginExtractionCoroutine();
        }

        private void WaitForFbxInstall()
        {
            if (_fbxInstallRequest == null) { EditorApplication.update -= WaitForFbxInstall; return; }
            if (!_fbxInstallRequest.IsCompleted)
            {
                _statusText = "Installing FBX Exporter package...";
                Repaint();
                return;
            }

            EditorApplication.update -= WaitForFbxInstall;
            if (_fbxInstallRequest.Status == StatusCode.Success)
            {
                _statusText = "FBX package installed, starting...";
                _fbxInstallRequest = null;
                
                EditorApplication.delayCall += () => EditorApplication.delayCall += BeginExtractionCoroutine;
            }
            else
            {
                _statusText = "FBX install failed: " + (_fbxInstallRequest.Error?.message ?? "unknown");
                _isRunning = false;
                _progress = 0f;
                _fbxInstallRequest = null;
                Repaint();
            }
        }

        private void BeginExtractionCoroutine()
        {
            _isRunning = true;
            _progress = 0f;
            _statusText = "Starting...";
            _extractionEnumerator = RunExtractionCoroutine(_target);
            EditorApplication.update += TickExtractionCoroutine;
        }

        private void TickExtractionCoroutine()
        {
            if (_extractionEnumerator == null)
            {
                EditorApplication.update -= TickExtractionCoroutine;
                _isRunning = false;
                Repaint();
                return;
            }

            try
            {
                if (!_extractionEnumerator.MoveNext())
                {
                    EditorApplication.update -= TickExtractionCoroutine;
                    _extractionEnumerator = null;
                    _isRunning = false;
                    _progress = 1f;
                    _statusText = "Done.";
                    Repaint();
                }
                else
                {
                    Repaint();
                }
            }
            catch (Exception e)
            {
                EditorApplication.update -= TickExtractionCoroutine;
                _extractionEnumerator = null;
                _isRunning = false;
                _statusText = "Error: " + e.Message;
                Debug.LogException(e);
                Repaint();
            }
        }

        private IEnumerator RunExtractionCoroutine(GameObject root)
        {
            _lastRunStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            _statusText = "Preparing output folder...";
            _progress = 0.02f;
            yield return null;

            MeshExtractorUtils.EnsureFolder(_outputFolder);
            var runFolder = Path.Combine(_outputFolder,
                "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + MeshExtractorUtils.MakeSafe(root.name));
            MeshExtractorUtils.EnsureFolder(runFolder);

            _statusText = "Scanning hierarchy...";
            _progress = 0.05f;
            yield return null;

            var slots = new List<MeshSlot>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                    slots.Add(new MeshSlot {
                        Component = mf, Owner = mf.gameObject,
                        Source = mf.sharedMesh, IsSkinned = false });
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null)
                    slots.Add(new MeshSlot {
                        Component = smr, Owner = smr.gameObject,
                        Source = smr.sharedMesh, IsSkinned = true });

            if (slots.Count == 0)
            {
                Debug.LogWarning("Skinned Mesh Extractor: no meshes found under " + root.name);
                AssetDatabase.Refresh();
                yield break;
            }

            _statusText = $"Enabling Read/Write on {slots.Count} source(s)...";
            _progress = 0.10f;
            yield return null;

            var rwRestores = ForceReadWriteOnSources(slots);

            
            foreach (var slot in slots)
            {
                if (slot.IsSkinned)
                {
                    var smr = slot.Component as SkinnedMeshRenderer;
                    if (smr != null && smr.sharedMesh != null) slot.Source = smr.sharedMesh;
                }
                else
                {
                    var mf = slot.Component as MeshFilter;
                    if (mf != null && mf.sharedMesh != null) slot.Source = mf.sharedMesh;
                }
            }

            try
            {
                
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    _statusText = $"Extracting ({i+1}/{slots.Count}): {slot.Owner.name}";
                    _progress = 0.10f + 0.70f * ((float)i / slots.Count);

                    MeshAttributeExtractor.ExtractAllAttributes(slot);
                    slot.Rebuilt = MeshBuilder.BuildMeshFromAttr(slot, runFolder);
                }

                _statusText = "Rebuilding hierarchy...";
                _progress = 0.85f;
                yield return null;

                if (_rebuildInScene || _savePrefab)
                    HierarchyRebuilder.Rebuild(root, slots, runFolder, _savePrefab, _rebuildInScene, _exportFbx, _keepOriginalNames, _rigType);
            }
            finally
            {
                RestoreReadWrite(rwRestores);
            }

            AssetDatabase.Refresh();

            _statusText = $"Done — {slots.Count} mesh(es) processed. Output: {runFolder}";
            _progress = 1f;
        }

        private class RwRestore
        {
            public string importerPath;
            public bool originalIsReadable;
        }

        private List<RwRestore> ForceReadWriteOnSources(List<MeshSlot> slots)
        {
            var restores = new List<RwRestore>();
            var seenPaths = new HashSet<string>();

            foreach (var slot in slots)
            {
                if (slot.Source == null) continue;
                var path = AssetDatabase.GetAssetPath(slot.Source);
                if (string.IsNullOrEmpty(path)) continue;
                if (!seenPaths.Add(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                if (!importer.isReadable)
                {
                    restores.Add(new RwRestore {
                        importerPath = path,
                        originalIsReadable = importer.isReadable
                    });
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }

            return restores;
        }

        private void RestoreReadWrite(List<RwRestore> restores)
        {
            foreach (var r in restores)
            {
                var importer = AssetImporter.GetAtPath(r.importerPath) as ModelImporter;
                if (importer == null) continue;
                importer.isReadable = r.originalIsReadable;
                importer.SaveAndReimport();
            }
        }
    }
}
