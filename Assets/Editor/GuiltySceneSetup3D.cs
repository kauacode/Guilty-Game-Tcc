using UnityEditor;
using UnityEngine;

/// <summary>
/// Ferramenta de setup da Fase 1 (migração 2D -> 3D).
/// Roda uma vez no Editor: instancia o FBX da sala, reposiciona a Main Camera
/// no assento do suspeito e recria as luzes documentadas no Blender.
/// Não salva a cena automaticamente — revise visualmente e salve com Ctrl+S.
/// </summary>
public static class GuiltySceneSetup3D
{
    private const string FbxPath = "Assets/Models/Environment/GUILTY_InterrogationRoom.fbx";
    private const string RoomInstanceName = "GUILTY_InterrogationRoom";

    [MenuItem("Guilty/Fase 1 - Setup Cena 3D")]
    public static void SetupScene()
    {
        GameObject roomInstance = GameObject.Find(RoomInstanceName);
        if (roomInstance == null)
        {
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[GuiltySetup] FBX não encontrado em {FbxPath}. Abra o projeto no Unity e aguarde o import antes de rodar este menu.");
                return;
            }

            roomInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            roomInstance.name = RoomInstanceName;
            roomInstance.transform.position = Vector3.zero;
            roomInstance.transform.rotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(roomInstance, "Instantiate Interrogation Room");
            Debug.Log("[GuiltySetup] Sala 3D instanciada na cena.");
        }
        else
        {
            Debug.Log("[GuiltySetup] Sala já existe na cena — pulando instanciação.");
        }

        Transform chairSuspect = FindDeepChild(roomInstance.transform, "PFB_Chair_Suspect");
        Transform chairDetective = FindDeepChild(roomInstance.transform, "PFB_Chair_Detective");

        if (chairSuspect == null || chairDetective == null)
        {
            Debug.LogError("[GuiltySetup] Não encontrei PFB_Chair_Suspect / PFB_Chair_Detective dentro do modelo importado. Abortando posicionamento de câmera e luzes.");
            return;
        }

        RepositionCamera(chairSuspect, chairDetective);
        SetupLights(roomInstance.transform, chairDetective);
        SetupRoomCollider(roomInstance);

        Debug.Log("[GuiltySetup] Fase 1 concluída. Revise visualmente (posição da câmera, intensidade das luzes) e salve a cena manualmente quando estiver satisfeito.");
    }

    private static void RepositionCamera(Transform chairSuspect, Transform chairDetective)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[GuiltySetup] Nenhuma Main Camera encontrada na cena.");
            return;
        }

        Undo.RecordObject(mainCam.transform, "Reposition Main Camera");
        Vector3 eyePosition = chairSuspect.position + Vector3.up * 1.2f;
        mainCam.transform.position = eyePosition;
        mainCam.transform.LookAt(chairDetective.position + Vector3.up * 1.0f);

        Debug.Log($"[GuiltySetup] Main Camera reposicionada para {eyePosition}, olhando para a cadeira do detetive.");
    }

    private static void SetupLights(Transform roomRoot, Transform chairDetective)
    {
        Transform lampRoot = FindDeepChild(roomRoot, "PFB_Prop_Lamp");
        Vector3 lampPos = lampRoot != null ? lampRoot.position : new Vector3(0f, 2.9f, 0f);

        CreateOrUpdateLight("Light_Interrogation_Main", LightType.Spot, lampPos,
            Quaternion.Euler(90f, 0f, 0f), new Color(1f, 0.75f, 0.45f), intensity: 8f, spotAngle: 52f, range: 6f);

        Vector3 fillDetectivePos = chairDetective.position + new Vector3(0f, 2.2f, 0f);
        CreateOrUpdateLight("Light_Fill_Detective", LightType.Point, fillDetectivePos,
            Quaternion.identity, new Color(0.55f, 0.75f, 0.85f), intensity: 1.5f, spotAngle: 0f, range: 4f);

        CreateOrUpdateLight("Light_Environment_Fill", LightType.Point, new Vector3(0f, 2.7f, 0f),
            Quaternion.identity, new Color(0.6f, 0.6f, 0.65f), intensity: 0.4f, spotAngle: 0f, range: 8f);

        Debug.Log("[GuiltySetup] Luzes criadas/atualizadas. Intensidades são um ponto de partida — calibre visualmente no Editor.");
    }

    private static void CreateOrUpdateLight(string name, LightType type, Vector3 pos, Quaternion rot, Color color, float intensity, float spotAngle, float range)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            go.AddComponent<Light>();
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        }

        Light light = go.GetComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        if (type == LightType.Spot)
        {
            light.spotAngle = spotAngle;
        }

        go.transform.position = pos;
        go.transform.rotation = rot;
    }

    private static void SetupRoomCollider(GameObject roomInstance)
    {
        Renderer[] renderers = roomInstance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[GuiltySetup] Nenhum Renderer encontrado — colisor da sala não foi criado.");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        GameObject colliderGo = GameObject.Find("RoomCollision");
        if (colliderGo == null)
        {
            colliderGo = new GameObject("RoomCollision");
            colliderGo.transform.SetParent(roomInstance.transform);
            colliderGo.AddComponent<BoxCollider>();
            Undo.RegisterCreatedObjectUndo(colliderGo, "Create RoomCollision");
        }

        BoxCollider box = colliderGo.GetComponent<BoxCollider>();
        colliderGo.transform.position = bounds.center;
        box.center = Vector3.zero;
        box.size = bounds.size;
        box.isTrigger = false;

        Debug.Log($"[GuiltySetup] Colisor da sala criado/atualizado: centro={bounds.center}, tamanho={bounds.size}");
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent;
        }

        foreach (Transform child in parent)
        {
            Transform result = FindDeepChild(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
