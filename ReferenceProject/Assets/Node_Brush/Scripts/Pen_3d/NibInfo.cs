using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NibInfo : MonoBehaviour
{
    [HideInInspector]
    public PenBase pen;
    void Start()
    {
        pen = transform.GetComponentInParent<PenBase>();
    }

   
}
