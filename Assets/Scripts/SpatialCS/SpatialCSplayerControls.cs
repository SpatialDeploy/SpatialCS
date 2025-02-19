using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpatialCSplayerControls : MonoBehaviour
{
	[SerializeField]
	private GameObject m_camera;
	private SpatialCSplayer m_player;

	private const float OFFSET = 0.1f;

	private float m_targetAngle = 0.0f;
	private float m_angle = 0.0f;

	//-------------------------//

	public void PauseButtonClicked()
	{
		if(m_player == null)
			return;

		m_player.PauseButtonClicked();
	}

	public bool IsSpatialPlaying()
	{
		if(m_player == null)
			return false;

		return m_player.IsPlaying();
	}

	public void SetSpatialProgress(float val)
	{
		if(m_player == null)
			return;

		m_player.SetProgress(val);
	}

	public void SetAdjustingProgess(bool adjusting)
	{
		if(m_player == null)
			return;

		m_player.SetAdjustingProgess(adjusting);
	}

	public float GetSpatialProgess()
	{
		if(m_player == null)
			return 0.0f;

		return m_player.GetProgress();
	}

	//-------------------------//

	private void Start()
	{
		GameObject parent = transform.parent.gameObject;
		if(parent == null)
		{
			Debug.LogWarning("spatial controls has no parent");
			return;
		}

		m_player = parent.GetComponent<SpatialCSplayer>();
		if(m_player == null)
		{
			Debug.LogWarning("spatial controls parent does not have SpatialCSplayer behavior");
			return;
		}

		Update();
		m_angle = m_targetAngle;
	}

	private void Update()
	{
		//if camera not found, set default position:
		//-----------------
		if(m_camera == null)
		{
			transform.localPosition = new Vector3(0.0f, -0.5f - OFFSET, 0.5f + OFFSET);
			transform.localEulerAngles = new Vector3(0.0f, 0.0f, 0.0f);
			transform.localScale = Vector3.one;
			transform.localScale = new Vector3 (1.0f / transform.lossyScale.x, 1.0f / transform.lossyScale.y, 1.0f / transform.lossyScale.z);
		
			return;
		}

		//calculate target angle:
		//-----------------
		Vector3 dist = m_camera.transform.position - transform.parent.position;
		dist.y = 0.0f;
		dist.Normalize();

		m_targetAngle = Vector3.Angle(dist, new Vector3(0.0f, 0.0f, 1.0f));
		if(dist.x < 0.0f)
			m_targetAngle = 360.0f - m_targetAngle;
		m_targetAngle -= transform.parent.eulerAngles.y;
		m_targetAngle += 45.0f;
		m_targetAngle -= m_targetAngle % 90.0f;

		m_targetAngle = (m_targetAngle + 360.0f) % 360.0f;		

		//update angle:
		//-----------------
		float angleDiff = (m_targetAngle - m_angle) % 360.0f;
		if (angleDiff < -180.0f)
			angleDiff += 360.0f;
		else if (angleDiff > 180.0f)
			angleDiff -= 360.0f;
	
		m_angle += angleDiff * (1.0f - Mathf.Pow(0.975f, 1000.0f * Time.deltaTime));
		m_angle = (m_angle + 360.0f) % 360.0f;
		
		//calculate position:
		//-----------------
		float x = Mathf.Sin(m_angle * Mathf.Deg2Rad) * (0.5f + OFFSET);
		float y = -0.5f - OFFSET;
		float z = Mathf.Cos(m_angle * Mathf.Deg2Rad) * (0.5f + OFFSET);

		Vector3Int spatialSize;
		if(m_player != null)
			spatialSize = m_player.GetSpatialResolution();
		else
			spatialSize = new Vector3Int(1, 1, 1);

		int maxSize = Math.Max(Math.Max(spatialSize.x, spatialSize.y), spatialSize.z);

		Vector3 position = new Vector3(x, y, z);
		position.x *= (float)spatialSize.x / maxSize;
		position.z *= (float)spatialSize.z / maxSize;

		//set transform:
		//-----------------
		transform.localPosition = position;
		transform.localEulerAngles = new Vector3(0.0f, m_angle, 0.0f);
		transform.localScale = Vector3.one;
		transform.localScale = new Vector3 (1.0f / transform.lossyScale.x, 1.0f / transform.lossyScale.y, 1.0f / transform.lossyScale.z);
	}
}
