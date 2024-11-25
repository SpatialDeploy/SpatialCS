using UnityEngine;

public class JinxController : MonoBehaviour
{
    private Animator m_animator = null;
    private bool m_looking = false;

    void Start()
    {
        m_animator = GetComponent<Animator>();

        if(m_animator != null)
            m_animator.SetBool("IsCheering", true);
    }

    void Update()
    {
        if(m_animator == null)
            return;

        RaycastHit hit;
        if(Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out hit, 100f) && 
            hit.transform == transform && !m_looking)
        {
            m_animator.SetTrigger("Greet");
            m_looking = true;
        }
        else
        {
            m_animator.ResetTrigger("Greet");
            m_looking = false;
        }
    }
}