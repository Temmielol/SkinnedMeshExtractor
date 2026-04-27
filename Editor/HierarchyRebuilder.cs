using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MeshTools
{
    internal static class HierarchyRebuilder
    {
        public static void Rebuild(
            GameObject source,
            List<MeshSlot> slots,
            string runFolder,
            bool savePrefab,
            bool rebuildInScene,
            bool exportFbx,
            bool keepOriginalNames,
            FbxRigType rigType)
        {
            var clone = Object.Instantiate(source);
            clone.name = keepOriginalNames ? source.name : source.name + "_extracted";

            ApplyRebuiltMeshes(clone, source, slots);

            GameObject prefabSource = clone;
            GameObject fbxInstance = null;

            if (exportFbx)
            {
                var fbxPath = Path.Combine(runFolder, clone.name + ".fbx").Replace('\\', '/');
                FbxExporter.TryExportFbx(clone, fbxPath);

                if (File.Exists(fbxPath))
                {
                    var sourceType = FbxExporter.DetectSourceRigType(source);
                    var fallback = sourceType ?? ModelImporterAnimationType.Generic;
                    FbxExporter.SetRigTypeOnAsset(fbxPath, rigType, fallback);

                    if (savePrefab)
                    {
                        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                        if (fbxAsset != null)
                        {
                            fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                            fbxInstance.name = clone.name;

                            fbxInstance.transform.localPosition = source.transform.localPosition;
                            fbxInstance.transform.localRotation = source.transform.localRotation;
                            fbxInstance.transform.localScale    = source.transform.localScale;

                            CopyExtraComponents(source, fbxInstance);
                            SyncRendererState(source, fbxInstance);

                            prefabSource = fbxInstance;
                        }
                        else
                        {
                            Debug.LogWarning("FBX export ran but the asset could not be loaded at " + fbxPath + ". Falling back to clone-based prefab.");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("FBX was not created at " + fbxPath + " (package missing or export failed).");
                }
            }

            if (savePrefab)
            {
                var prefabPath = Path.Combine(runFolder, clone.name + ".prefab").Replace('\\', '/');
                PrefabUtility.SaveAsPrefabAssetAndConnect(prefabSource, prefabPath, InteractionMode.AutomatedAction);
            }

            if (rebuildInScene)
            {
                Selection.activeGameObject = prefabSource;
                if (prefabSource == fbxInstance && clone != null)
                    Object.DestroyImmediate(clone);
                else if (fbxInstance != null && fbxInstance != prefabSource)
                    Object.DestroyImmediate(fbxInstance);
            }
            else
            {
                if (clone != null) Object.DestroyImmediate(clone);
                if (fbxInstance != null) Object.DestroyImmediate(fbxInstance);
            }
        }

        private static void CopyExtraComponents(GameObject source, GameObject target)
        {
            var targetByPath = new Dictionary<string, Transform>();
            foreach (var t in target.GetComponentsInChildren<Transform>(true))
                targetByPath[MeshExtractorUtils.GetPath(t, target.transform)] = t;

            foreach (var srcT in source.GetComponentsInChildren<Transform>(true))
            {
                var path = MeshExtractorUtils.GetPath(srcT, source.transform);
                if (!targetByPath.TryGetValue(path, out var dstT) || dstT == null) continue;

                var dstGO = dstT.gameObject;

                if (dstGO.activeSelf != srcT.gameObject.activeSelf)
                    dstGO.SetActive(srcT.gameObject.activeSelf);

                var existing = new HashSet<System.Type>();
                foreach (var c in dstGO.GetComponents<Component>())
                    if (c != null) existing.Add(c.GetType());

                foreach (var srcComp in srcT.GetComponents<Component>())
                {
                    if (srcComp == null) continue; // missing script
                    if (IsIntrinsicComponent(srcComp)) continue;

                    var type = srcComp.GetType();

                    if (existing.Contains(type) && IsTypicallyUniqueOnGameObject(type))
                        continue;

                    if (!ComponentUtility.CopyComponent(srcComp))
                        continue;

                    if (!ComponentUtility.PasteComponentAsNew(dstGO))
                    {
                        Debug.LogWarning("Failed to paste component " + type.Name + " onto " + dstGO.name);
                    }
                }
            }
        }

        private static bool IsIntrinsicComponent(Component c)
        {
            return c is Transform
                || c is MeshFilter
                || c is MeshRenderer
                || c is SkinnedMeshRenderer;
        }

        private static void SyncRendererState(GameObject source, GameObject target)
        {
            PairAndSync(
                source.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                target.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                SyncSkinnedRenderer,
                r => r.sharedMesh);

            PairAndSync(
                source.GetComponentsInChildren<MeshRenderer>(true),
                target.GetComponentsInChildren<MeshRenderer>(true),
                SyncMeshRenderer,
                r => {
                    var mf = r.GetComponent<MeshFilter>();
                    return mf != null ? mf.sharedMesh : null;
                });
        }

        private static void PairAndSync<T>(
            T[] srcs,
            T[] dsts,
            System.Action<T, T> syncFn,
            System.Func<T, Mesh> meshGetter) where T : Renderer
        {
            var unmatchedDsts = new List<T>(dsts);
            var pairs = new List<(T src, T dst)>();

            var byName = new Dictionary<string, List<T>>();
            foreach (var d in unmatchedDsts)
            {
                if (d == null) continue;
                var n = d.gameObject.name;
                if (!byName.TryGetValue(n, out var list))
                {
                    list = new List<T>();
                    byName[n] = list;
                }
                list.Add(d);
            }

            var stillUnmatchedSrcs = new List<T>();
            foreach (var s in srcs)
            {
                if (s == null) continue;
                if (byName.TryGetValue(s.gameObject.name, out var candidates) && candidates.Count > 0)
                {
                    var picked = candidates[0];
                    candidates.RemoveAt(0);
                    unmatchedDsts.Remove(picked);
                    pairs.Add((s, picked));
                }
                else
                {
                    stillUnmatchedSrcs.Add(s);
                }
            }

            if (stillUnmatchedSrcs.Count > 0 && unmatchedDsts.Count > 0)
            {
                var byMeshName = new Dictionary<string, List<T>>();
                foreach (var d in unmatchedDsts)
                {
                    var dm = meshGetter(d);
                    if (dm == null || string.IsNullOrEmpty(dm.name)) continue;
                    if (!byMeshName.TryGetValue(dm.name, out var list))
                    {
                        list = new List<T>();
                        byMeshName[dm.name] = list;
                    }
                    list.Add(d);
                }

                var unresolved = new List<T>();
                foreach (var s in stillUnmatchedSrcs)
                {
                    var sm = meshGetter(s);
                    if (sm != null && !string.IsNullOrEmpty(sm.name)
                        && byMeshName.TryGetValue(sm.name, out var candidates) && candidates.Count > 0)
                    {
                        var picked = candidates[0];
                        candidates.RemoveAt(0);
                        unmatchedDsts.Remove(picked);
                        pairs.Add((s, picked));
                    }
                    else
                    {
                        unresolved.Add(s);
                    }
                }
                stillUnmatchedSrcs = unresolved;
            }

            int n2 = Mathf.Min(stillUnmatchedSrcs.Count, unmatchedDsts.Count);
            for (int i = 0; i < n2; i++)
                pairs.Add((stillUnmatchedSrcs[i], unmatchedDsts[i]));

            if (stillUnmatchedSrcs.Count != unmatchedDsts.Count)
            {
                Debug.LogWarning($"Renderer pairing: {stillUnmatchedSrcs.Count} source vs {unmatchedDsts.Count} target left unmatched after name and mesh passes — some renderer state may not transfer correctly.");
            }

            foreach (var (src, dst) in pairs)
                syncFn(src, dst);
        }

        private static void SyncSkinnedRenderer(SkinnedMeshRenderer src, SkinnedMeshRenderer dst)
        {
            dst.sharedMaterials = src.sharedMaterials;
            dst.enabled         = src.enabled;
            CopyCommonRendererProps(src, dst);

            dst.updateWhenOffscreen  = src.updateWhenOffscreen;
            dst.skinnedMotionVectors = src.skinnedMotionVectors;
            dst.quality              = src.quality;
            dst.localBounds          = src.localBounds;
            dst.gameObject.SetActive(src.gameObject.activeSelf);

            if (dst.sharedMesh != null && src.sharedMesh != null)
            {
                int n = Mathf.Min(dst.sharedMesh.blendShapeCount, src.sharedMesh.blendShapeCount);
                for (int i = 0; i < n; i++)
                    dst.SetBlendShapeWeight(i, src.GetBlendShapeWeight(i));
            }
        }

        private static void SyncMeshRenderer(MeshRenderer src, MeshRenderer dst)
        {
            dst.sharedMaterials = src.sharedMaterials;
            dst.enabled         = src.enabled;
            CopyCommonRendererProps(src, dst);

            dst.gameObject.SetActive(src.gameObject.activeSelf);
        }

        private static void CopyCommonRendererProps(Renderer src, Renderer dst)
        {
            dst.shadowCastingMode       = src.shadowCastingMode;
            dst.receiveShadows          = src.receiveShadows;
            dst.lightProbeUsage         = src.lightProbeUsage;
            dst.reflectionProbeUsage    = src.reflectionProbeUsage;
            dst.probeAnchor             = src.probeAnchor;
            dst.lightProbeProxyVolumeOverride = src.lightProbeProxyVolumeOverride;
            dst.motionVectorGenerationMode    = src.motionVectorGenerationMode;
            dst.allowOcclusionWhenDynamic     = src.allowOcclusionWhenDynamic;
            dst.renderingLayerMask      = src.renderingLayerMask;
            dst.rendererPriority        = src.rendererPriority;
            dst.sortingLayerID          = src.sortingLayerID;
            dst.sortingOrder            = src.sortingOrder;
        }

        private static bool IsTypicallyUniqueOnGameObject(System.Type t)
        {
            return typeof(Animator).IsAssignableFrom(t)
                || typeof(Animation).IsAssignableFrom(t)
                || typeof(Rigidbody).IsAssignableFrom(t)
                || typeof(Rigidbody2D).IsAssignableFrom(t);
        }

        private static void ApplyRebuiltMeshes(GameObject target, GameObject source, List<MeshSlot> slots)
        {
            var targetByPath = new Dictionary<string, Transform>();
            foreach (var t in target.GetComponentsInChildren<Transform>(true))
                targetByPath[MeshExtractorUtils.GetPath(t, target.transform)] = t;

            Transform MapBone(Transform src)
            {
                if (src == null) return null;
                var path = MeshExtractorUtils.GetPath(src, source.transform);
                return targetByPath.TryGetValue(path, out var found) ? found : null;
            }

            foreach (var slot in slots)
            {
                var targetT = MapBone(slot.Owner.transform);
                if (targetT == null) continue;

                if (slot.IsSkinned)
                {
                    var smr = targetT.GetComponent<SkinnedMeshRenderer>();
                    if (smr == null) continue;

                    var origSmr = (SkinnedMeshRenderer)slot.Component;

                    var origBones = origSmr.bones;
                    if (origBones != null && origBones.Length > 0)
                    {
                        var newBones = new Transform[origBones.Length];
                        for (int i = 0; i < origBones.Length; i++)
                            newBones[i] = MapBone(origBones[i]);
                        smr.bones = newBones;
                    }

                    var newRoot = MapBone(origSmr.rootBone);
                    if (newRoot != null) smr.rootBone = newRoot;

                    smr.sharedMesh = slot.Rebuilt;

                    if (slot.Rebuilt != null && slot.Rebuilt.blendShapeCount > 0 && origSmr.sharedMesh != null)
                    {
                        for (int i = 0; i < slot.Rebuilt.blendShapeCount && i < origSmr.sharedMesh.blendShapeCount; i++)
                            smr.SetBlendShapeWeight(i, origSmr.GetBlendShapeWeight(i));
                    }
                }
                else
                {
                    var mf = targetT.GetComponent<MeshFilter>();
                    if (mf != null) mf.sharedMesh = slot.Rebuilt;
                }
            }
        }
    }
}
