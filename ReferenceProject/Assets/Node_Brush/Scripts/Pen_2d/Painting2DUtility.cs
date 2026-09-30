using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Painting2DUtility : MonoBehaviour
{
    public Painting2d painting;

    public Texture[] burshStyles;


    /// 设置画笔大小
    public void SetPenSize(float v)
    {
        painting.widthPower = v;
    }

    /// 设置画笔样式
    public void SetBurshStyle(int index)
    {
        index = index >= burshStyles.Length ? 0 : index;
        painting.brushTypeTexture = burshStyles[index];
    }

    /// 设置画笔颜色
    public void SetPenColor(Color color)
    {
        painting.brushColor = color;
    }

    /// 撤销
    public void Cancel()
    {
        painting.CanclePaint();
    }

    /// 清空
    public void Clear()
    {
        painting.OnClickClear();
    }

}
