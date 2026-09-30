using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Paint2DDynamicSize : MonoBehaviour
{
    public Painting2d painting;
    public float MaxSize= 1.0f;
    public float rate = 5;

    public void Update()
    {

        if (painting.isMouseDown)
        {
            if (painting.widthPower<MaxSize)
                painting.widthPower += Time.deltaTime*rate;
        }
       else
        {

            painting.widthPower = 0;
        }

    }

  
}
