using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Renderer), typeof(MeshFilter))]
public class PlayheadController : MonoBehaviour
{
	private SpatialCSplayerControls m_controller = null;
	private Material m_material = null;

	private float minLocalX = 0.0f;
	private float maxLocalX = 1.0f;

	private IXRSelectInteractor m_curInteractor = null;

	//-------------------------//

	void Start()
	{
		GameObject parent = transform.parent.gameObject;
		if(parent == null)
		{
			Debug.LogWarning("Playhead has no parent");
			return;
		}

		m_controller = parent.GetComponent<SpatialCSplayerControls>();
		if(m_controller == null)
		{
			Debug.LogWarning("Parent to playhead does not have SpatialCSplayerControls behavior");
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
			m_material.SetFloat("_Threshold", m_controller.GetSpatialProgess());

		m_controller.SetAdjustingProgess(false);
		if(m_curInteractor != null && m_curInteractor is XRRayInteractor rayInteractor)
		{
			RaycastHit hit;
			if(rayInteractor.TryGetCurrent3DRaycastHit(out hit))
			{
				if(hit.transform == transform)
				{
					Vector3 localPosition = transform.InverseTransformPoint(hit.point);
					float localX = localPosition.x;
					localX = 1.0f - Mathf.InverseLerp(minLocalX, maxLocalX, localX);
					
					m_controller.SetAdjustingProgess(true);
					m_controller.SetSpatialProgress(localX);
				}
			}
		}
	}

	public void OnSelect(SelectEnterEventArgs args)
	{
		m_curInteractor = args.interactorObject;
	}

	public void OnDeselect()
	{
		m_curInteractor = null;
	}
}
