using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MeshTools
{
    
    public enum FbxRigType
    {
       
        MatchSource,
        Humanoid,
        Generic,
        Legacy,
        None,
    }

    internal static class FbxExporter
    {
        public static bool IsFbxPackageInstalled()
        {
            return Type.GetType(
                "UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor") != null;
        }

        public static void TryExportFbx(GameObject go, string fbxPath)
        {
            
            var exporterType = Type.GetType(
                "UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
            if (exporterType == null)
            {
                Debug.LogWarning("FBX export skipped: Unity FBX Exporter package not installed. Install 'com.unity.formats.fbx' from Package Manager.");
                return;
            }

            try
            {
                var method = exporterType.GetMethod("ExportObject",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), typeof(UnityEngine.Object) },
                    null);
                if (method == null)
                {
                    Debug.LogWarning("FBX export skipped: FBX Exporter found but ExportObject(string, Object) method signature missing.");
                    return;
                }

                method.Invoke(null, new object[] { fbxPath, go });
            }
            catch (Exception e)
            {
                Debug.LogWarning("FBX export threw: " + e.GetType().Name + " — " + e.Message);
            }
        }

        
        public static ModelImporterAnimationType? DetectSourceRigType(GameObject source)
        {
            if (source == null) return null;

            var animator = source.GetComponentInChildren<Animator>(true);

            
            if (animator != null && animator.avatar != null)
            {
                var avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
                var importer = AssetImporter.GetAtPath(avatarPath) as ModelImporter;
                if (importer != null) return importer.animationType;
            }

            foreach (var smr in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var path = AssetDatabase.GetAssetPath(smr.sharedMesh);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null) return importer.animationType;
            }

            foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null) return importer.animationType;
            }

            
            if (animator != null && animator.avatar != null && animator.avatar.isValid)
            {
                return animator.avatar.isHuman
                    ? ModelImporterAnimationType.Human
                    : ModelImporterAnimationType.Generic;
            }

            return null;
        }

        
        public static bool SetRigTypeOnAsset(string fbxAssetPath, FbxRigType rigType, ModelImporterAnimationType fallbackIfNoSource = ModelImporterAnimationType.Generic)
        {
            if (string.IsNullOrEmpty(fbxAssetPath)) return false;

            AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("Rig type setup skipped: no ModelImporter at " + fbxAssetPath);
                return false;
            }

            ModelImporterAnimationType target;
            switch (rigType)
            {
                case FbxRigType.Humanoid: target = ModelImporterAnimationType.Human; break;
                case FbxRigType.Generic:  target = ModelImporterAnimationType.Generic; break;
                case FbxRigType.Legacy:   target = ModelImporterAnimationType.Legacy; break;
                case FbxRigType.None:     target = ModelImporterAnimationType.None; break;
                default:                  target = fallbackIfNoSource; break;
            }

            if (importer.animationType != target)
            {
                importer.animationType = target;
                if (target == ModelImporterAnimationType.Human)
                {
                    //Let Unity auto generate the avatar from this FBXs own skeleton
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
                importer.SaveAndReimport();
            }

            return true;
        }
    }
}
