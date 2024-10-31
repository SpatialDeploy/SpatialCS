using System;
using UnityEngine;

public class GhostController : MonoBehaviour
{   
    [SerializeField]
    private float m_radius = 1.0f;
    private Vector2 m_anchorPos;

    [SerializeField]
    private float m_minPosChangeTime = 3.0f;
    [SerializeField]
    private float m_maxPosChangeTime = 5.0f;
    private float m_nextPosChange;

    private Vector2 m_targetPos;
    private float m_targetAngle;

    //-------------------------//

    void Start()
    {
        m_anchorPos = new Vector2(transform.position.x, transform.position.z);
    
        m_nextPosChange = Time.time + UnityEngine.Random.Range(m_minPosChangeTime, m_maxPosChangeTime);

        m_targetPos = m_anchorPos;
        m_targetAngle = transform.rotation.y;
    }

    void Update()
    {
        if(Time.time >= m_nextPosChange)
        {
            float angle = UnityEngine.Random.Range(0.0f, 360.0f);
            float radius = UnityEngine.Random.Range(0.0f, m_radius);
            
            m_targetAngle = angle;
            m_targetPos = new Vector2(radius * Mathf.Sin(angle * Mathf.Deg2Rad), radius * Mathf.Cos(angle * Mathf.Deg2Rad)) + m_anchorPos;

            m_nextPosChange = Time.time + UnityEngine.Random.Range(m_minPosChangeTime, m_maxPosChangeTime);
        }

        //update position:
	    //-----------------
        Vector2 curPos = new Vector2(transform.position.x, transform.position.z);
        curPos += (m_targetPos - curPos) * (1.0f - Mathf.Pow(0.995f, 1000.0f * Time.deltaTime));

        transform.position = new Vector3(curPos.x, transform.position.y, curPos.y);

        //update angle:
	    //-----------------
        float curAngle = transform.eulerAngles.y - 90.0f;

		float angleDiff = (m_targetAngle - curAngle) % 360.0f;
		if (angleDiff < -180.0f)
			angleDiff += 360.0f;
		else if (angleDiff > 180.0f)
			angleDiff -= 360.0f;
	
		curAngle += angleDiff * (1.0f - Mathf.Pow(0.975f, 1000.0f * Time.deltaTime));
		curAngle = (curAngle + 360.0f) % 360.0f;

        transform.eulerAngles = new Vector3(0.0f, curAngle + 90.0f, 0.0f);
    }
}
