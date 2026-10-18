using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using Microsoft.Win32;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent (typeof(MeshCollider))]
public class MeshCombiner : MonoBehaviour
{
    public bool CombineOnStart = false;
    MeshFilter myMeshFilter;
    MeshCollider myRenderer;

    void Start()
    {
        if (CombineOnStart)
        {
        myRenderer = GetComponent<MeshCollider>();
        myMeshFilter = GetComponent<MeshFilter>();
        CombineStaticSceneMeshes();
        }
        
    }

    [ContextMenu("Combined Meshs")]
    void CombineStaticSceneMeshes()
    {
        myRenderer = GetComponent<MeshCollider>();
        myMeshFilter = GetComponent<MeshFilter>();
        MeshRenderer myMeshRenderer = GetComponent<MeshRenderer>();

        Dictionary<Material, List<CombineInstance>> materialToCombineMap = new Dictionary<Material, List<CombineInstance>>();
        List<Mesh> temporaryMeshCopies = new List<Mesh>();

        List<GameObject> sceneObjects = ObjectsInScene();

        foreach (GameObject sceneObj in sceneObjects)
        {
            // 1. Skip target combiner GameObject itself
            if (sceneObj == gameObject) continue;

            // 2. Safely check for AllowCombine component and verify Allow is true
            if (!sceneObj.TryGetComponent<AllowCombine>(out AllowCombine allow) || !allow.Allow)
            {
                continue;
            }

            

            if (sceneObj.TryGetComponent<MeshFilter>(out MeshFilter filter) &&
                sceneObj.TryGetComponent<MeshRenderer>(out MeshRenderer renderer))
            {
                Mesh sharedMesh = filter.sharedMesh;
                Material[] sharedMaterials = renderer.sharedMaterials;

                if (sharedMesh == null || sharedMaterials == null) continue;

                // Instantiate an isolated copy of the mesh to avoid modifying asset files
                Mesh meshCopy = Instantiate(sharedMesh);
                temporaryMeshCopies.Add(meshCopy);

                int subMeshCount = Mathf.Min(meshCopy.subMeshCount, sharedMaterials.Length);
                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    Material mat = sharedMaterials[sub];
                    if (mat == null) continue;

                    CombineInstance ci = new CombineInstance
                    {
                        mesh = meshCopy,
                        subMeshIndex = sub,
                        transform = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    };

                    if (!materialToCombineMap.ContainsKey(mat))
                    {
                        materialToCombineMap[mat] = new List<CombineInstance>();
                    }

                    materialToCombineMap[mat].Add(ci);
                }

                // Disable original renderer so it doesn't double-render
                renderer.gameObject.SetActive(false);
                myRenderer.sharedMesh = meshCopy;
            }
        }

        List<Material> finalMaterials = new List<Material>();
        List<CombineInstance> submeshCombines = new List<CombineInstance>();
        List<Mesh> intermediateSubmeshes = new List<Mesh>();

        // Step 1: Combine geometry per material into intermediate submeshes
        foreach (KeyValuePair<Material, List<CombineInstance>> entry in materialToCombineMap)
        {
            Mesh subMesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };

            subMesh.CombineMeshes(entry.Value.ToArray(), true, true);
            intermediateSubmeshes.Add(subMesh);

            CombineInstance subMeshCombine = new CombineInstance
            {
                mesh = subMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            };

            submeshCombines.Add(subMeshCombine);
            finalMaterials.Add(entry.Key);
        }

        // Step 2: Merge intermediate submeshes into final mesh
        Mesh finalCombinedMesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        finalCombinedMesh.CombineMeshes(submeshCombines.ToArray(), false, false);

        myMeshFilter.mesh = finalCombinedMesh;
        myMeshRenderer.sharedMaterials = finalMaterials.ToArray();

        // Cleanup temporary in-memory mesh copies to prevent RAM/VRAM leaks
        foreach (Mesh tempMesh in temporaryMeshCopies) Destroy(tempMesh);
        foreach (Mesh tempSubMesh in intermediateSubmeshes) Destroy(tempSubMesh);
    }

    List<GameObject> ObjectsInScene()
    {
        List<GameObject> allGameObjects = new List<GameObject>();
        Scene currentScene = SceneManager.GetActiveScene();

        GameObject[] rootObjects = currentScene.GetRootGameObjects();
        foreach (GameObject root in rootObjects)
        {
            allGameObjects.Add(root);
            CollectChildren(root.transform, allGameObjects);
        }

        return allGameObjects;
    }

    [ContextMenu("Setup")]
    void Setup()
    {
        List<GameObject> sceneObjects = ObjectsInScene();
        int addedCount = 0;

        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject == gameObject) continue;

            // Only add AllowCombine if it does not already exist
            if (!sceneObject.TryGetComponent<AllowCombine>(out _))
            {
                sceneObject.AddComponent<AllowCombine>();
                addedCount++;
            }
        }

        Debug.Log($"Setup complete. Added AllowCombine component to {addedCount} GameObjects.");
        Debug.Log("Please Allow Meshs that you want to combine");
    }

    void CollectChildren(Transform parent, List<GameObject> list)
    {
        foreach (Transform child in parent)
        {
            list.Add(child.gameObject);
            CollectChildren(child, list);
        }
    }

    [ContextMenu("Save")]
    public void SaveMeshAsAsset()
    {
#if UNITY_EDITOR
        myMeshFilter = GetComponent<MeshFilter>();
        if (myMeshFilter == null || myMeshFilter.sharedMesh == null)
        {
            Debug.LogWarning("No MeshFilter or Mesh found on this GameObject.");
            return;
        }

        // Opens a save dialog restricted to the project's Assets directory
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Mesh Asset",
            myMeshFilter.sharedMesh.name + "_Copy",
            "asset",
            "Select location to save mesh asset"
        );

        // User cancelled the dialog
        if (string.IsNullOrEmpty(path)) return;

        // Instantiate a fresh copy so you aren't referencing a built-in or scene-bound mesh
        Mesh meshToSave = Object.Instantiate(myMeshFilter.sharedMesh);

        AssetDatabase.CreateAsset(meshToSave, path);
        AssetDatabase.SaveAssets();
        Debug.Log("Mesh asset saved to: " + path);

        myMeshFilter.mesh = meshToSave;
        myRenderer.sharedMesh = meshToSave;
#endif
    }


}

    
