using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Renderer), typeof(MeshFilter))]
public class PlayheadController : MonoBehaviour
{
    private VideoControlsController m_controller = null;
    private Material m_material = null;

    private float minLocalX = 0.0f;
    private float maxLocalX = 1.0f;

    //-------------------------//

    void Start()
    {
        GameObject parent = transform.parent.gameObject;
        if(parent == null)
        {
            Debug.LogWarning("Playhead has no parent");
            return;
        }

        m_controller = parent.GetComponent<VideoControlsController>();
        if(m_controller == null)
        {
            Debug.LogWarning("Parent to playhead does not have VideoControlsController behavior");
            return;
        }

        Renderer renderer = GetComponent<Renderer>();
        m_material = renderer.material;
        if(m_material == null)
        {
            Debug.LogWarning("Playhead material not found");
            return;
        }

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        Bounds meshBounds = meshFilter.sharedMesh.bounds;
        m_material.SetFloat("_MinX", meshBounds.min.x);
        m_material.SetFloat("_MaxX", meshBounds.max.x);

        Vector3 min = renderer.bounds.min;
        Vector3 max = renderer.bounds.max;
        minLocalX = transform.InverseTransformPoint(new Vector3(min.x, transform.position.y, transform.position.z)).x;
        maxLocalX = transform.InverseTransformPoint(new Vector3(max.x, transform.position.y, transform.position.z)).x;
    }

    void Update()
    {
        if(m_controller == null)
            return;

        if(m_material != null)
            m_material.SetFloat("_Threshold", m_controller.GetVideoProgess());

        m_controller.SetAdjustingProgess(false);
        if(Input.GetMouseButton(0))
        {
            Vector3 mousePos = Input.mousePosition;

            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            RaycastHit hit;
            if(Physics.Raycast(ray, out hit))
            {
                if(hit.transform == transform)
                {
                    Vector3 localPosition = transform.InverseTransformPoint(hit.point);
                    float localX = localPosition.x;
                    localX = 1.0f - Mathf.InverseLerp(minLocalX, maxLocalX, localX);
                    
                    m_controller.SetAdjustingProgess(true);
                    m_controller.SetVideoProgress(localX);
                }
            }
        }
    }
}
