using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
public class MouseEnterExitEnv : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{

    public Painting2d painting2d;
    public void OnPointerEnter(PointerEventData eventData)
    {
        painting2d.isMouseEnter = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        painting2d.isMouseEnter = false;
    }
}
