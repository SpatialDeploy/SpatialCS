using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VideoControlsController : MonoBehaviour
{
	[SerializeField]
	private GameObject m_camera;
	private VoxelVideoPlayer m_player;

	private const float OFFSET = 0.05f;

	//-------------------------//

	public void PauseButtonClicked()
	{
		if(m_player == null)
			return;

		m_player.PauseButtonClicked();
	}

	public bool IsVideoPlaying()
	{
		if(m_player == null)
			return false;

		return m_player.IsPlaying();
	}

	public void SetVideoProgress(float val)
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

	public float GetVideoProgess()
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
            Debug.LogWarning("Video controls has no parent");
            return;
        }

		m_player = parent.GetComponent<VoxelVideoPlayer>();
		if(m_player == null)
		{
			Debug.LogWarning("Video controls parent does not have VoxelVideoPlayer behavior");
			return;
		}
	}

	private void Update()
	{
		if(m_camera == null)
		{
			float yBase = -0.5f - OFFSET;
			float zBase =  0.5f + OFFSET;
			transform.localPosition = new Vector3(0.0f, yBase, zBase);
			return;
		}

		Vector3 dist = m_camera.transform.position - transform.parent.position;
		dist.y = 0.0f;
		dist.Normalize();

		float angle = Vector3.Angle(dist, new Vector3(0.0f, 0.0f, 1.0f));
		if(dist.x < 0.0f)
			angle = 360.0f - angle;
		angle -= transform.parent.eulerAngles.y;
		angle += 45.0f;
		angle -= angle % 90.0f;

		float x = Mathf.Sin(angle * Mathf.Deg2Rad);
		float y = -0.5f - OFFSET;
		float z = Mathf.Cos(angle * Mathf.Deg2Rad);

		transform.localPosition = new Vector3(x, y, z);
		transform.localEulerAngles = new Vector3(0.0f, angle, 0.0f);
	}
}
