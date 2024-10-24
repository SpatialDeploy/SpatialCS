using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//-------------------------//

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class PlayButtonController : MonoBehaviour
{
    private VideoControlsController m_controller = null;
    private MeshRenderer m_meshRenderer = null;
    private MeshFilter m_meshFilter = null;

    [SerializeField]
    private Mesh m_playMesh = null;
    [SerializeField]
    private Material m_playMaterial = null;
    [SerializeField]
    private Mesh m_pauseMesh = null;
    [SerializeField]
    private Material m_pauseMaterial = null;

    private bool m_playing = false;

    //-------------------------//

    private void Start()
    {
        GameObject parent = transform.parent.gameObject;
        if(parent == null)
        {
            Debug.LogWarning("Play button has no parent");
            return;
        }

        m_controller = parent.GetComponent<VideoControlsController>();
        if(m_controller == null)
        {
            Debug.LogWarning("Parent to play button does not have VideoControlsController behavior");
            return;
        }

        m_meshRenderer = GetComponent<MeshRenderer>();
        m_meshFilter = GetComponent<MeshFilter>();

        m_playing = m_controller.IsVideoPlaying();
        SetMesh();
    }

    private void Update()
    {
        if(m_controller == null)
            return;

        if(m_playing != m_controller.IsVideoPlaying())
        {
            m_playing = m_controller.IsVideoPlaying();
            SetMesh();
        }
    }

    private void SetMesh()
    {
        if(m_playing)
        {
            m_meshFilter.mesh = m_pauseMesh;
            m_meshRenderer.material = m_pauseMaterial;
        }
        else
        {
            m_meshFilter.mesh = m_playMesh;
            m_meshRenderer.material = m_playMaterial;
        }
    }

    public void OnSelect()
    {
        if(m_controller == null)
            return;

        m_controller.PauseButtonClicked();
    }
}
