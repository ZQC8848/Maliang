using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PenColorHandler : MonoBehaviour
{
    public Color color=Color.black;

    private void OnTriggerEnter(Collider other)
    {
        if (other.transform.CompareTag("Finish"))
        {
            NibInfo info = other.GetComponent<NibInfo>();
            if (info != null)
                info.pen.SetBurshColor(color);
        }
    }
}
